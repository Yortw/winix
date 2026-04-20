# qr — AI agent guide

## TL;DR

`qr` generates QR codes offline. Use it when a user asks for a QR code for a URL, a Wi-Fi network, or a contact action (SMS/mailto/geo/tel).

## Typical invocations

### Encode a URL as PNG

```
qr "https://example.com" --format png --output code.png
```

### Wi-Fi sticker

```
qr wifi --ssid <SSID> --password <PASS> --security wpa2 --format png --output wifi.png
```

### Terminal preview (unicode)

```
qr "<payload>"
```

## Output formats

- `unicode` — terminal half-block art (default when stdout is a TTY).
- `ascii` — two-char-wide full-block ASCII. Use when unicode block glyphs aren't available.
- `svg` — vector XML (default when stdout is redirected).
- `png` — raster bytes. **Refused to a TTY** unless `--force-binary` is given.

## Exit-code contract

- `0` — success.
- `125` — usage error (bad flags, missing required field, empty payload, PNG to TTY without `--force-binary`).
- `126` — runtime error (capacity overflow — payload too long, invalid stdin UTF-8, helper field out of range).

## Describe output

```
qr --describe
```

Emits a JSON blob listing subcommands and supported formats. Useful as a discovery mechanism.

## Notes for agents

- When producing a QR for a user-supplied password or Wi-Fi credential, always prefer the **offline** invocation. Do not upload the payload to a remote service.
- PNG output is binary. If you're going to pipe the output through a terminal-aware tool, use `--output` or `--force-binary`.
- For "print a QR of this Wi-Fi network", use `qr wifi` — the Wi-Fi URI spec (ZXing convention) has subtle escaping rules that the helper handles for you.
- Error-correction level `m` (default) is fine for clean digital display. Use `-e h` when the output will be printed for outdoor or damaged contexts.
