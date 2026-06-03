# mkauth — Manual Endpoint Verification

**Purpose:** live wire-correctness proof against *real* counterparts — the ship gate the
protocol-fake rule requires. Reference-vector anchoring lives in the unit tests
(`tests/Winix.MkAuth.Tests`); this document is the end-to-end "a real server accepted our header"
layer.

`BIN` below is the `mkauth` binary (after `scoop`/`dotnet tool` install it's just `mkauth`; from a
build it's `src/mkauth/bin/Release/net10.0/<rid>/publish/mkauth.exe`). Examples use bash; the
`$(mkauth …)` command-substitution pattern is the intended usage. `mkauth` prints a literal-secret
warning to **stderr** when `literal:` is used — harmless here (public test creds), and it never
pollutes the header on stdout.

---

## Status

| Scheme | Real-counterpart proof | Status |
|--------|------------------------|--------|
| **OAuth 1.0a** | Postman Echo signature validator | ✅ **PASS** (2026-06-03) |
| **Basic** | Postman Echo `/basic-auth` | ✅ **PASS** (2026-06-03) |
| **Bearer** | httpbin `/bearer` | ✅ **PASS** (2026-06-03) |
| **JWT** | GitHub App (RS256) / Google service account, or local `openssl` verify | ⬜ pending (cred-gated; cred-free local proof available) |
| **Azure SharedKey** | Real storage account, or Azurite emulator | ⬜ pending (cred-free via Azurite available) |

---

## Cred-free proofs (no account needed — reproducible anytime)

### OAuth 1.0a — Postman Echo  ✅ PASS 2026-06-03
Postman Echo exposes a real OAuth-1.0a verification endpoint with published 2-legged test creds.
```bash
HDR=$("$BIN" oauth1 --method GET --url https://postman-echo.com/oauth1 \
        --consumer-key RKCGzna7bv9YD57c --consumer-secret 'literal:D+EdQ-gs$-%@2Nu7' 2>/dev/null)
curl -s -H "$HDR" https://postman-echo.com/oauth1
# Expect: {"status":"pass","message":"OAuth-1.0a signature verification was successful"}
```
A `status":"fail"` would mean the base-string/encoding/signature is wrong. (Confirmed PASS.)

### Basic — Postman Echo  ✅ PASS 2026-06-03
```bash
HDR=$("$BIN" basic --user postman --password 'literal:password' 2>/dev/null)
curl -s -H "$HDR" https://postman-echo.com/basic-auth
# Expect: {"authenticated":true}
```

### Bearer — httpbin  ✅ PASS 2026-06-03
```bash
HDR=$("$BIN" bearer --token 'literal:my-secret-token-123' 2>/dev/null)
curl -s -H "$HDR" https://httpbin.org/bearer
# Expect: {"authenticated": true, "token": "my-secret-token-123"}
```

### Azure SharedKey — Azurite emulator (cred-free)
The Azurite emulator ships a well-known dev account + key, so the SharedKey path can be proven with
no cloud account. **Note the x-ms-date / x-ms-version replay requirement** (see the wrinkle below).
```bash
# 1) Start Azurite (Docker):
docker run --rm -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0

# 2) Sign a "list containers" request and replay the signed headers on curl:
ACCT=devstoreaccount1
KEY='Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
DATE=$(date -u '+%a, %d %b %Y %H:%M:%S GMT')
VER=2021-08-06
HDR=$("$BIN" azure-storage --account "$ACCT" --key "literal:$KEY" --method GET \
        --url "http://127.0.0.1:10000/${ACCT}?comp=list" --x-ms-date "$DATE" --x-ms-version "$VER" 2>/dev/null)
curl -s -H "$HDR" -H "x-ms-date: $DATE" -H "x-ms-version: $VER" \
     "http://127.0.0.1:10000/${ACCT}?comp=list"
# Expect: HTTP 200 + <EnumerationResults …> XML. A 403 "AuthenticationFailed" = signature mismatch.
```

### JWT — local `openssl` verify (cred-free)
Not a network endpoint, but an independent verifier (openssl, not our code) over a token we mint.
```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out priv.pem
openssl rsa -in priv.pem -pubout -out pub.pem
JWT=$("$BIN" jwt --alg RS256 --key file:priv.pem --sub test --iat --exp 1h --value-only 2>/dev/null)
H=$(printf '%s' "$JWT" | cut -d. -f1); P=$(printf '%s' "$JWT" | cut -d. -f2); S=$(printf '%s' "$JWT" | cut -d. -f3)
# base64url-decode the signature, then verify with the public key:
printf '%s' "$S" | tr '_-' '/+' | sed 's/$/===/' | base64 -d > sig.bin 2>/dev/null
printf '%s.%s' "$H" "$P" | openssl dgst -sha256 -verify pub.pem -signature sig.bin
# Expect: "Verified OK"
```
Or paste the token at https://jwt.io and verify with the public key in the UI.

---

## Cred-gated proofs (fill in when you have access)

> The OAuth1 algorithm is already proven by Postman Echo above, so a production OAuth1 API only adds
> coverage of *that vendor's* parameter handling. JWT and Azure are the ones whose real-endpoint
> proof has not yet run at all.

### JWT — GitHub App (RS256, clean free option)
Create a GitHub App (Settings → Developer settings → GitHub Apps), download its private key (`.pem`),
note the App ID. GitHub requires `iss` = App ID, `iat` ~now, `exp` ≤ 10 min.
```bash
APP_ID='<FILL IN: numeric app id>'
curl -s -H "$("$BIN" jwt --alg RS256 --key file:<FILL IN: app-key.pem> \
              --iss "$APP_ID" --iat --exp 9m)" \
     -H "Accept: application/vnd.github+json" \
     https://api.github.com/app
# Expect: HTTP 200 + JSON describing the app (name, owner, …). 401 = bad signature/claims.
```

### JWT — Google service account (jwt-bearer grant)
Download a service-account JSON key; extract `private_key` (PEM) and `client_email`.
```bash
SA_EMAIL='<FILL IN: ...@...iam.gserviceaccount.com>'
JWT=$("$BIN" jwt --alg RS256 --key file:<FILL IN: sa-key.pem> \
        --iss "$SA_EMAIL" --sub "$SA_EMAIL" \
        --aud "https://oauth2.googleapis.com/token" \
        --claim scope=https://www.googleapis.com/auth/cloud-platform \
        --iat --exp 1h --value-only)
curl -s -X POST https://oauth2.googleapis.com/token \
     -d grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer \
     -d assertion="$JWT"
# Expect: {"access_token":"...","expires_in":3599,...}. An "invalid_grant"/"invalid_assertion" = bad JWT.
```

### Azure SharedKey — real storage account
```bash
ACCT='<FILL IN: account name>'
KEY='<FILL IN: account key (base64) — prefer a file: or env: ref, not literal:>'
DATE=$(date -u '+%a, %d %b %Y %H:%M:%S GMT')
VER=2021-08-06
HDR=$("$BIN" azure-storage --account "$ACCT" --key "file:<FILL IN: azkey.txt>" --method GET \
        --url "https://${ACCT}.blob.core.windows.net/?comp=list" --x-ms-date "$DATE" --x-ms-version "$VER")
curl -s -H "$HDR" -H "x-ms-date: $DATE" -H "x-ms-version: $VER" \
     "https://${ACCT}.blob.core.windows.net/?comp=list"
# Expect: HTTP 200 + <EnumerationResults> XML listing containers. 403 "AuthenticationFailed" = mismatch.
```

### OAuth 1.0a — a production API (optional, algorithm already proven)
e.g. Trello (`GET https://api.trello.com/1/members/me` with key+token), Discogs, or a WooCommerce
store's REST API. Pass `--consumer-key`/`--consumer-secret` (+ `--token`/`--token-secret` for
3-legged) and confirm a 200 with real data.

---

## The Azure x-ms-date / x-ms-version replay wrinkle (important)

`mkauth azure-storage` signs the `x-ms-date` and `x-ms-version` header *values* (they're part of the
StringToSign). When you feed the `Authorization` header to a separate `curl`, **curl must send the
exact same `x-ms-date` and `x-ms-version`** it was signed with — otherwise the server recomputes a
different StringToSign and returns 403. Always pass explicit `--x-ms-date`/`--x-ms-version` to
`mkauth` and replay the same values via `curl -H`. (If `--x-ms-date` is omitted, `mkauth` uses *now*,
which you then can't replay — so for the curl pattern, set it explicitly.)
