# mkauth

Compute HTTP Authorization headers (OAuth 1.0a, JWT, Basic, Bearer, Azure Storage) for `curl` and scripts. Single native binary, no runtime, cross-platform.

`mkauth` is a **pure, stateless calculator** — it prints one header line to stdout and exits. Use it in command substitution with `curl -H "$(mkauth …)"`:

```bash
curl -H "$(mkauth oauth1 --method GET --url https://api.example.com/v1/thing \
              --consumer-key abc --consumer-secret 'vault:api/consumer' \
              --token def --token-secret 'vault:api/token')" \
     https://api.example.com/v1/thing
```

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/mkauth
```

### Winget (Windows, stable releases)

```bash
winget install Winix.MkAuth
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.MkAuth
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
mkauth basic     --user NAME --password REF [options]
mkauth bearer    --token REF [options]
mkauth oauth1    --method VERB --url URL --consumer-key K --consumer-secret REF [options]
mkauth jwt       --alg ALG --key REF [claim flags] [options]
mkauth azure-storage --account NAME --key REF --method VERB --url URL [options]
```

Run `mkauth SUBCOMMAND --help` for subcommand-specific flags.

The computed header is written to **stdout** as a `Name: Value` line, suitable for `curl -H "$(…)"`. `--show-base-string` debug output goes to stderr. Warnings (literal-secret exposure, PLAINTEXT over non-HTTPS) go to stderr.

### secret references

Every secret-bearing flag takes a **secret reference** — a string with a scheme prefix. Secrets are never required as bare values on argv (which would expose them in shell history and `ps` output).

| Reference | Source | Notes |
|-----------|--------|-------|
| `env:NAME` | Environment variable `NAME` | Value read at runtime, not from argv. |
| `file:PATH` | File contents | Trailing `\r\n`/`\n` run trimmed; all other bytes preserved verbatim. |
| `vault:NS/KEY` | OS keychain via Winix.SecretStore | DPAPI (Windows) / Keychain (macOS) / libsecret (Linux). Splits on the **first** `/`; `NS` cannot contain `/`, `KEY` may. |
| `-` or `stdin` | Standard input | Safe; at most **one** secret per invocation may use stdin. |
| `literal:VALUE` | The literal value | **Emits a `ps`/history-exposure warning to stderr.** Explicit escape hatch only. |

**F6 note:** `vault:NS/KEY` splits on the *first* `/`. A namespace cannot contain `/`; a key may contain `/` characters after the first slash.

**F7 note:** `file:` and `stdin` secrets have a **single trailing `\r\n`/`\n` run trimmed**. All other bytes — including trailing spaces — are preserved verbatim. This matches the convention from `digest --key-file`.

### basic — Basic authentication

Computes `Authorization: Basic base64(user:password)` per RFC 7617.

```bash
# Hardcoded user, password from environment variable
mkauth basic --user alice --password env:API_PASSWORD

# Both from the OS keychain
mkauth basic --user alice --password 'vault:api/password'

# Feed to curl
curl -H "$(mkauth basic --user alice --password env:API_PASSWORD)" https://api.example.com/data
```

#### basic options

| Flag | Required | Description |
|------|----------|-------------|
| `--user NAME` | yes | Username. A username containing `:` is a usage error. |
| `--password REF` | yes | Secret reference for the password. |

### bearer — Bearer token

Outputs `Authorization: Bearer <token>` per RFC 6750. Pure passthrough — the resolved token value is used as-is.

```bash
# Token from environment variable
mkauth bearer --token env:ACCESS_TOKEN

# Token from keychain
mkauth bearer --token 'vault:myapp/token'

# Feed to curl
curl -H "$(mkauth bearer --token env:ACCESS_TOKEN)" https://api.example.com/data
```

#### bearer options

| Flag | Required | Description |
|------|----------|-------------|
| `--token REF` | yes | Secret reference for the bearer token. |

### oauth1 — OAuth 1.0a signed header *(flagship)*

Computes the full OAuth 1.0a `Authorization` header (RFC 5849) including the percent-encoded signature base string, signing key, and HMAC or PLAINTEXT signature.

**Scope note:** `mkauth oauth1` *signs* a request given credentials. It does **not** perform the OAuth 1.0a 3-legged request-token→authorize→access-token dance. Token acquisition is a separate flow, out of scope for this tool.

```bash
# 2-legged (no token) with HMAC-SHA1
mkauth oauth1 --method POST --url https://api.example.com/data \
              --consumer-key mykey --consumer-secret 'vault:myapp/consumer-secret'

# 3-legged with HMAC-SHA256
mkauth oauth1 --method GET --url 'https://api.example.com/timeline?count=10' \
              --consumer-key mykey --consumer-secret env:CONSUMER_SECRET \
              --token mytoken --token-secret env:TOKEN_SECRET \
              --signature-method HMAC-SHA256

# Include body params in signature (application/x-www-form-urlencoded bodies)
mkauth oauth1 --method POST --url https://api.example.com/post \
              --consumer-key k --consumer-secret env:CS \
              --param "status=Hello World" --param "source=myapp"

# Show the computed signature base string (debug)
mkauth oauth1 --method GET --url https://api.example.com/thing \
              --consumer-key k --consumer-secret env:CS \
              --show-base-string

# Feed to curl
curl -H "$(mkauth oauth1 --method GET --url https://api.example.com/v1/me \
              --consumer-key abc --consumer-secret 'vault:api/consumer' \
              --token def --token-secret 'vault:api/token')" \
     https://api.example.com/v1/me
```

#### oauth1 options

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--method VERB` | yes | — | HTTP method (upper-cased for the base string). |
| `--url URL` | yes | — | Full request URL. Query params are parsed out and included in the signature base. |
| `--consumer-key K` | yes | — | `oauth_consumer_key`. |
| `--consumer-secret REF` | yes | — | Secret reference for the consumer secret. |
| `--token T` | no | — | `oauth_token` (omit for 2-legged). |
| `--token-secret REF` | no | empty | Secret reference for the token secret. Empty = 2-legged. |
| `--signature-method M` | no | `HMAC-SHA1` | Signature method: `HMAC-SHA1`, `HMAC-SHA256`, `PLAINTEXT`. |
| `--param k=v` | no | — | Extra/body params for the signature base (repeatable). |
| `--realm R` | no | — | `realm` value (not part of the signature base). |
| `--timestamp N` | no | auto | `oauth_timestamp` (Unix seconds). Override for reproducibility. |
| `--nonce S` | no | auto | `oauth_nonce`. Override for reproducibility. |

#### Signature algorithm

The signature base string is `METHOD & pct(base_url) & pct(normalized_params)`, where:
- `base_url` = scheme + authority + path (scheme/host lowercased, default port dropped, no query/fragment)
- `normalized_params` = RFC 3986 percent-encoded, lexicographically-sorted, `&`-joined merge of URL query params + `--param` params + `oauth_*` params (excluding `realm` and `oauth_signature`)
- Signing key = `pct(consumer_secret) & pct(token_secret)`

**PLAINTEXT warning:** `PLAINTEXT` sends the signing key as the signature. `mkauth` warns to stderr if the URL scheme is not `https`.

### jwt — JWT minting and signing

Mints a JSON Web Token (RFC 7519) and wraps it as `Authorization: Bearer <jwt>`. Useful for service-account auth (Google service accounts, GitHub Apps) that require a freshly signed JWT per call.

```bash
# HS256 with a shared secret
mkauth jwt --alg HS256 --key 'vault:myapp/jwt-secret' \
           --iss myapp --sub user123 --exp 1h

# RS256 with a PEM private key file
mkauth jwt --alg RS256 --key file:/path/to/private.pem \
           --iss myapp@project.iam.gserviceaccount.com --sub api@example.com \
           --aud https://api.example.com/ --exp 1h --iat

# ES256 with key from keychain
mkauth jwt --alg ES256 --key 'vault:apps/jwt-key' \
           --claim scope=read:data --claim-num priority=1 \
           --kid my-key-id

# Arbitrary claims from a JSON file
mkauth jwt --alg HS256 --key env:JWT_SECRET \
           --claims-file /path/to/claims.json --exp 15m

# Bare JWT value only (no 'Authorization: Bearer ' prefix)
mkauth jwt --alg HS256 --key env:JWT_SECRET --sub user --exp 1h --value-only

# Feed to curl
curl -H "$(mkauth jwt --alg RS256 --key file:private.pem \
              --iss svc@project.iam.gserviceaccount.com \
              --aud https://api.example.com/ --exp 1h --iat)" \
     https://api.example.com/data
```

#### jwt options

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--alg ALG` | yes | `HS256` | Signing algorithm: `HS256/384/512`, `RS256/384/512`, `ES256/384/512`. |
| `--key REF` | yes | — | HS: shared secret. RS/ES: PEM private key (use `file:` or `vault:`). |
| `--claim k=v` | no | — | String claim (repeatable). |
| `--claim-num k=v` | no | — | Numeric claim — `v` is parsed as a number (for NumericDate fields). |
| `--claim-json k=v` | no | — | Raw JSON claim — `v` is parsed as raw JSON. |
| `--claims-file PATH` | no | — | JSON object file merged into the claim set. |
| `--claims-stdin` | no | off | Merge a JSON object from stdin. Mutually exclusive with `--key stdin`. |
| `--iss S` | no | — | `iss` (issuer) standard claim. |
| `--sub S` | no | — | `sub` (subject) standard claim. |
| `--aud S` | no | — | `aud` (audience) standard claim. |
| `--exp DURATION` | no | — | `exp` = now + DURATION (e.g. `1h`, `30m`, `3600s`). |
| `--iat` | no | off | Set `iat` to now. |
| `--nbf DURATION` | no | — | `nbf` = now + DURATION. |
| `--kid S` | no | — | `kid` JOSE header parameter. |
| `--header k=v` | no | — | Additional JOSE header parameters (repeatable). |

### azure-storage — Azure Storage SharedKey

Computes the `Authorization: SharedKey account:signature` header for Azure Storage REST API requests (Blob, Queue, and File services).

```bash
# Sign a Blob GET request
mkauth azure-storage --account mystorageacct \
                     --key env:STORAGE_ACCOUNT_KEY \
                     --method GET \
                     --url 'https://mystorageacct.blob.core.windows.net/mycontainer/myblob' \
                     --header 'x-ms-version:2023-11-03'

# Show the StringToSign (debug)
mkauth azure-storage --account mystorageacct --key env:STORAGE_KEY \
                     --method PUT --url 'https://mystorageacct.blob.core.windows.net/c/b' \
                     --header 'Content-Type:application/octet-stream' \
                     --show-base-string

# Feed to curl (note: x-ms-date and x-ms-version must match the headers you send)
DATE=$(date -u '+%a, %d %b %Y %H:%M:%S GMT')
curl -H "x-ms-date: $DATE" \
     -H "x-ms-version: 2023-11-03" \
     -H "$(mkauth azure-storage --account mystorageacct --key env:STORAGE_KEY \
               --method GET --url 'https://mystorageacct.blob.core.windows.net/c/b' \
               --header "x-ms-date:$DATE" --header 'x-ms-version:2023-11-03')" \
     'https://mystorageacct.blob.core.windows.net/c/b'
```

#### azure-storage options

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--account NAME` | yes | — | Storage account name. |
| `--key REF` | yes | — | Account key (base64-encoded); decoded to the HMAC key. |
| `--method VERB` | yes | — | HTTP method. |
| `--url URL` | yes | — | Request URL → canonicalized resource (`/account/path` + sorted query). |
| `--x-ms-date S` | no | auto (RFC1123 GMT) | The `x-ms-date` header value. Must match the value sent with the request. |
| `--x-ms-version V` | no | pinned default | `x-ms-version` header value. |
| `--header k:v` | no | — | Additional headers (repeatable). Used for fixed StringToSign headers (Content-Type, Content-Length, …) and `x-ms-*` CanonicalizedHeaders. |

### Common flags (all subcommands)

| Flag | Description |
|------|-------------|
| `--value-only` | Print the header value only (no `Authorization: ` prefix). Useful for `curl --oauth2-bearer "$(mkauth bearer --value-only …)"` or for scripting. |
| `--json` | Emit a JSON envelope to stdout: `{"scheme":"…","header_name":"Authorization","header_value":"…"}`. With `--show-base-string`, adds `"base_string":"…"`. |
| `--show-base-string` | Emit the computed signature base string or StringToSign to stderr (or in `--json`). Applies to `oauth1` and `azure-storage`. |
| `--describe` | Emit structured JSON metadata for AI discoverability. |
| `--help`, `-h` | Show help and exit. |
| `--version`, `-v` | Show version and exit. |
| `--color[=auto\|always\|never]` | Force coloured output; bare `--color` = always. |
| `--no-color` | Disable coloured output. Respects `NO_COLOR`. |

## Composability with curl

The primary use pattern — command substitution feeding `curl -H`:

```bash
# OAuth 1.0a
curl -H "$(mkauth oauth1 --method POST --url https://api.example.com/data \
              --consumer-key k --consumer-secret env:CS \
              --token t --token-secret env:TS)" \
     -d '{"key":"value"}' https://api.example.com/data

# JWT service account
curl -H "$(mkauth jwt --alg RS256 --key file:service-account.pem \
              --iss svc@project.iam.gserviceaccount.com \
              --aud https://api.example.com/ --exp 1h --iat)" \
     https://api.example.com/data

# Basic auth (keep password out of shell history)
curl -H "$(mkauth basic --user alice --password 'vault:myapp/password')" \
     https://api.example.com/protected

# Bearer token from keychain
curl -H "$(mkauth bearer --token 'vault:myapp/access-token')" \
     https://api.example.com/api
```

## For other HMAC schemes, use `digest`

`mkauth` handles OAuth 1.0a, JWT, Basic, Bearer, and Azure SharedKey. For other body-based HMAC schemes (Stripe webhooks, GitHub webhooks, generic `timestamp.body` patterns), use `digest --hmac` directly:

```bash
# Generic HMAC-SHA256 of a body (Stripe / GitHub style)
printf '%s.%s' "$ts" "$body" | digest --hmac sha256 --key-env WEBHOOK_SECRET --base64

# Stripe webhook signature
echo -n "${ts}.${payload}" | digest --hmac sha256 --key-env STRIPE_WEBHOOK_SECRET --base64

# HMAC over a file
digest --hmac sha256 --key-file signing.key payload.json
```

**AWS SigV4** is covered by `curl --aws-sigv4` (built in to curl). `mkauth` does not duplicate it.

## Storing secrets in the keychain

Use `envvault` to store secrets in the OS keychain and pass them to `mkauth` via `vault:` references:

```bash
# Store once
envvault set myapp consumer-secret "my-consumer-secret-value"

# Use in mkauth (secret never touches argv or shell history)
mkauth oauth1 --method GET --url https://api.example.com/me \
              --consumer-key mykey --consumer-secret 'vault:myapp/consumer-secret'
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success. The computed header was written to stdout. |
| 125 | Usage error — unknown subcommand, missing required flag, invalid flag value, conflicting flags (e.g. two stdin references), or unexpected positional. Stderr carries the message. |
| 126 | Runtime error — keychain access failure, key file not found, signing error, or output write failure. Stderr carries the message. |

## Colour

`mkauth` output is plain (no coloured output). The `--color` and `--no-color` flags are accepted for suite consistency. `NO_COLOR` is respected.

## Related Tools

- [`envvault`](../envvault/README.md) — store secrets in the OS keychain and inject them as env vars: `vault:NS/KEY` references in `mkauth` read from the same store
- [`digest`](../digest/README.md) — HMAC and cryptographic hashing for body/payload signing schemes
- [`mksecret`](../mksecret/README.md) — generate HMAC signing keys, API keys, and other secrets

## See Also

- `man mkauth` (after `winix install man`)
- `mkauth --describe` for JSON metadata
