# mkauth — AI Agent Guide

## What This Tool Does

`mkauth` computes HTTP `Authorization` headers and prints them to stdout — one header per invocation. It is a **pure, stateless calculator**: no network I/O, no token storage, no caching. Subcommands: `basic`, `bearer`, `oauth1`, `jwt`, `azure-storage`.

Primary use pattern: command substitution feeding `curl -H`:

```bash
curl -H "$(mkauth oauth1 --method GET --url https://api.example.com/v1/me \
              --consumer-key abc --consumer-secret 'vault:api/consumer' \
              --token def --token-secret 'vault:api/token')" \
     https://api.example.com/v1/me
```

## When to Use This

- Signing an OAuth 1.0a request for a curl call — curl has no OAuth 1.0a signing built in
- Minting a signed JWT for service-account auth (Google service accounts, GitHub Apps, Apple Sign-in)
- Computing an Azure Storage SharedKey header for a REST API call
- Basic or Bearer auth where the credential lives in the OS keychain and must not appear in argv/history
- Any context where secrets should come from `vault:` (OS keychain), `env:`, `file:`, or `stdin` — not bare argv

## When NOT to Use This

- **OAuth 1.0a 3-legged token acquisition** — `mkauth oauth1` signs a request with credentials you already hold; it does not perform the request-token→authorize→access-token flow. That is a separate tool not yet in the suite.
- **OAuth2 token acquisition** (client-credentials, authorization-code, device-code) — `mkauth` attaches a token you already have (`bearer`); it does not fetch one.
- **AWS SigV4** — `curl --aws-sigv4` already handles this; no gap to fill.
- **Generic body HMAC** (Stripe webhooks, GitHub webhooks) — use `digest --hmac` directly; see "Other HMAC schemes" below.
- **Verifying a signature** — `mkauth` produces; it does not verify. Use `digest --verify`.

## Basic Invocation

```bash
# Basic auth (password from keychain)
mkauth basic --user alice --password 'vault:myapp/password'

# Bearer token from env var
mkauth bearer --token env:ACCESS_TOKEN

# OAuth 1.0a 2-legged (no user token)
mkauth oauth1 --method POST --url https://api.example.com/data \
              --consumer-key mykey --consumer-secret env:CONSUMER_SECRET

# OAuth 1.0a 3-legged with HMAC-SHA256
mkauth oauth1 --method GET --url 'https://api.example.com/timeline?count=10' \
              --consumer-key mykey --consumer-secret env:CONSUMER_SECRET \
              --token mytoken --token-secret env:TOKEN_SECRET \
              --signature-method HMAC-SHA256

# JWT HS256 with 1h expiry
mkauth jwt --alg HS256 --key 'vault:myapp/jwt-secret' \
           --iss myapp --sub user123 --exp 1h --iat

# JWT RS256 from a PEM file
mkauth jwt --alg RS256 --key file:service-account.pem \
           --iss svc@project.iam.gserviceaccount.com \
           --aud https://api.example.com/ --exp 1h --iat

# Azure Storage SharedKey
mkauth azure-storage --account mystorageacct --key env:STORAGE_KEY \
                     --method GET --url 'https://mystorageacct.blob.core.windows.net/c/b' \
                     --header 'x-ms-version:2023-11-03'
```

## JSON Output

Pass `--json` for machine-parseable output:

```bash
mkauth bearer --token env:ACCESS_TOKEN --json
```

Output shape:
```json
{"scheme": "bearer", "header_name": "Authorization", "header_value": "Bearer eyJ..."}
```

With `--show-base-string` on `oauth1` or `azure-storage`, adds `"base_string": "..."`.

## Secret References

Every secret-bearing flag accepts a secret reference — never a bare value on argv:

| Reference | Source | Notes |
|-----------|--------|-------|
| `env:NAME` | Environment variable | Safe — not in argv/history |
| `file:PATH` | File contents | Trailing `\r\n`/`\n` run trimmed; all other bytes preserved |
| `vault:NS/KEY` | OS keychain | DPAPI / Keychain / libsecret; splits on first `/` |
| `-` or `stdin` | Standard input | At most one per invocation |
| `literal:VALUE` | Literal value | **Emits a ps/history warning to stderr** |

`vault:NS/KEY` splits on the **first** `/`. A namespace cannot contain `/`; a key may.

## Output Flags

- Default: `Authorization: Value` (full header line, for `curl -H "$(…)"`)
- `--value-only`: bare value only (no prefix)
- `--json`: JSON envelope on stdout
- `--show-base-string`: OAuth1 signature base / Azure StringToSign; to stderr in plain mode, in envelope in JSON mode

## Other HMAC Schemes (use `digest`, not `mkauth`)

```bash
# Generic HMAC-SHA256 over a body
printf '%s.%s' "$ts" "$body" | digest --hmac sha256 --key-env WEBHOOK_SECRET --base64

# Stripe/GitHub webhook style
echo -n "${ts}.${payload}" | digest --hmac sha256 --key-env WEBHOOK_SECRET --base64

# File HMAC
digest --hmac sha256 --key-file signing.key payload.json
```

AWS SigV4: use `curl --aws-sigv4` — it is built in to curl.

## Storing Secrets in the Keychain

```bash
# Store once with envvault
envvault set myapp consumer-secret "my-secret-value"

# Use via vault: reference (never touches argv)
mkauth oauth1 --method GET --url https://api.example.com/me \
              --consumer-key mykey --consumer-secret 'vault:myapp/consumer-secret'
```

## Composability

```bash
# OAuth 1.0a with curl
curl -H "$(mkauth oauth1 --method POST --url https://api.example.com/data \
              --consumer-key k --consumer-secret env:CS \
              --token t --token-secret env:TS)" \
     -d '{"key":"value"}' https://api.example.com/data

# JWT for Google service account API call
curl -H "$(mkauth jwt --alg RS256 --key file:service-account.pem \
              --iss svc@project.iam.gserviceaccount.com \
              --aud https://www.googleapis.com/oauth2/v4/token --exp 1h --iat)" \
     https://www.googleapis.com/oauth2/v4/token

# Bare JWT value (e.g. for another tool)
mkauth jwt --alg HS256 --key env:SECRET --sub user --exp 1h --value-only

# JSON envelope for scripted pipelines
mkauth bearer --token env:TOKEN --json | jq -r '.header_value'
```

## Platform Notes

`mkauth` is fully cross-platform — Windows, Linux, macOS. All subcommands, flags, and exit codes are identical everywhere. `vault:` references use DPAPI on Windows, Keychain on macOS, and libsecret on Linux.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success. The computed header was written to stdout. |
| 125 | Usage error — unknown subcommand, missing required flag, invalid flag value, conflicting stdin references, or unexpected positional. Stderr carries the message. |
| 126 | Runtime error — keychain access failure, key file not found, signing error, or output write failure. Stderr carries the message. |

## Metadata

Run `mkauth --describe` for full structured metadata (subcommands, flags, examples, composability, platform scope).
Run `mkauth SUBCOMMAND --help` for subcommand-specific help.
