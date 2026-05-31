# Gateway-aware LAN IP selection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `hcat --lan` surface (and QR-encode) only LAN-reachable addresses by preferring NICs that have a default gateway, falling back to all addresses when none qualify.

**Architecture:** Split `HCatServer.EnumerateLanIPv4()` into a pure, unit-testable selector (`SelectLanAddresses`) and a thin NIC-walking shell that pairs each non-loopback IPv4 address with a "has IPv4 gateway" flag. `BindResolver`/`RenderQr` are unchanged — filtering at the source fixes both the banner and the QR (which uses `Urls[0]`).

**Tech Stack:** C# / .NET 10, `System.Net.NetworkInformation` (`NetworkInterface.GetIPProperties().GatewayAddresses`), xUnit.

Spec: `docs/plans/2026-05-31-hcat-lan-ip-selection-design.md`. ADR: `docs/plans/2026-05-31-hcat-lan-ip-selection-adr.md`.

---

### Task 1: Pure LAN-address selector + tests

**Files:**
- Modify: `src/Winix.HCat/HCatServer.cs` (add `internal static SelectLanAddresses`, near `EnumerateLanIPv4`)
- Test: `tests/Winix.HCat.Tests/LanAddressSelectorTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests/Winix.HCat.Tests/LanAddressSelectorTests.cs`:

```csharp
using System.Collections.Generic;
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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~LanAddressSelectorTests"`
Expected: FAIL — compile error, `HCatServer.SelectLanAddresses` does not exist.

- [ ] **Step 3: Add the pure selector**

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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~LanAddressSelectorTests"`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.HCat/HCatServer.cs tests/Winix.HCat.Tests/LanAddressSelectorTests.cs
git commit -m "feat(hcat): pure gateway-aware LAN-address selector + tests"
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

            IPInterfaceProperties props = nic.GetIPProperties();

            // A NIC is LAN-reachable iff it has a real (non-0.0.0.0) IPv4 default gateway. 0.0.0.0 appears as
            // a placeholder gateway entry on some adapters and must not count.
            bool hasGateway = false;
            foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
            {
                if (gw.Address.AddressFamily == AddressFamily.InterNetwork
                    && !gw.Address.Equals(IPAddress.Any))
                {
                    hasGateway = true;
                    break;
                }
            }

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

(`IPInterfaceProperties` and `GatewayIPAddressInformation` are in `System.Net.NetworkInformation`, already imported.)

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
Expected: PASS — all tests (85 = 81 + 4 new).

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
| `--lan` | | Bind `0.0.0.0` to share on the local network (prints a QR code). LAN URLs/QR prefer gateway-routed (reachable) addresses; virtual host-only adapters (Hyper-V/WSL/Docker) are skipped unless none have a gateway. Use `--host` to pin a specific address. |
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

**Spec coverage:** D1 (gateway signal) → Task 2 NIC flagging; D2 (filter-with-fallback) → Task 1 selector; testability → Task 1 unit tests + Task 2 native smoke; docs note → Task 3. All spec sections covered.

**Placeholder scan:** none — every step has concrete code/commands.

**Type consistency:** `SelectLanAddresses(IReadOnlyList<(string Address, bool HasGateway)>)` defined in Task 1 Step 3, called in Task 2 Step 1 with the same shape; tests in Task 1 pass `(string, bool)[]` (assignable to `IReadOnlyList<(string, bool)>`). Consistent.
