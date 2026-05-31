# Gateway-aware LAN IP selection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `hcat --lan` surface (and QR-encode) only LAN-reachable addresses by preferring NICs that have a default gateway, falling back to all addresses when none qualify.

**Architecture:** Split `HCatServer.EnumerateLanIPv4()` into a pure, unit-testable selector (`SelectLanAddresses`) and a thin NIC-walking shell that pairs each non-loopback IPv4 address with a "has IPv4 gateway" flag. `BindResolver`/`RenderQr` are unchanged — filtering at the source fixes both the banner and the QR (which uses `Urls[0]`).

**Tech Stack:** C# / .NET 10, `System.Net.NetworkInformation` (`NetworkInterface.GetIPProperties().GatewayAddresses`), xUnit.

Spec: `docs/plans/2026-05-31-hcat-lan-ip-selection-design.md`. ADR: `docs/plans/2026-05-31-hcat-lan-ip-selection-adr.md`.

---

### Task 1: Two pure helpers (selector + gateway predicate) + tests

Both load-bearing decisions are extracted as pure, deterministically-testable helpers: `SelectLanAddresses` (filter-with-fallback) and `HasUsableIPv4Gateway` (the `0.0.0.0`-placeholder rejection — adversarial-review F2, which would otherwise be reachable only via the non-deterministic native smoke).

**Files:**
- Modify: `src/Winix.HCat/HCatServer.cs` (add `internal static SelectLanAddresses` and `internal static HasUsableIPv4Gateway`, near `EnumerateLanIPv4`)
- Test: `tests/Winix.HCat.Tests/LanAddressSelectorTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests/Winix.HCat.Tests/LanAddressSelectorTests.cs`:

```csharp
using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class LanAddressSelectorTests
{
    [Fact]   // Gateway present → only gateway-routed (LAN-reachable) addresses; virtual host-only IPs dropped.
    public void Returns_only_gateway_addresses_when_any_present()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("172.20.80.1", false),   // Hyper-V vEthernet, no gateway
            ("192.168.1.84", true),   // physical Wi-Fi, has gateway
            ("172.28.80.1", false),
        });
        Assert.Equal(new[] { "192.168.1.84" }, picked);
    }

    [Fact]   // Order preserved among multiple gateway NICs (e.g. Ethernet + Wi-Fi both up).
    public void Preserves_order_among_gateway_addresses()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("10.0.0.5", true),
            ("172.20.80.1", false),
            ("192.168.1.84", true),
        });
        Assert.Equal(new[] { "10.0.0.5", "192.168.1.84" }, picked);
    }

    [Fact]   // No gateway anywhere (isolated/static-IP LAN) → fall back to ALL addresses, nothing lost.
    public void Falls_back_to_all_when_none_have_gateway()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("172.20.80.1", false),
            ("172.28.80.1", false),
        });
        Assert.Equal(new[] { "172.20.80.1", "172.28.80.1" }, picked);
    }

    [Fact]
    public void Empty_in_empty_out()
    {
        Assert.Empty(HCatServer.SelectLanAddresses(new (string, bool)[0]));
    }

    [Fact]   // F2: a real IPv4 gateway counts.
    public void HasUsableIPv4Gateway_true_for_real_ipv4_gateway()
    {
        Assert.True(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Parse("192.168.1.254") }));
    }

    [Fact]   // F2: 0.0.0.0 is a placeholder gateway entry, NOT a real gateway — must not count.
    public void HasUsableIPv4Gateway_false_for_zero_placeholder_only()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Any }));
    }

    [Fact]   // F2: an IPv6 gateway does not make the NIC IPv4-LAN-reachable for our IPv4 URLs.
    public void HasUsableIPv4Gateway_false_for_ipv6_only()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Parse("fe80::1") }));
    }

    [Fact]   // F2: no gateway entries at all.
    public void HasUsableIPv4Gateway_false_for_empty()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(System.Array.Empty<IPAddress>()));
    }

    [Fact]   // F2: a mix of placeholder + real → real one wins.
    public void HasUsableIPv4Gateway_true_when_real_mixed_with_placeholder()
    {
        Assert.True(HCatServer.HasUsableIPv4Gateway(
            new[] { IPAddress.Any, IPAddress.Parse("10.0.0.1") }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~LanAddressSelectorTests"`
Expected: FAIL — compile error, `HCatServer.SelectLanAddresses` / `HasUsableIPv4Gateway` do not exist.

- [ ] **Step 3: Add the two pure helpers**

In `src/Winix.HCat/HCatServer.cs`, add immediately before `EnumerateLanIPv4()`:

```csharp
    /// <summary>Pure LAN-address selection. When any candidate sits on a gateway-routed NIC, returns only
    /// those (LAN-reachable) addresses, preserving input order; otherwise returns all candidate addresses —
    /// the fallback for an isolated/static-IP host with no default gateway, so nothing is ever lost. Empty
    /// input yields empty output. Split out from <see cref="EnumerateLanIPv4"/> so the policy is unit-testable
    /// without touching real network interfaces.</summary>
    internal static IReadOnlyList<string> SelectLanAddresses(
        IReadOnlyList<(string Address, bool HasGateway)> candidates)
    {
        var gatewayed = new List<string>();
        foreach ((string address, bool hasGateway) in candidates)
        {
            if (hasGateway)
            {
                gatewayed.Add(address);
            }
        }
        if (gatewayed.Count > 0)
        {
            return gatewayed;
        }

        var all = new List<string>();
        foreach ((string address, bool _) in candidates)
        {
            all.Add(address);
        }
        return all;
    }

    /// <summary>True when <paramref name="gatewayAddresses"/> contains a usable IPv4 default gateway. A
    /// <c>0.0.0.0</c> entry is a placeholder some adapters report and is NOT a real gateway; IPv6 gateways do
    /// not make the NIC reachable for the IPv4 LAN URLs we render. Pure so the placeholder/family edge — the
    /// part most likely to be wrong — is unit-tested rather than reachable only via the native smoke (F2).</summary>
    internal static bool HasUsableIPv4Gateway(IEnumerable<IPAddress> gatewayAddresses)
    {
        foreach (IPAddress gw in gatewayAddresses)
        {
            if (gw.AddressFamily == AddressFamily.InterNetwork && !gw.Equals(IPAddress.Any))
            {
                return true;
            }
        }
        return false;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~LanAddressSelectorTests"`
Expected: PASS — 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.HCat/HCatServer.cs tests/Winix.HCat.Tests/LanAddressSelectorTests.cs
git commit -m "feat(hcat): pure gateway-aware LAN selector + gateway predicate + tests"
```

---

### Task 2: Rewire `EnumerateLanIPv4` to flag gateway NICs and feed the selector

**Files:**
- Modify: `src/Winix.HCat/HCatServer.cs` (replace the body of `EnumerateLanIPv4`)

This shell reads real NICs, so it is verified by the native `--lan` smoke (Step 3), not a deterministic unit test.

- [ ] **Step 1: Replace the `EnumerateLanIPv4` body**

Current body iterates NICs and adds every non-loopback IPv4. Replace the whole method with:

```csharp
    /// <summary>Enumerates the machine's operational, non-loopback IPv4 addresses for LAN display URLs,
    /// preferring addresses on gateway-routed NICs (see <see cref="SelectLanAddresses"/>). Each address is
    /// paired with whether its NIC has a usable IPv4 default gateway — host-only virtual switches (Hyper-V/
    /// WSL/Docker) have none, so their unreachable addresses are dropped unless no NIC has a gateway.</summary>
    private static IReadOnlyList<string> EnumerateLanIPv4()
    {
        var candidates = new List<(string Address, bool HasGateway)>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            // F1: GetIPProperties() can throw NetworkInformationException for a transient/odd adapter. Skip
            // that one NIC rather than letting it blank out every LAN address (and abort --lan startup).
            IPInterfaceProperties props;
            try
            {
                props = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            // A NIC is LAN-reachable iff it has a real (non-0.0.0.0) IPv4 default gateway; host-only virtual
            // switches have none. The placeholder/family edge lives in HasUsableIPv4Gateway (unit-tested).
            bool hasGateway = HasUsableIPv4Gateway(props.GatewayAddresses.Select(gw => gw.Address));

            foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ua.Address))
                {
                    candidates.Add((ua.Address.ToString(), hasGateway));
                }
            }
        }
        return SelectLanAddresses(candidates);
    }
```

(`IPInterfaceProperties`, `GatewayIPAddressInformation`, and `NetworkInformationException` are in `System.Net.NetworkInformation`; `Select` is `System.Linq` — all already imported in `HCatServer.cs`.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Winix.HCat/Winix.HCat.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Native `--lan` smoke (verifies the NIC shell end-to-end)**

Publish and run the native binary; confirm the banner lists only the gateway IP and the QR encodes it.

Run:
```bash
dotnet publish src/hcat/hcat.csproj -c Release -r win-x64
```
Then run the native exe with `--lan` and a short timeout against any directory:
```
src/hcat/bin/Release/net10.0/win-x64/publish/hcat.exe serve <some-dir> --port 18102 --lan --capture 1 --timeout 4s
```
Expected: the banner's LAN URL list shows the gateway-routed address (e.g. `http://192.168.1.84:18102`) and NOT the `172.x` `vEthernet` virtual IPs; a QR block is printed. Exit 1 (no request within the timeout) is expected.

- [ ] **Step 4: Run the full HCat test suite (no regression)**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj`
Expected: PASS — all tests (90 = 81 + 9 new).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.HCat/HCatServer.cs
git commit -m "feat(hcat): EnumerateLanIPv4 prefers gateway-routed addresses for --lan/QR"
```

---

### Task 3: Docs note that `--lan` prefers gateway-routed addresses

**Files:**
- Modify: `src/hcat/README.md` (the `--lan` row / Sharing section)
- Modify: `src/hcat/man/man1/hcat.1` (the `--lan` entry)
- Modify: `docs/ai/hcat.md` (the `--lan` description)

- [ ] **Step 1: Update README**

In `src/hcat/README.md`, find the `--lan` options-table row:

```
| `--lan` | | Bind `0.0.0.0` to share on the local network (prints a QR code). |
```

Replace with:

```
| `--lan` | | Bind `0.0.0.0` to share on the local network (prints a QR code). LAN URLs/QR prefer gateway-routed (reachable) addresses; virtual host-only adapters (Hyper-V/WSL/Docker) are **skipped from the banner** unless none have a gateway. Use `--host` to pin a specific (incl. host-only) address. |
```

Also add an explicit known-behaviour note (F3). After the Sharing/`--lan` prose section, add a short paragraph:

```
> **Note:** on a machine with virtual adapters (Hyper-V, WSL, Docker, VPNs), `--lan` deliberately lists only addresses on a gateway-routed interface — the ones a device on your real LAN can reach — and hides the rest to keep the banner and QR unambiguous. If you actually want to bind a host-only/virtual address (e.g. to reach a VM), pass it explicitly with `--host <addr>`. If no interface has a default gateway, all addresses are shown.
```

- [ ] **Step 2: Update the man page**

In `src/hcat/man/man1/hcat.1`, find the `--lan` `.TP` entry (the line group containing "prints a QR code with the LAN URL"). After its existing sentence, add a new line:

```
LAN URLs and the QR prefer gateway\-routed addresses; virtual host\-only adapters are skipped unless none have a gateway.
Use \fB\-\-host\fR to pin a specific address.
```

- [ ] **Step 3: Update the AI agent guide**

In `docs/ai/hcat.md`, find the `--lan` description line:

```
- `--lan` binds `0.0.0.0` (all interfaces) and prints a QR code with the LAN URL.
```

Replace with:

```
- `--lan` binds `0.0.0.0` (all interfaces) and prints a QR code with the LAN URL. The LAN URLs and QR prefer gateway-routed (reachable) addresses, skipping virtual host-only adapters (Hyper-V/WSL/Docker) unless none have a gateway; pin a specific address with `--host`.
```

- [ ] **Step 4: Verify groff is well-formed**

Run: `man --warnings -l src/hcat/man/man1/hcat.1 >/dev/null`
Expected: no groff warnings on stderr (or `man` renders cleanly).

- [ ] **Step 5: Commit**

```bash
git add src/hcat/README.md src/hcat/man/man1/hcat.1 docs/ai/hcat.md
git commit -m "docs(hcat): note --lan prefers gateway-routed addresses"
```

---

## Self-Review

**Spec coverage:** D1 (gateway signal) → Task 2 NIC flagging via `HasUsableIPv4Gateway`; D2 (filter-with-fallback) → Task 1 `SelectLanAddresses`; testability → Task 1 unit tests (both helpers) + Task 2 native smoke; docs note → Task 3. All spec sections covered.

**Placeholder scan:** none — every step has concrete code/commands.

**Type consistency:** `SelectLanAddresses(IReadOnlyList<(string Address, bool HasGateway)>)` and `HasUsableIPv4Gateway(IEnumerable<IPAddress>)` defined in Task 1 Step 3, called in Task 2 Step 1 with matching shapes; Task 1 tests pass `(string, bool)[]` and `IPAddress[]` respectively (assignable). Consistent.

**Adversarial-review integration (2026-05-31, 0 blockers / 2 test gaps / 1 defer):** F1 → Task 2 wraps per-NIC `GetIPProperties()` in `try/catch (NetworkInformationException) { continue; }` (PREDICTION-class: no deterministic test for a throwing NIC — hardening + documented policy here and in the design's edge cases). F2 → `HasUsableIPv4Gateway` extracted as a second pure helper with 5 unit tests (real/placeholder/IPv6/empty/mixed). F3 → Task 3 documents that non-gateway addresses are intentionally hidden and `--host` pins a specific one.
