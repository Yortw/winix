# hcat — gateway-aware LAN IP selection — ADR

**Date:** 2026-05-31
**Status:** Accepted
**Context:** `hcat --lan` rendered a QR of the first enumerated non-loopback IPv4, which on a multi-NIC machine is frequently an unreachable virtual-adapter IP (Hyper-V/WSL host-only switch). Related design doc: [`2026-05-31-hcat-lan-ip-selection-design.md`](2026-05-31-hcat-lan-ip-selection-design.md).

---

## D1 — Use default-gateway presence to identify LAN-reachable addresses

**Context.** The LAN URLs / QR must favour an address a device on the real LAN can reach. Virtual host-only adapters (Hyper-V `vEthernet`, WSL, Docker, VirtualBox host-only) expose IPv4 addresses that no external device can reach.

**Decision.** Treat a NIC as LAN-reachable iff it has a non-`0.0.0.0` IPv4 default gateway (`NetworkInterface.GetIPProperties().GatewayAddresses`). Prefer those addresses for the banner and QR.

**Rationale.** Gateway presence is the *causal* signal, not a correlate: host-only switches are unrouted by design and therefore have no gateway, while the NIC wired to the router does. It is cross-platform (Windows/Linux/macOS) and locale/OS-independent. Verified on the dev machine: the physical Wi-Fi adapter was the only one of seven with a gateway.

**Trade-offs accepted.** A VPN adapter with a gateway is treated as reachable (may not be intended); a real LAN with no configured gateway has no "reachable" signal (handled by the fallback, D2). `--host` is the explicit override for both.

**Options considered.** *NIC name/description blocklist* (`vEthernet`/`Hyper-V`/`VMware`/`Docker`/`WSL`) — rejected: fragile, varies by OS/locale/driver version. *IP-range preference* (favour `192.168`/`10` over `172`) — rejected: misfires on legitimate `172.16/12` corporate LANs. *Outbound-socket trick* (connect a UDP socket to a public IP, read the local endpoint) — rejected: picks only the single default-route IP, drops other valid LAN IPs, and probes the network at startup.

## D2 — Filter to gateway addresses, with fallback to all when none qualify

**Context.** How aggressively to act on the signal: hide non-gateway addresses, or merely reorder them.

**Decision.** List only gateway-having addresses; if **no** NIC has an IPv4 gateway, fall back to listing all operational non-loopback IPv4 addresses (today's behaviour).

**Rationale.** The headline use case ("scan from my phone on the LAN") wants one obvious reachable URL, not six virtual-IP distractors. Filtering gives the clean banner; the fallback guarantees an isolated/static-IP host still shows its addresses, so nothing is ever silently lost. The niche "share to a VM on a host-only network" case is served by the explicit `--host`.

**Trade-offs accepted.** When some NIC has a gateway, a genuinely-wanted no-gateway (host-only) address is hidden from the banner — recovered via `--host`.

**Options considered.** *Reorder but keep all* — rejected: still clutters the banner with unreachable virtual IPs on every multi-NIC dev machine. *Filter with no fallback* — rejected: an isolated LAN with no gateway would show no LAN address at all.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `--all-interfaces` opt-in to show virtual/host-only IPs | `--host` already covers the override; add only if asked. |
| Finer multi-gateway tiebreak (prefer Wi-Fi vs Ethernet) | Both are reachable; first-in-NIC-order is fine. |
| NIC-type / name heuristics as a secondary signal | Gateway signal is sufficient and robust; extra heuristics add fragility. |
