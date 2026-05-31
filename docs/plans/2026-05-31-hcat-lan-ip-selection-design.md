# hcat — gateway-aware LAN IP selection

**Date:** 2026-05-31
**Status:** Approved (brainstorm)
**Tool:** `hcat` (`Winix.HCat`)
**Companion ADR:** [`2026-05-31-hcat-lan-ip-selection-adr.md`](2026-05-31-hcat-lan-ip-selection-adr.md)

## Problem

`hcat ... --lan` lists every operational, non-loopback IPv4 address and renders a QR of the **first** one (`BindInfo.Urls[0]`). On a multi-NIC machine (Hyper-V / WSL / Docker / VirtualBox host-only switches, VPNs) that first address is often a **virtual-adapter IP a phone on the real LAN cannot reach**. Observed during the post-review native smoke: the QR encoded `172.20.80.1` (a Hyper-V `vEthernet` host-only adapter) instead of the physical Wi-Fi IP `192.168.1.84`, defeating the "scan to open on your phone" purpose. The banner also lists six unreachable `172.x` virtual IPs as clutter.

## Root signal

A NIC with an **IPv4 default gateway** is on a real routed network; host-only virtual switches are unrouted by design and have **no gateway**. Confirmed on the dev machine via `ipconfig`: only the physical Wi-Fi adapter (`192.168.1.84`) had a gateway (`192.168.1.254`); all six `vEthernet` adapters had none. Gateway presence is exposed cross-platform by `NetworkInterface.GetIPProperties().GatewayAddresses` (Windows/Linux/macOS) and is locale/OS-independent — unlike NIC-name pattern matching (`vEthernet`/`Hyper-V`/…) or IP-range heuristics (prefer `192.168` over `172`, which misfires on real `172.16/12` LANs).

## Design

Localised to `HCatServer.EnumerateLanIPv4()`. Two pieces:

1. **Pure selector (new, unit-testable):** `SelectLanAddresses(IEnumerable<(string Address, bool HasGateway)> candidates) -> IReadOnlyList<string>`.
   - If any candidate `HasGateway`, return only the gateway-having addresses (preserving input order).
   - Otherwise return all candidate addresses (the current behaviour — fallback so nothing is lost on an isolated/static-IP LAN).
   - Empty input → empty output.
2. **Thin NIC-walking shell:** `EnumerateLanIPv4()` walks `NetworkInterface.GetAllNetworkInterfaces()`, and for each `OperationalStatus.Up` NIC pairs every `AddressFamily.InterNetwork`, non-loopback unicast address with a `HasGateway` flag derived from `GetIPProperties().GatewayAddresses` (any `InterNetwork` gateway that is not `0.0.0.0`). It passes the pairs to `SelectLanAddresses` and returns the result.

No change to `BindResolver` or `RenderQr`: filtering at the source means the banner lists only reachable IPs **and** the QR (which uses `Urls[0]`) encodes the first reachable one. `--host` remains the explicit override for niche host-only / VM sharing.

## Edge cases

- **Multiple gateway NICs** (Ethernet + Wi-Fi both up): all listed in NIC order; QR = first. No finer tiebreak (YAGNI).
- **VPN adapter with a gateway:** included — acceptable; `--host` overrides if wrong.
- **No gateway anywhere:** fallback returns all addresses (unchanged from today).
- **No NICs / no IPv4 at all:** selector returns empty; `BindResolver` already falls back to a `0.0.0.0` display URL.
- **A NIC throws on `GetIPProperties()`** (adversarial-review F1): `NetworkInformationException` from a single transient/odd adapter is caught and that NIC is skipped, so one bad adapter cannot blank out every LAN address or abort `--lan`. Not a deterministic test (can't force a throwing NIC); hardening + this documented policy.
- **Hidden non-gateway addresses** (adversarial-review F3): when the filter drops virtual/host-only addresses, the banner shows fewer IPs than the machine has and gives no per-address "hidden" breadcrumb — intended (clean banner), documented in README, with `--host` as the override. A `--all-interfaces` flag / stderr "N hidden" note is deferred (see ADR).

## Testing

- **Unit (the new value):** `SelectLanAddresses` — gateway present filters to gateway-having; mixed input keeps order; none-present falls back to all; empty → empty.
- **Integration/native:** the existing `--lan` native smoke already exercises the NIC-walking shell end-to-end; re-run it to confirm the QR now encodes the gateway IP on this multi-NIC box.

## Scope

**In:** gateway-aware selection + fallback; the pure selector + its tests; a one-line docs note in README/man/ai-guide that `--lan` prefers gateway-routed (LAN-reachable) addresses.

**Out (YAGNI):** NIC-type/name heuristics, IP-range preference, finer multi-gateway tiebreaking, an "all interfaces incl. virtual" opt-in flag (deferred — `--host` covers the override need).
