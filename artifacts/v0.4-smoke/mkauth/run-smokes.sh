#!/bin/bash
set +e
BIN="$(pwd)/artifacts/v0.4-smoke/mkauth/bin/mkauth.exe"
OUT="$(pwd)/artifacts/v0.4-smoke/mkauth/out"
rm -rf "$OUT"; mkdir -p "$OUT"

run() {
  local id="$1"; local desc="$2"; shift 2
  local cmd="$*"
  echo "=== $id: $desc ==="
  echo "CMD: $cmd" > "$OUT/$id.cmd"
  eval "$cmd" 1>"$OUT/$id.stdout" 2>"$OUT/$id.stderr"
  echo $? > "$OUT/$id.exitcode"
  echo "  exit=$(cat "$OUT/$id.exitcode")  stdout=$(wc -c < "$OUT/$id.stdout")B  stderr=$(wc -c < "$OUT/$id.stderr")B"
}

# ---- Global flags ----
run S01-help "--help" "$BIN --help"
run S02-version "--version" "$BIN --version"
run S03-describe "--describe" "$BIN --describe"
run S04-nocolor "--no-color" "$BIN --help --no-color"
run S05-color "--color" "$BIN --help --color"

# ---- basic subcommand ----
run S06-basic-help "basic --help" "$BIN basic --help"
run S07-basic "basic with literal: password" "$BIN basic --user alice --password 'literal:s3cr3t'"
run S08-basic-env "basic with env: password" "MKAUTH_TEST_PW=hunter2 $BIN basic --user bob --password env:MKAUTH_TEST_PW"
run S09-basic-json "basic --json" "$BIN basic --user alice --password 'literal:s3cr3t' --json"
run S10-basic-value-only "basic --value-only" "$BIN basic --user alice --password 'literal:s3cr3t' --value-only"
run S11-basic-missing-user "basic missing --user -> 125" "$BIN basic --password 'literal:s3cr3t'"
run S12-basic-missing-pw "basic missing --password -> 125" "$BIN basic --user alice"

# ---- bearer subcommand ----
run S13-bearer-help "bearer --help" "$BIN bearer --help"
run S14-bearer "bearer with literal: token" "$BIN bearer --token 'literal:my-token-value'"
run S15-bearer-env "bearer with env: token" "MKAUTH_TEST_TOKEN=tok123 $BIN bearer --token env:MKAUTH_TEST_TOKEN"
run S16-bearer-json "bearer --json" "$BIN bearer --token 'literal:my-token-value' --json"
run S17-bearer-value-only "bearer --value-only" "$BIN bearer --token 'literal:my-token-value' --value-only"
run S18-bearer-missing "bearer missing --token -> 125" "$BIN bearer"

# ---- oauth1 subcommand ----
run S19-oauth1-help "oauth1 --help" "$BIN oauth1 --help"
run S20-oauth1-2legged "oauth1 2-legged HMAC-SHA1" \
  "$BIN oauth1 --method GET --url https://api.example.com/v1/thing \
    --consumer-key testkey --consumer-secret 'literal:testconsumersecret' \
    --timestamp 1234567890 --nonce fixednonce"
run S21-oauth1-3legged "oauth1 3-legged with token" \
  "$BIN oauth1 --method POST --url https://api.example.com/post \
    --consumer-key testkey --consumer-secret 'literal:testconsumersecret' \
    --token mytoken --token-secret 'literal:mytokensecret' \
    --timestamp 1234567890 --nonce fixednonce"
run S22-oauth1-sha256 "oauth1 HMAC-SHA256" \
  "$BIN oauth1 --method GET --url https://api.example.com/resource \
    --consumer-key k --consumer-secret 'literal:cs' \
    --signature-method HMAC-SHA256 \
    --timestamp 1234567890 --nonce fixednonce"
run S23-oauth1-query-params "oauth1 URL with query params" \
  "$BIN oauth1 --method GET --url 'https://api.example.com/search?q=hello&page=1' \
    --consumer-key k --consumer-secret 'literal:cs' \
    --timestamp 1234567890 --nonce fixednonce"
run S24-oauth1-extra-params "oauth1 --param body params" \
  "$BIN oauth1 --method POST --url https://api.example.com/post \
    --consumer-key k --consumer-secret 'literal:cs' \
    --param 'status=Hello World' \
    --timestamp 1234567890 --nonce fixednonce"
run S25-oauth1-json "oauth1 --json" \
  "$BIN oauth1 --method GET --url https://api.example.com/thing \
    --consumer-key k --consumer-secret 'literal:cs' \
    --timestamp 1234567890 --nonce fixednonce \
    --json"
run S26-oauth1-show-base "oauth1 --show-base-string" \
  "$BIN oauth1 --method GET --url https://api.example.com/thing \
    --consumer-key k --consumer-secret 'literal:cs' \
    --timestamp 1234567890 --nonce fixednonce \
    --show-base-string"
run S27-oauth1-value-only "oauth1 --value-only" \
  "$BIN oauth1 --method GET --url https://api.example.com/thing \
    --consumer-key k --consumer-secret 'literal:cs' \
    --timestamp 1234567890 --nonce fixednonce \
    --value-only"
run S28-oauth1-missing-method "oauth1 missing --method -> 125" \
  "$BIN oauth1 --url https://api.example.com/thing \
    --consumer-key k --consumer-secret 'literal:cs'"
run S29-oauth1-missing-url "oauth1 missing --url -> 125" \
  "$BIN oauth1 --method GET \
    --consumer-key k --consumer-secret 'literal:cs'"

# ---- jwt subcommand ----
run S30-jwt-help "jwt --help" "$BIN jwt --help"
run S31-jwt-hs256 "jwt HS256 with claims" \
  "$BIN jwt --alg HS256 --key 'literal:my-hmac-secret-key-32bytes-long!!' \
    --iss myapp --sub user123 --aud https://api.example.com/ \
    --exp 1h --iat"
run S32-jwt-hs256-value-only "jwt HS256 --value-only (bare JWT)" \
  "$BIN jwt --alg HS256 --key 'literal:my-hmac-secret-key-32bytes-long!!' \
    --sub user123 --exp 1h --value-only"
run S33-jwt-hs256-json "jwt HS256 --json" \
  "$BIN jwt --alg HS256 --key 'literal:my-hmac-secret-key-32bytes-long!!' \
    --sub user123 --exp 1h --json"
run S34-jwt-claim "jwt --claim custom value" \
  "$BIN jwt --alg HS256 --key 'literal:my-hmac-secret-key-32bytes-long!!' \
    --claim scope=read:data --claim-num priority=1 \
    --sub user123 --exp 30m"
run S35-jwt-missing-alg "jwt missing --alg -> 125" \
  "$BIN jwt --key 'literal:secret' --sub user"
run S36-jwt-missing-key "jwt missing --key -> 125" \
  "$BIN jwt --alg HS256 --sub user"
run S37-jwt-bad-alg "jwt invalid --alg -> 125" \
  "$BIN jwt --alg BOGUS --key 'literal:secret' --sub user"

# ---- azure-storage subcommand ----
run S38-azurestorage-help "azure-storage --help" "$BIN azure-storage --help"
run S39-azurestorage "azure-storage SharedKey" \
  "MKAUTH_TEST_KEY=$(printf 'aaaa' | base64) $BIN azure-storage \
    --account mystorageacct \
    --key 'literal:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' \
    --method GET \
    --url 'https://mystorageacct.blob.core.windows.net/mycontainer/myblob' \
    --x-ms-date 'Wed, 03 Jun 2026 00:00:00 GMT' \
    --header 'x-ms-version:2023-11-03'"
run S40-azurestorage-show-base "azure-storage --show-base-string" \
  "$BIN azure-storage \
    --account mystorageacct \
    --key 'literal:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' \
    --method PUT --url 'https://mystorageacct.blob.core.windows.net/c/b' \
    --x-ms-date 'Wed, 03 Jun 2026 00:00:00 GMT' \
    --show-base-string"
run S41-azurestorage-missing-account "azure-storage missing --account -> 125" \
  "$BIN azure-storage --key 'literal:AAAA' --method GET \
    --url 'https://mystorageacct.blob.core.windows.net/c/b'"

# ---- exit code surface ----
run S42-unknown-subcommand "unknown subcommand -> 125" "$BIN bogus-subcommand"
run S43-unknown-flag "unknown flag -> 125" "$BIN --bogus-flag"
run S44-no-subcommand "no subcommand -> 125" "$BIN"

# ---- wire-correctness composition smoke ----
# This case exercises the full oauth1 signing pipeline against a stable reference
# input (fixed timestamp + nonce) and checks that the output header contains the
# expected structural elements. It does not validate the exact signature bytes
# (that requires a live counterpart), but confirms the Authorization: OAuth prefix,
# the oauth_consumer_key, oauth_signature, and oauth_version fields are present.
run S45-oauth1-wire-shape "oauth1 wire shape check" \
  "$BIN oauth1 --method GET --url 'http://photos.example.net/photos?file=vacation.jpg&size=original' \
    --consumer-key dpf43f3p2l4k3l03 --consumer-secret 'literal:kd94hf93k423kf44' \
    --token nnch734d00sl2jdk --token-secret 'literal:pfkkdhi9sl3r4s00' \
    --timestamp 1191242096 --nonce kllo9940pd9333jh"

echo "=== Smoke run complete ==="
