#!/usr/bin/env bash
# sign-release.sh — Download win-x64 binaries from a draft GitHub release,
# sign them with Authenticode, re-upload, and publish the release.
#
# Prerequisites:
#   - SimplySign Desktop running and logged in
#   - signtool.exe on PATH (Developer Command Prompt) or SIGNTOOL env var set
#   - gh CLI authenticated
#
# Usage:
#   ./scripts/sign-release.sh v0.2.0
#   SIGNTOOL="/c/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/signtool.exe" ./scripts/sign-release.sh v0.2.0

set -euo pipefail

REPO="Yortw/winix"
THUMBPRINT="C745DD3F792DDB3DDD7C3BE6BC7AEF9CC855200F"
TIMESTAMP_URL="http://time.certum.pl"

# --- Resolve signtool path ---
if [ -n "${SIGNTOOL:-}" ]; then
  SIGNTOOL_EXE="$SIGNTOOL"
elif command -v signtool.exe &>/dev/null; then
  SIGNTOOL_EXE="signtool.exe"
elif command -v signtool &>/dev/null; then
  SIGNTOOL_EXE="signtool"
else
  echo "ERROR: signtool not found. Either:" >&2
  echo "  - Run from Developer Command Prompt, or" >&2
  echo "  - Set SIGNTOOL to the full path of signtool.exe" >&2
  exit 1
fi

# --- Validate arguments ---
if [ $# -ne 1 ]; then
  echo "Usage: $0 <tag>  (e.g. v0.2.0)" >&2
  exit 1
fi

TAG="$1"

# --- Verify the release exists and is a draft ---
RELEASE_STATUS=$(gh release view "$TAG" --repo "$REPO" --json isDraft --jq '.isDraft')
if [ "$RELEASE_STATUS" != "true" ]; then
  echo "ERROR: Release $TAG is not a draft (or does not exist)." >&2
  echo "  If the release is already published, re-uploading signed binaries" >&2
  echo "  won't update Scoop/winget hashes. You'd need to re-run those jobs." >&2
  exit 1
fi

echo "Found draft release: $TAG"

# --- Create temp working directory ---
WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT
echo "Working in: $WORK_DIR"

# --- Download all win-x64 zips ---
echo ""
echo "Downloading win-x64 zips..."
gh release download "$TAG" --repo "$REPO" --pattern "*-win-x64.zip" --dir "$WORK_DIR"

# List what we got
ZIPS=("$WORK_DIR"/*-win-x64.zip)
echo "Downloaded ${#ZIPS[@]} zip files:"
for z in "${ZIPS[@]}"; do
  echo "  $(basename "$z")"
done

# --- Unzip, sign, re-zip each file ---
echo ""
echo "Signing binaries..."

for ZIP_PATH in "${ZIPS[@]}"; do
  ZIP_NAME=$(basename "$ZIP_PATH")
  TOOL_NAME="${ZIP_NAME%-win-x64.zip}"
  EXTRACT_DIR="$WORK_DIR/$TOOL_NAME"

  mkdir -p "$EXTRACT_DIR"
  unzip -q -o "$ZIP_PATH" -d "$EXTRACT_DIR"

  # Find all .exe files (including in subdirectories for combined winix zip)
  mapfile -t EXE_FILES < <(find "$EXTRACT_DIR" -name '*.exe' -type f)
  if [ ${#EXE_FILES[@]} -eq 0 ]; then
    echo "  SKIP $ZIP_NAME (no .exe files)"
    continue
  fi

  echo "  Signing $ZIP_NAME (${#EXE_FILES[@]} exe)..."
  "$SIGNTOOL_EXE" sign \
    /sha1 "$THUMBPRINT" \
    /fd sha256 \
    /tr "$TIMESTAMP_URL" \
    /td sha256 \
    /q \
    "${EXE_FILES[@]}"

  # Verify signatures
  for exe in "${EXE_FILES[@]}"; do
    "$SIGNTOOL_EXE" verify /pa /q "$exe"
  done

  # Re-zip preserving directory structure (important for combined winix zip
  # which contains a man/ subdirectory)
  rm "$ZIP_PATH"
  (cd "$EXTRACT_DIR" && zip -q -r "$ZIP_PATH" .)
done

echo ""
echo "All binaries signed and verified."

# --- Upload signed win-x64 zips back to the release ---
echo ""
echo "Uploading signed zips to release $TAG..."

for ZIP_PATH in "${ZIPS[@]}"; do
  ZIP_NAME=$(basename "$ZIP_PATH")
  echo "  Uploading $ZIP_NAME..."
  gh release upload "$TAG" "$ZIP_PATH" --repo "$REPO" --clobber
done

# --- Generate checksums from final release assets (all platforms) ---
# Download AFTER uploading signed zips so win-x64 hashes are correct.
echo ""
echo "Generating checksums for all release zips..."
CHECKSUM_DIR="$WORK_DIR/checksums"
mkdir -p "$CHECKSUM_DIR"
gh release download "$TAG" --repo "$REPO" --pattern "*.zip" --dir "$CHECKSUM_DIR"

(cd "$CHECKSUM_DIR" && sha256sum *.zip | sort -k2) > "$WORK_DIR/SHA256SUMS"
cat "$WORK_DIR/SHA256SUMS"

echo "  Uploading SHA256SUMS..."
gh release upload "$TAG" "$WORK_DIR/SHA256SUMS" --repo "$REPO" --clobber

echo ""
echo "All signed zips, checksums uploaded."

# --- Prompt to publish ---
echo ""
echo "Release $TAG is still a draft."
echo "To publish: gh release edit $TAG --repo $REPO --draft=false"
echo ""
read -rp "Publish now? [y/N] " CONFIRM
if [[ "$CONFIRM" =~ ^[Yy]$ ]]; then
  gh release edit "$TAG" --repo "$REPO" --draft=false
  echo "Release $TAG published!"
else
  echo "Left as draft. Publish when ready with:"
  echo "  gh release edit $TAG --repo $REPO --draft=false"
fi
