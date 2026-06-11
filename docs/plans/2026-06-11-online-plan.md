# online — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `online`, a blocking network-readiness gate: exit `0` the moment a layered, captive-portal-aware internet check (and/or named-URL health checks) all pass, `124` on timeout, `1` on a single `--once` miss, `125` on usage error.

**Architecture:** Class library `Winix.Online` (pure logic + injectable network/clock seams) + thin console app `src/online` (parse, console setup, Ctrl+C, real HttpClient). Checks implement `IReadinessCheck`; `WaitEngine` polls them with an injected clock+sleep seam (precedent `RetryRunner`). `Cli.RunAsync` is the library seam; `Program.Main` owns process-global state only.

**Tech Stack:** .NET 10, AOT, `Yort.ShellKit.CommandLineParser` (mandatory), `HttpClient` over `SocketsHttpHandler`, `Dns.GetHostAddressesAsync`, `NetworkInterface.GetIsNetworkAvailable`, xUnit + Xunit.SkippableFact.

**Source documents:**
- Design: `docs/plans/2026-06-11-online-design.md`
- ADR: `docs/plans/2026-06-11-online-adr.md`

**Reference files to mirror (read before starting):**
- `src/Winix.Retry/Cli.cs`, `src/Winix.Retry/RetryRunner.cs` — Cli seam + injected clock/delay pattern
- `src/retry/Program.cs` — thin Main + Ctrl+C handler
- `src/notify/Program.cs` — shared `HttpClient` lifetime
- `src/Winix.WhoHolds/Formatting.cs` — own-data JSON/summary formatting
- `src/Yort.ShellKit/JsonHelper.cs` — `Utf8JsonWriter` envelope construction
- `tests/Winix.Retry.Tests/Winix.Retry.Tests.csproj` — test csproj shape (`UseSystemResourceKeys=true`)

**Conventions (non-negotiable, from `CLAUDE.md`):** full braces always; `#nullable enable`; warnings-as-errors; query-syntax LINQ where natural; XML doc comments on public/internal members; `int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)`; never pipe a framework `ex.Message` to user output (use `SafeError.Describe`); own-data tool ⇒ `--json` to **stdout**, summary to **stderr**.

---

## Pre-flight (do once before Task 1)

- [ ] **Confirm branch.** Run: `git branch --show-current` → must print `feature/online`. If not: `git switch feature/online`. (Branch already exists off `release/v0.4.0`; design+ADR are committed at `548dc57`.)

---

## Task 1: Project scaffolding (csproj trio + solution wiring)

**Files:**
- Create: `src/Winix.Online/Winix.Online.csproj`
- Create: `src/online/online.csproj`
- Create: `tests/Winix.Online.Tests/Winix.Online.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the class-library csproj** `src/Winix.Online/Winix.Online.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Online.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the console-app csproj** `src/online/online.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>online</ToolCommandName>
    <PackageId>Winix.Online</PackageId>
    <Description>Block until the internet — or a named endpoint — is actually healthy (captive-portal-aware network-readiness gate).</Description>
    <PackageTags>cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix;network;connectivity;readiness;wait;captive-portal</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Online\Winix.Online.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="man\man1\online.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\online.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the test csproj** `tests/Winix.Online.Tests/Winix.Online.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <!-- Mirror the shipped tool's UseSystemResourceKeys=true so tests observe the same bare
         CoreLib resource-key behaviour on framework exception .Message that AOT publish produces. -->
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.Online\Winix.Online.csproj" />
    <!--
      ProjectReference to the console app (build-but-not-link) so a clean-tree
      `dotnet test tests/Winix.Online.Tests` still produces online.dll for any spawn-based test.
      Matches retry's pattern.
    -->
    <ProjectReference Include="..\..\src\online\online.csproj" ReferenceOutputAssembly="false" OutputItemType="None" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add `InternalsVisibleTo` so tests can reach the internal seam overload.** Create `src/Winix.Online/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Winix.Online.Tests")]
```

- [ ] **Step 5: Add all three projects to the solution**

Run:
```
dotnet sln Winix.sln add src/Winix.Online/Winix.Online.csproj src/online/online.csproj tests/Winix.Online.Tests/Winix.Online.Tests.csproj
```
Expected: "Project ... added to the solution." ×3.

- [ ] **Step 6: Verify the solution still restores** (no source files yet, so build will be a no-op for these projects)

Run: `dotnet restore Winix.sln`
Expected: restore succeeds, no errors.

- [ ] **Step 7: Commit**

```
git add src/Winix.Online src/online tests/Winix.Online.Tests Winix.sln
git commit -m "chore(online): scaffold class library, console app, and test projects"
```

---

## Task 2: Core types — `HttpProbeResult`, seam delegates, `CheckResult`, `IReadinessCheck`

**Files:**
- Create: `src/Winix.Online/HttpProbeResult.cs`
- Create: `src/Winix.Online/Seams.cs`
- Create: `src/Winix.Online/CheckResult.cs`
- Create: `src/Winix.Online/IReadinessCheck.cs`

These are type declarations (no behaviour to test directly); they are exercised by every later task's tests.

- [ ] **Step 1: `HttpProbeResult.cs`**

```csharp
#nullable enable

namespace Winix.Online;

/// <summary>
/// Outcome of a single HTTP probe. <see cref="Connected"/> is <see langword="false"/> when no HTTP
/// reply was obtained at all — connect/TLS failure, a DNS failure surfaced at request time, or a
/// per-probe timeout. When <see langword="true"/>, <see cref="StatusCode"/> and
/// <see cref="BodyEmpty"/> describe the response.
/// </summary>
/// <param name="Connected">Whether an HTTP response was received.</param>
/// <param name="StatusCode">HTTP status code (0 when not connected).</param>
/// <param name="BodyEmpty">Whether the response body was empty (used by the 204 internet rung;
/// a captive portal returns a non-empty HTML body).</param>
public sealed record HttpProbeResult(bool Connected, int StatusCode, bool BodyEmpty)
{
    /// <summary>Shared "no HTTP reply" result for connect failures and per-probe timeouts.</summary>
    public static readonly HttpProbeResult Unreachable = new(false, 0, false);
}
```

- [ ] **Step 2: `Seams.cs`** (delegate types for the injectable network probes)

```csharp
#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>Performs a single HTTP GET against <paramref name="url"/>. Must translate connect/TLS
/// failures and per-probe timeouts into <see cref="HttpProbeResult.Unreachable"/> and only rethrow
/// <see cref="System.OperationCanceledException"/> when the OUTER token (user cancel) fired.</summary>
public delegate Task<HttpProbeResult> HttpProbe(string url, CancellationToken cancellationToken);

/// <summary>Resolves <paramref name="host"/> to one or more addresses. Returns <see langword="false"/>
/// on resolution failure; rethrows only on outer-token cancellation.</summary>
public delegate Task<bool> DnsProbe(string host, CancellationToken cancellationToken);
```

- [ ] **Step 3: `CheckResult.cs`**

```csharp
#nullable enable

namespace Winix.Online;

/// <summary>
/// Result of one readiness check in one poll cycle.
/// </summary>
/// <param name="Kind">Check kind: <c>"internet"</c> or <c>"url"</c>.</param>
/// <param name="Target">The target URL for a url check; <see langword="null"/> for the internet check.</param>
/// <param name="Ok">Whether the check passed this cycle.</param>
/// <param name="Detail">Human-readable detail, e.g. <c>"204 via https://..."</c>, <c>"503"</c>,
/// <c>"no network route"</c>, <c>"connect failed"</c>.</param>
public sealed record CheckResult(string Kind, string? Target, bool Ok, string Detail);
```

- [ ] **Step 4: `IReadinessCheck.cs`**

```csharp
#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>A single network-readiness check evaluated once per poll cycle.</summary>
public interface IReadinessCheck
{
    /// <summary>Evaluates the check once. Honours <paramref name="cancellationToken"/> for user cancel.</summary>
    Task<CheckResult> RunAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Build to verify the types compile**

Run: `dotnet build src/Winix.Online/Winix.Online.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```
git add src/Winix.Online
git commit -m "feat(online): core check types and probe seam delegates"
```

---

## Task 3: `StatusSpec` — parse and match `--status` (default `2xx`)

**Files:**
- Create: `src/Winix.Online/StatusSpec.cs`
- Test: `tests/Winix.Online.Tests/StatusSpecTests.cs`

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/StatusSpecTests.cs`

```csharp
#nullable enable

using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class StatusSpecTests
{
    [Fact]
    public void Default_matches_2xx_only()
    {
        StatusSpec spec = StatusSpec.Default;
        Assert.True(spec.Matches(200));
        Assert.True(spec.Matches(204));
        Assert.True(spec.Matches(299));
        Assert.False(spec.Matches(199));
        Assert.False(spec.Matches(300));
        Assert.False(spec.Matches(503));
    }

    [Theory]
    [InlineData("2xx", 204, true)]
    [InlineData("2xx", 404, false)]
    [InlineData("200,204", 204, true)]
    [InlineData("200,204", 201, false)]
    [InlineData("200-299", 250, true)]
    [InlineData("200-299", 300, false)]
    [InlineData("200,500-599", 503, true)]   // mixed list + range
    [InlineData("5xx", 503, true)]
    [InlineData("5xx", 200, false)]
    public void Parse_then_match(string spec, int code, bool expected)
    {
        Assert.True(StatusSpec.TryParse(spec, out StatusSpec parsed, out _));
        Assert.Equal(expected, parsed.Matches(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2x")]
    [InlineData("700")]        // out of 100-599 range
    [InlineData("250-200")]    // reversed range
    [InlineData("200-")]       // incomplete range
    public void Invalid_specs_report_error(string spec)
    {
        Assert.False(StatusSpec.TryParse(spec, out _, out string? error));
        Assert.NotNull(error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail** (StatusSpec does not exist yet)

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter StatusSpecTests`
Expected: FAIL — does not compile (`StatusSpec` not found).

- [ ] **Step 3: Implement `StatusSpec.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Winix.Online;

/// <summary>
/// An HTTP status-code matcher parsed from a <c>--status</c> spec. Supports a class shorthand
/// (<c>2xx</c>), explicit codes (<c>200,204</c>), inclusive ranges (<c>200-299</c>), and any
/// comma-separated mix of those. Default is <c>2xx</c>.
/// </summary>
public sealed class StatusSpec
{
    private readonly IReadOnlyList<(int Lo, int Hi)> _ranges;

    private StatusSpec(IReadOnlyList<(int, int)> ranges)
    {
        _ranges = ranges;
    }

    /// <summary>The default spec: any 2xx status (200–299).</summary>
    public static StatusSpec Default { get; } = new(new (int, int)[] { (200, 299) });

    /// <summary>Returns <see langword="true"/> when <paramref name="code"/> falls in any parsed range.</summary>
    public bool Matches(int code)
    {
        foreach ((int lo, int hi) in _ranges)
        {
            if (code >= lo && code <= hi)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parses a status spec. On failure, <paramref name="error"/> describes the offending token and
    /// the method returns <see langword="false"/> (caller maps to a usage error).
    /// </summary>
    public static bool TryParse(string spec, out StatusSpec result, out string? error)
    {
        result = Default;
        error = null;
        var ranges = new List<(int, int)>();

        foreach (string token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Class shorthand: a single digit 1-5 followed by "xx" (case-insensitive).
            if (token.Length == 3 && token[0] >= '1' && token[0] <= '5'
                && (token[1] == 'x' || token[1] == 'X') && (token[2] == 'x' || token[2] == 'X'))
            {
                int hundreds = (token[0] - '0') * 100;
                ranges.Add((hundreds, hundreds + 99));
                continue;
            }

            int dash = token.IndexOf('-');
            if (dash > 0 && dash < token.Length - 1)
            {
                string loText = token.Substring(0, dash);
                string hiText = token.Substring(dash + 1);
                if (TryCode(loText, out int lo) && TryCode(hiText, out int hi) && lo <= hi)
                {
                    ranges.Add((lo, hi));
                    continue;
                }
                error = $"invalid status range '{token}'";
                return false;
            }

            if (TryCode(token, out int code))
            {
                ranges.Add((code, code));
                continue;
            }

            error = $"invalid status '{token}'";
            return false;
        }

        if (ranges.Count == 0)
        {
            error = "empty status spec";
            return false;
        }

        result = new StatusSpec(ranges);
        return true;
    }

    private static bool TryCode(string text, out int code)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out code)
            && code >= 100 && code <= 599)
        {
            return true;
        }
        code = 0;
        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter StatusSpecTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```
git add src/Winix.Online/StatusSpec.cs tests/Winix.Online.Tests/StatusSpecTests.cs
git commit -m "feat(online): StatusSpec parser/matcher for --status"
```

---

## Task 4: `UrlCheck` — GET + status match

**Files:**
- Create: `src/Winix.Online/UrlCheck.cs`
- Test: `tests/Winix.Online.Tests/UrlCheckTests.cs`

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/UrlCheckTests.cs`

```csharp
#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class UrlCheckTests
{
    private static HttpProbe Probe(HttpProbeResult result)
        => (_, _) => Task.FromResult(result);

    [Fact]
    public async Task Status_2xx_is_ready()
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(new HttpProbeResult(true, 200, false)));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.True(r.Ok);
        Assert.Equal("url", r.Kind);
        Assert.Equal("https://api/health", r.Target);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(301)]
    public async Task Non_matching_status_is_not_ready(int status)
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(new HttpProbeResult(true, status, false)));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Connection_failure_is_not_ready()
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(HttpProbeResult.Unreachable));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Custom_status_matches_exact_set()
    {
        Assert.True(StatusSpec.TryParse("200,204", out StatusSpec spec, out _));
        var ready = new UrlCheck("https://x", spec, Probe(new HttpProbeResult(true, 204, true)));
        var notReady = new UrlCheck("https://x", spec, Probe(new HttpProbeResult(true, 201, false)));
        Assert.True((await ready.RunAsync(CancellationToken.None)).Ok);
        Assert.False((await notReady.RunAsync(CancellationToken.None)).Ok);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter UrlCheckTests`
Expected: FAIL — `UrlCheck` not found.

- [ ] **Step 3: Implement `UrlCheck.cs`**

```csharp
#nullable enable

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// Waits for a named URL to return a status matching a <see cref="StatusSpec"/> (default 2xx).
/// Connection failure, per-probe timeout, 5xx, 429, and any non-matching status all report
/// "not ready" so the caller keeps waiting (a transient server error must not end the wait).
/// </summary>
public sealed class UrlCheck : IReadinessCheck
{
    private readonly string _target;
    private readonly StatusSpec _status;
    private readonly HttpProbe _probe;

    /// <summary>Creates a check for <paramref name="target"/> against <paramref name="status"/>.</summary>
    public UrlCheck(string target, StatusSpec status, HttpProbe probe)
    {
        _target = target;
        _status = status;
        _probe = probe;
    }

    /// <inheritdoc/>
    public async Task<CheckResult> RunAsync(CancellationToken cancellationToken)
    {
        HttpProbeResult probe = await _probe(_target, cancellationToken);
        if (!probe.Connected)
        {
            return new CheckResult("url", _target, false, "connect failed");
        }

        string detail = probe.StatusCode.ToString(CultureInfo.InvariantCulture);
        return new CheckResult("url", _target, _status.Matches(probe.StatusCode), detail);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter UrlCheckTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add src/Winix.Online/UrlCheck.cs tests/Winix.Online.Tests/UrlCheckTests.cs
git commit -m "feat(online): UrlCheck — GET + status match, transient errors keep waiting"
```

---

## Task 5: `InternetCheck` — layered route → DNS → 204, randomised short-circuit

**Files:**
- Create: `src/Winix.Online/InternetCheck.cs`
- Test: `tests/Winix.Online.Tests/InternetCheckTests.cs`

The injected ordering seam is `Func<IReadOnlyList<string>, IReadOnlyList<string>>`; tests pass identity so order is deterministic. Counting fakes pin the invariants: no DNS/HTTP when the route is down; no HTTP when DNS fails; no further endpoints probed after the first 204.

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/InternetCheckTests.cs`

```csharp
#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class InternetCheckTests
{
    private static readonly IReadOnlyList<string> TwoEndpoints =
        new[] { "https://a.example/generate_204", "https://b.example/generate_204" };

    // Identity ordering — deterministic test path, no Random.
    private static IReadOnlyList<string> Identity(IReadOnlyList<string> e) => e;

    [Fact]
    public async Task Route_down_short_circuits_with_zero_dns_and_http()
    {
        int dnsCalls = 0, httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => false,
            dnsProbe: (_, _) => { dnsCalls++; return Task.FromResult(true); },
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204, true)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Equal(0, dnsCalls);   // invariant: no traffic when offline
        Assert.Equal(0, httpCalls);
    }

    [Fact]
    public async Task Dns_failure_skips_http_for_that_endpoint()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(false),
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204, true)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Equal(0, httpCalls);  // invariant: no HTTP when DNS fails
    }

    [Fact]
    public async Task Captive_portal_200_with_body_is_not_online()
    {
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 200, BodyEmpty: false)),
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Empty_204_is_online()
    {
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204, BodyEmpty: true)),
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal("internet", r.Kind);
    }

    [Fact]
    public async Task First_success_short_circuits_remaining_endpoints()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204, true)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(1, httpCalls);  // invariant: first 204 stops; second endpoint not probed
    }

    [Fact]
    public async Task Falls_through_to_second_endpoint_when_first_fails()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) =>
            {
                httpCalls++;
                // First endpoint connect-fails; second returns 204.
                return Task.FromResult(httpCalls == 1 ? HttpProbeResult.Unreachable : new HttpProbeResult(true, 204, true));
            },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(2, httpCalls);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter InternetCheckTests`
Expected: FAIL — `InternetCheck` not found.

- [ ] **Step 3: Implement `InternetCheck.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// The layered, captive-portal-aware "is the internet actually up" check:
/// <list type="number">
/// <item>Route present — a fast OS negative (<c>NetworkInterface.GetIsNetworkAvailable()</c> in
/// production). <see langword="false"/> ⇒ offline with zero external traffic. <see langword="true"/>
/// is untrustworthy (lies about virtual adapters), so it only gates continuation.</item>
/// <item>DNS resolves for the endpoint host.</item>
/// <item>HTTP GET returns <c>204</c> with an empty body — a 200-with-body / redirect ⇒ captive portal.</item>
/// </list>
/// Endpoints are tried in the order returned by the injected <c>order</c> seam (randomised in
/// production), and the first endpoint that returns an empty 204 wins (short-circuit).
/// </summary>
public sealed class InternetCheck : IReadinessCheck
{
    private readonly IReadOnlyList<string> _endpoints;
    private readonly Func<bool> _routeAvailable;
    private readonly DnsProbe _dnsProbe;
    private readonly HttpProbe _httpProbe;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>> _order;

    /// <summary>Creates the internet check with injectable network and ordering seams.</summary>
    public InternetCheck(
        IReadOnlyList<string> endpoints,
        Func<bool> routeAvailable,
        DnsProbe dnsProbe,
        HttpProbe httpProbe,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> order)
    {
        _endpoints = endpoints;
        _routeAvailable = routeAvailable;
        _dnsProbe = dnsProbe;
        _httpProbe = httpProbe;
        _order = order;
    }

    /// <inheritdoc/>
    public async Task<CheckResult> RunAsync(CancellationToken cancellationToken)
    {
        // Rung 1 — cheap OS negative. The common outage (wifi dropped, cable out) ends here with
        // no external requests at all.
        if (!_routeAvailable())
        {
            return new CheckResult("internet", null, false, "no network route");
        }

        IReadOnlyList<string> ordered = _order(_endpoints);
        string lastDetail = "no connectivity endpoints configured";

        foreach (string url in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string host = ExtractHost(url);

            // Rung 2 — DNS. A failure here means this endpoint is unusable; try the next one.
            if (!await _dnsProbe(host, cancellationToken))
            {
                lastDetail = $"DNS resolution failed for {host}";
                continue;
            }

            // Rung 3 — HTTP 204. Empty 204 ⇒ online; anything else ⇒ portal / not online.
            HttpProbeResult probe = await _httpProbe(url, cancellationToken);
            if (!probe.Connected)
            {
                lastDetail = $"connect failed to {url}";
                continue;
            }
            if (probe.StatusCode == 204 && probe.BodyEmpty)
            {
                return new CheckResult("internet", null, true, $"204 via {url}");
            }

            lastDetail = $"unexpected {probe.StatusCode} from {url} (captive portal?)";
        }

        return new CheckResult("internet", null, false, lastDetail);
    }

    private static string ExtractHost(string url)
    {
        // Endpoints are validated as absolute http(s) URIs at options-build time, so this succeeds.
        // Falls back to the raw string defensively rather than throwing inside the poll loop.
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter InternetCheckTests`
Expected: PASS (all 6).

- [ ] **Step 5: Commit**

```
git add src/Winix.Online/InternetCheck.cs tests/Winix.Online.Tests/InternetCheckTests.cs
git commit -m "feat(online): InternetCheck — layered route/DNS/204 with randomised short-circuit"
```

---

## Task 6: `WaitResult` + `WaitEngine` — the poll loop

**Files:**
- Create: `src/Winix.Online/WaitResult.cs`
- Create: `src/Winix.Online/WaitEngine.cs`
- Test: `tests/Winix.Online.Tests/WaitEngineTests.cs`

The clock is an injected `Func<DateTimeOffset>`; the sleep is an injected `Func<TimeSpan, CancellationToken, Task>`. A counting fake on the sleep pins "ready after N cycles ⇒ N−1 sleeps". The AND-invariant test pins that one failing check holds the gate closed even when the other passes.

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/WaitEngineTests.cs`

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class WaitEngineTests
{
    // A check that returns a fixed Ok value, optionally flipping to Ok after a given cycle count.
    private sealed class ScriptedCheck : IReadinessCheck
    {
        private readonly Queue<bool> _results;
        public ScriptedCheck(params bool[] results) => _results = new Queue<bool>(results);
        public Task<CheckResult> RunAsync(CancellationToken ct)
        {
            bool ok = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
            return Task.FromResult(new CheckResult("test", null, ok, ok ? "ok" : "down"));
        }
    }

    private static (WaitEngine engine, Func<int> sleepCount) BuildEngine()
    {
        int sleeps = 0;
        var clock = new FakeClock();
        var engine = new WaitEngine(
            now: () => clock.Now,
            sleep: (d, _) => { sleeps++; clock.Advance(d); return Task.CompletedTask; });
        return (engine, () => sleeps);
    }

    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;
        public void Advance(TimeSpan d) => Now += d;
    }

    private static OnlineOptions Opts(TimeSpan? timeout = null, bool once = false)
        => new(
            checkInternet: false,
            urls: Array.Empty<string>(),
            status: StatusSpec.Default,
            endpoints: Array.Empty<string>(),
            timeout: timeout ?? TimeSpan.FromMinutes(10),
            interval: TimeSpan.FromSeconds(2),
            probeTimeout: TimeSpan.FromSeconds(3),
            once: once,
            verbose: false);

    [Fact]
    public async Task Ready_first_cycle_returns_ready_with_no_sleep()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(true) }, Opts(), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.False(r.TimedOut);
        Assert.Equal(1, r.Attempts);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task Ready_after_three_cycles_sleeps_twice()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        var check = new ScriptedCheck(false, false, true);  // dequeues false, false, then peeks true
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { check }, Opts(), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.Equal(3, r.Attempts);
        Assert.Equal(2, sleeps());  // N-1 sleeps for N cycles
    }

    [Fact]
    public async Task Deadline_exceeded_returns_timed_out_124_shape()
    {
        (WaitEngine engine, _) = BuildEngine();
        // 5s budget, 2s interval, never ready → cycles at t=0,2,4 then t=6 deadline passed.
        WaitResult r = await engine.RunAsync(
            new IReadinessCheck[] { new ScriptedCheck(false) },
            Opts(timeout: TimeSpan.FromSeconds(5)), null, CancellationToken.None);
        Assert.False(r.Ready);
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task Once_ready_returns_ready()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(true) }, Opts(once: true), null, CancellationToken.None);
        Assert.True(r.Ready);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task Once_not_ready_returns_not_ready_not_timed_out()
    {
        (WaitEngine engine, Func<int> sleeps) = BuildEngine();
        WaitResult r = await engine.RunAsync(new IReadinessCheck[] { new ScriptedCheck(false) }, Opts(once: true), null, CancellationToken.None);
        Assert.False(r.Ready);
        Assert.False(r.TimedOut);   // a single-probe miss is NOT a timeout (exit 1, not 124)
        Assert.Equal(1, r.Attempts);
        Assert.Equal(0, sleeps());
    }

    [Fact]
    public async Task And_invariant_one_failing_check_keeps_gate_closed()
    {
        (WaitEngine engine, _) = BuildEngine();
        var checks = new IReadinessCheck[] { new ScriptedCheck(true), new ScriptedCheck(false) };
        WaitResult r = await engine.RunAsync(checks, Opts(once: true), null, CancellationToken.None);
        Assert.False(r.Ready);   // requirement: ALL must pass; one passing is not enough
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter WaitEngineTests`
Expected: FAIL — `WaitEngine`/`WaitResult`/`OnlineOptions` not found. (OnlineOptions is built in Task 7; for now this fails to compile — that is expected. If executing strictly task-by-task, write Task 7's `OnlineOptions` first, then this. See note below.)

> **Ordering note for the executor:** `WaitEngineTests` references `OnlineOptions` (Task 7). Implement `OnlineOptions.cs` (Task 7 Step 3) before running this task's tests, or stub the constructor. The plan keeps `WaitEngine` and `OnlineOptions` in adjacent tasks for this reason — do Task 7 Step 3 first if you hit the missing-type error here.

- [ ] **Step 3: Implement `WaitResult.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Outcome of a wait. <see cref="Ready"/> ⇒ exit 0. <see cref="TimedOut"/> ⇒ exit 124.
/// Neither (only possible under <c>--once</c>) ⇒ exit 1.
/// </summary>
/// <param name="Ready">Every requested check passed in the final cycle.</param>
/// <param name="TimedOut">The wait budget was exhausted before ready (wait mode only).</param>
/// <param name="Attempts">Number of poll cycles run.</param>
/// <param name="Elapsed">Wall time as measured by the injected clock.</param>
/// <param name="LastChecks">Per-check results from the final cycle (for the JSON envelope / summary).</param>
public sealed record WaitResult(
    bool Ready,
    bool TimedOut,
    int Attempts,
    TimeSpan Elapsed,
    IReadOnlyList<CheckResult> LastChecks);
```

- [ ] **Step 4: Implement `WaitEngine.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>
/// Polls a set of <see cref="IReadinessCheck"/>s until all pass, the deadline is reached, or (in
/// <c>--once</c> mode) a single cycle completes. The clock and sleep are injected so the loop is
/// unit-tested with no real waiting (precedent: <c>RetryRunner</c>'s delay seam).
/// </summary>
public sealed class WaitEngine
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    /// <summary>Creates the engine with injected clock and sleep seams.</summary>
    public WaitEngine(Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> sleep)
    {
        _now = now;
        _sleep = sleep;
    }

    /// <summary>
    /// Runs the poll loop. The gate opens only when EVERY check passes in the same cycle.
    /// </summary>
    /// <param name="checks">Checks to AND-combine.</param>
    /// <param name="options">Timing and mode options (<c>--timeout</c> 0 ⇒ no deadline).</param>
    /// <param name="onAttempt">Optional per-cycle callback (cycle number + that cycle's results) for verbose output.</param>
    /// <param name="cancellationToken">User cancel (Ctrl+C).</param>
    public async Task<WaitResult> RunAsync(
        IReadOnlyList<IReadinessCheck> checks,
        OnlineOptions options,
        Action<int, IReadOnlyList<CheckResult>>? onAttempt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset start = _now();
        DateTimeOffset? deadline = options.Timeout == TimeSpan.Zero ? null : start + options.Timeout;
        int attempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            var results = new List<CheckResult>(checks.Count);
            bool allOk = true;
            foreach (IReadinessCheck check in checks)
            {
                CheckResult result = await check.RunAsync(cancellationToken);
                results.Add(result);
                if (!result.Ok)
                {
                    allOk = false;
                }
            }

            onAttempt?.Invoke(attempts, results);

            if (allOk)
            {
                return new WaitResult(true, false, attempts, _now() - start, results);
            }
            if (options.Once)
            {
                // A single-probe miss is a normal negative, NOT a timeout — distinct exit code.
                return new WaitResult(false, false, attempts, _now() - start, results);
            }
            if (deadline.HasValue && _now() >= deadline.Value)
            {
                return new WaitResult(false, true, attempts, _now() - start, results);
            }

            await _sleep(options.Interval, cancellationToken);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass** (after Task 7 Step 3's `OnlineOptions` exists)

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter WaitEngineTests`
Expected: PASS (all 6).

- [ ] **Step 6: Commit**

```
git add src/Winix.Online/WaitResult.cs src/Winix.Online/WaitEngine.cs tests/Winix.Online.Tests/WaitEngineTests.cs
git commit -m "feat(online): WaitEngine poll loop with injected clock/sleep + AND-combine"
```

---

## Task 7: `OnlineOptions` + default endpoint list

**Files:**
- Create: `src/Winix.Online/OnlineOptions.cs`
- Create: `src/Winix.Online/DefaultEndpoints.cs`
- Test: `tests/Winix.Online.Tests/OnlineOptionsTests.cs`

`OnlineOptions` is a validated value object (constructor stores already-parsed values). Parsing/validation of raw strings lives in `Cli` (Task 9). This task also pins the connectivity endpoint list.

- [ ] **Step 1: Implement `DefaultEndpoints.cs`** (URLs verified in Task 11)

```csharp
#nullable enable

using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Built-in connectivity endpoints for <c>--internet</c>. Each MUST return <c>204 No Content</c>
/// with an empty body (a <c>generate_204</c>-style endpoint). Overridable via <c>--endpoint</c>.
/// </summary>
/// <remarks>
/// These exact URLs are verified to return an empty 204 in Task 11 (real-network reconciliation)
/// before ship. Apple/Microsoft NCSI endpoints are deliberately excluded — they return 200-with-body,
/// not 204, and would fail the uniform "expect 204" rule.
/// </remarks>
public static class DefaultEndpoints
{
    /// <summary>The default 204-style connectivity endpoints, in declaration order
    /// (production randomises the order per cycle; see <c>InternetCheck</c>).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "https://www.gstatic.com/generate_204",
        "https://cp.cloudflare.com/generate_204",
    };
}
```

- [ ] **Step 2: Write the failing tests** `tests/Winix.Online.Tests/OnlineOptionsTests.cs`

```csharp
#nullable enable

using System;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class OnlineOptionsTests
{
    [Fact]
    public void Stores_values_verbatim()
    {
        var opts = new OnlineOptions(
            checkInternet: true,
            urls: new[] { "https://a" },
            status: StatusSpec.Default,
            endpoints: DefaultEndpoints.All,
            timeout: TimeSpan.FromMinutes(10),
            interval: TimeSpan.FromSeconds(2),
            probeTimeout: TimeSpan.FromSeconds(3),
            once: false,
            verbose: true);

        Assert.True(opts.CheckInternet);
        Assert.Single(opts.Urls);
        Assert.Equal(TimeSpan.FromMinutes(10), opts.Timeout);
        Assert.True(opts.Verbose);
    }

    [Fact]
    public void Default_endpoint_list_is_all_204_style_urls()
    {
        Assert.NotEmpty(DefaultEndpoints.All);
        foreach (string url in DefaultEndpoints.All)
        {
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && u.Scheme == "https",
                $"endpoint must be an absolute https URL: {url}");
            Assert.Contains("generate_204", url, StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 3: Implement `OnlineOptions.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Validated, parsed configuration for one <c>online</c> invocation. Raw-string parsing and
/// validation live in <c>Cli</c>; this type only holds already-valid values.
/// </summary>
public sealed class OnlineOptions
{
    /// <summary>Whether the layered internet check runs (true for bare <c>online</c>).</summary>
    public bool CheckInternet { get; }

    /// <summary>Named-URL health-wait targets (validated absolute http(s) URLs).</summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>Expected-status matcher for <c>--url</c> checks.</summary>
    public StatusSpec Status { get; }

    /// <summary>Connectivity endpoints for the internet check (override or <see cref="DefaultEndpoints"/>).</summary>
    public IReadOnlyList<string> Endpoints { get; }

    /// <summary>Total wait budget. <see cref="TimeSpan.Zero"/> means wait forever.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Sleep between poll cycles.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Per-probe (DNS / HTTP) timeout.</summary>
    public TimeSpan ProbeTimeout { get; }

    /// <summary>Run exactly one cycle and exit (no waiting).</summary>
    public bool Once { get; }

    /// <summary>Emit per-attempt diagnostics to stderr.</summary>
    public bool Verbose { get; }

    /// <summary>Creates a validated options object.</summary>
    public OnlineOptions(
        bool checkInternet,
        IReadOnlyList<string> urls,
        StatusSpec status,
        IReadOnlyList<string> endpoints,
        TimeSpan timeout,
        TimeSpan interval,
        TimeSpan probeTimeout,
        bool once,
        bool verbose)
    {
        CheckInternet = checkInternet;
        Urls = urls;
        Status = status;
        Endpoints = endpoints;
        Timeout = timeout;
        Interval = interval;
        ProbeTimeout = probeTimeout;
        Once = once;
        Verbose = verbose;
    }
}
```

- [ ] **Step 4: Run the OnlineOptions tests AND the WaitEngine tests (now resolvable)**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter "OnlineOptionsTests|WaitEngineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add src/Winix.Online/OnlineOptions.cs src/Winix.Online/DefaultEndpoints.cs tests/Winix.Online.Tests/OnlineOptionsTests.cs
git commit -m "feat(online): OnlineOptions value object + default 204 endpoint list"
```

---

## Task 8: `Formatting` — JSON envelope + human summary + verbose line

**Files:**
- Create: `src/Winix.Online/Formatting.cs`
- Test: `tests/Winix.Online.Tests/FormattingTests.cs`

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/FormattingTests.cs`

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class FormattingTests
{
    private static WaitResult Ready() => new(
        Ready: true, TimedOut: false, Attempts: 3, Elapsed: TimeSpan.FromMilliseconds(1234),
        LastChecks: new List<CheckResult>
        {
            new("internet", null, true, "204 via https://www.gstatic.com/generate_204"),
            new("url", "https://api/health", true, "200"),
        });

    [Fact]
    public void Json_contains_top_level_fields()
    {
        string json = Formatting.FormatJson(Ready(), "1.2.3");
        Assert.Contains("\"ready\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"timed_out\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"attempts\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"elapsed_ms\":1234", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"online\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":\"1.2.3\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_contains_per_check_objects_with_target()
    {
        string json = Formatting.FormatJson(Ready(), "1.2.3");
        Assert.Contains("\"kind\":\"internet\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"url\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\":\"https://api/health\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_ready_mentions_ready()
    {
        string summary = Formatting.FormatSummary(Ready(), useColor: false);
        Assert.Contains("ready", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_timeout_mentions_timed_out()
    {
        var timedOut = new WaitResult(false, true, 300, TimeSpan.FromMinutes(10),
            new List<CheckResult> { new("internet", null, false, "no network route") });
        string summary = Formatting.FormatSummary(timedOut, useColor: false);
        Assert.Contains("timed out", summary, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter FormattingTests`
Expected: FAIL — `Formatting` not found.

- [ ] **Step 3: Implement `Formatting.cs`**

```csharp
#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.Online;

/// <summary>
/// Output formatting for the online tool. The JSON envelope goes to stdout (own-data tool); the
/// human summary and per-attempt verbose lines go to stderr. All methods are pure.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Builds the <c>--json</c> envelope. Field names: <c>tool, version, ready, timed_out,
    /// elapsed_ms, attempts, checks[]</c> where each check is <c>{ kind, target, ok, detail }</c>.
    /// </summary>
    public static string FormatJson(WaitResult result, string version)
    {
        (System.Text.Json.Utf8JsonWriter writer, System.Buffers.ArrayBufferWriter<byte> buffer) = JsonHelper.CreateWriter();
        writer.WriteStartObject();
        writer.WriteString("tool", "online");
        writer.WriteString("version", version);
        writer.WriteBoolean("ready", result.Ready);
        writer.WriteBoolean("timed_out", result.TimedOut);
        writer.WriteNumber("elapsed_ms", (long)result.Elapsed.TotalMilliseconds);
        writer.WriteNumber("attempts", result.Attempts);
        writer.WriteStartArray("checks");
        foreach (CheckResult check in result.LastChecks)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", check.Kind);
            if (check.Target is null)
            {
                writer.WriteNull("target");
            }
            else
            {
                writer.WriteString("target", check.Target);
            }
            writer.WriteBoolean("ok", check.Ok);
            writer.WriteString("detail", check.Detail);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return JsonHelper.GetString(buffer);
    }

    /// <summary>Builds the one-line human summary written to stderr after the wait completes.</summary>
    public static string FormatSummary(WaitResult result, bool useColor)
    {
        string green = AnsiColor.Green(useColor);
        string red = AnsiColor.Red(useColor);
        string reset = AnsiColor.Reset(useColor);
        long ms = (long)result.Elapsed.TotalMilliseconds;

        if (result.Ready)
        {
            return $"{green}online{reset}: ready after {result.Attempts} attempt(s), {ms}ms";
        }
        if (result.TimedOut)
        {
            string lastFail = FirstFailureDetail(result.LastChecks);
            return $"{red}online{reset}: timed out after {ms}ms ({result.Attempts} attempts) — {lastFail}";
        }
        // --once miss.
        string detail = FirstFailureDetail(result.LastChecks);
        return $"{red}online{reset}: not ready — {detail}";
    }

    /// <summary>Builds a per-attempt verbose line (one per cycle) for <c>-v</c>.</summary>
    public static string FormatAttempt(int attempt, IReadOnlyList<CheckResult> results, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);
        var sb = new StringBuilder();
        sb.Append(dim);
        sb.Append("attempt ");
        sb.Append(attempt.ToString(CultureInfo.InvariantCulture));
        sb.Append(reset);
        foreach (CheckResult check in results)
        {
            sb.Append("  ");
            sb.Append(check.Ok ? "ok" : "wait");
            sb.Append('(');
            sb.Append(check.Kind);
            if (check.Target is not null)
            {
                sb.Append(' ');
                sb.Append(check.Target);
            }
            sb.Append(": ");
            sb.Append(check.Detail);
            sb.Append(')');
        }
        return sb.ToString();
    }

    private static string FirstFailureDetail(IReadOnlyList<CheckResult> results)
    {
        foreach (CheckResult check in results)
        {
            if (!check.Ok)
            {
                return check.Detail;
            }
        }
        return "unknown";
    }
}
```

> **Verify-at-implementation:** confirm `AnsiColor` exposes `Green`, `Red`, `Dim`, `Reset` with the `(bool useColor)` signature (precedent: `Formatting.cs` in whoholds uses `AnsiColor.Dim(useColor)`). If a colour helper is named differently, adjust to the actual ShellKit surface — do not invent one.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter FormattingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add src/Winix.Online/Formatting.cs tests/Winix.Online.Tests/FormattingTests.cs
git commit -m "feat(online): Formatting — JSON envelope, summary, verbose attempt line"
```

---

## Task 9: `Cli.RunAsync` — parser surface, validation, seam wiring, exit mapping

**Files:**
- Create: `src/Winix.Online/Cli.cs`
- Test: `tests/Winix.Online.Tests/CliRunAsyncTests.cs`

`Cli` exposes a clean public `RunAsync(args, stdout, stderr, ct)` (used by Program.cs + the contract test) and an `internal` overload taking an `OnlineSeams` bundle (used by tests to inject fakes). Production builds the real seams (route/DNS/HTTP/order/clock/sleep) when the bundle is null.

- [ ] **Step 1: Write the failing tests** `tests/Winix.Online.Tests/CliRunAsyncTests.cs`

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class CliRunAsyncTests
{
    // Seams that make the internet check pass instantly with no real network or waiting.
    private static OnlineSeams HealthySeams() => new(
        RouteAvailable: () => true,
        DnsProbe: (_, _) => Task.FromResult(true),
        HttpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204, true)),
        EndpointOrder: e => e,
        Now: () => DateTimeOffset.UnixEpoch,
        Sleep: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Once_internet_healthy_returns_0()
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once" }, outW, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Once_internet_down_returns_1()
    {
        var seams = new OnlineSeams(
            RouteAvailable: () => false,
            DnsProbe: (_, _) => Task.FromResult(true),
            HttpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204, true)),
            EndpointOrder: e => e,
            Now: () => DateTimeOffset.UnixEpoch,
            Sleep: (_, _) => Task.CompletedTask);
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, TextWriter.Null, CancellationToken.None, seams);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Timeout_returns_124()
    {
        // Clock that jumps past the 1s budget on the second read so the loop times out fast.
        var times = new Queue<DateTimeOffset>(new[]
        {
            DateTimeOffset.UnixEpoch,                          // start
            DateTimeOffset.UnixEpoch,                          // after cycle 1 (elapsed in result)
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(5),// deadline check — past 1s budget
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(5),// elapsed in result
        });
        var seams = new OnlineSeams(
            RouteAvailable: () => false,                       // never ready
            DnsProbe: (_, _) => Task.FromResult(false),
            HttpProbe: (_, _) => Task.FromResult(HttpProbeResult.Unreachable),
            EndpointOrder: e => e,
            Now: () => times.Count > 1 ? times.Dequeue() : times.Peek(),
            Sleep: (_, _) => Task.CompletedTask);
        int code = await Cli.RunAsync(new[] { "--timeout", "1s" }, TextWriter.Null, TextWriter.Null, CancellationToken.None, seams);
        Assert.Equal(124, code);
    }

    [Fact]
    public async Task Json_envelope_goes_to_stdout()
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once", "--json" }, outW, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(0, code);
        Assert.Contains("\"ready\":true", outW.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"ready\"", errW.ToString(), StringComparison.Ordinal);  // not on stderr
    }

    [Theory]
    [InlineData("--timeout", "notaduration")]
    [InlineData("--status", "abc")]
    [InlineData("--url", "not-a-url")]
    [InlineData("--interval", "0")]
    public async Task Invalid_args_return_125(string flag, string value)
    {
        int code = await Cli.RunAsync(new[] { flag, value }, TextWriter.Null, TextWriter.Null, CancellationToken.None, HealthySeams());
        Assert.Equal(125, code);
    }

    [Fact]
    public async Task Seam_that_throws_is_mapped_not_crashed()
    {
        var seams = new OnlineSeams(
            RouteAvailable: () => true,
            DnsProbe: (_, _) => Task.FromResult(true),
            HttpProbe: (_, _) => throw new InvalidOperationException("boom"),
            EndpointOrder: e => e,
            Now: () => DateTimeOffset.UnixEpoch,
            Sleep: (_, _) => Task.CompletedTask);
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, errW, CancellationToken.None, seams);
        Assert.Equal(ExitCodeForUnexpected, code);
        Assert.Contains("online:", errW.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException\n   at ", errW.ToString(), StringComparison.Ordinal); // no stack trace
    }

    // Unexpected-error exit code chosen in Cli (see implementation): 126 (NotExecutable-style "tool fault").
    private const int ExitCodeForUnexpected = 126;
}
```

> **Verify-at-implementation:** `OnlineSeams` is `internal`, so this test file reaches it via `InternalsVisibleTo` (Task 1 Step 4). `Cli.RunAsync(args, stdout, stderr, ct, OnlineSeams)` is the internal overload.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter CliRunAsyncTests`
Expected: FAIL — `Cli`/`OnlineSeams` not found.

- [ ] **Step 3: Implement `Cli.cs`**

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Online;

/// <summary>
/// Internal bundle of injectable seams. <see langword="null"/> members fall back to the production
/// implementations built in <see cref="Cli.RunAsync(string[], TextWriter, TextWriter, CancellationToken, OnlineSeams?)"/>.
/// Exists for testability only — NOT a generalisation runway (ADR D10).
/// </summary>
internal sealed record OnlineSeams(
    Func<bool>? RouteAvailable = null,
    DnsProbe? DnsProbe = null,
    HttpProbe? HttpProbe = null,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? EndpointOrder = null,
    Func<DateTimeOffset>? Now = null,
    Func<TimeSpan, CancellationToken, Task>? Sleep = null);

/// <summary>
/// Library entry point for the online tool: parses arguments, validates, builds checks, runs the
/// wait loop, and routes output. <c>Program.Main</c> is a thin shell owning console setup and Ctrl+C.
/// </summary>
public static class Cli
{
    private const int ExitReady = 0;
    private const int ExitNotReadyOnce = 1;
    private const int ExitTimedOut = 124;        // GNU timeout(1) convention
    private const int ExitUnexpected = ExitCode.NotExecutable; // 126 — tool fault, distinct from usage 125

    /// <summary>Production entry point. Builds the real network/clock seams.</summary>
    public static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        => RunAsync(args, stdout, stderr, cancellationToken, seams: null);

    /// <summary>
    /// Test/production entry point. When <paramref name="seams"/> (or any member) is null the real
    /// implementation is used.
    /// </summary>
    internal static async Task<int> RunAsync(
        string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken, OnlineSeams? seams)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("online", version)
            .Description("Block until the internet — or a named endpoint — is actually healthy.")
            .Maturity(ToolMaturity.Fresh)
            .Flag("--internet", null, "Wait for working internet (layered, captive-portal-aware). Default when no check flag is given.")
            .ListOption("--url", null, "URL", "Wait until URL returns a status matching --status (repeatable)")
            .ListOption("--endpoint", null, "URL", "Override the built-in 204 connectivity endpoints for --internet (repeatable)")
            .Option("--status", null, "SPEC", "Expected status for --url: 2xx (default), list 200,204, or range 200-299")
            .Option("--timeout", null, "DURATION", "Total wait budget, e.g. 30s, 10m. 0 = forever (default: 10m)")
            .Option("--interval", null, "DURATION", "Sleep between poll cycles (default: 2s)")
            .Option("--probe-timeout", null, "DURATION", "Per-probe DNS/HTTP timeout (default: 3s)")
            .Flag("--once", null, "Run one cycle and exit (no waiting): exit 0 ready, 1 not ready")
            .Flag("--verbose", "-v", "Print per-attempt diagnostics to stderr")
            .StandardFlags()
            .ExitCodes(
                (ExitReady, "Ready — every requested check healthy"),
                (ExitNotReadyOnce, "--once only: checked once, not ready right now"),
                (ExitTimedOut, "Timed out before ready (wait mode)"),
                (ExitCode.UsageError, "Usage error: bad arguments, unparseable duration/status, malformed URL"))
            .Platform("cross-platform",
                replaces: new[] { "wait-for-it.sh", "wait-on" },
                valueOnWindows: "No native 'is the internet up' wait; PowerShell scripting required, and Test-Connection is ICMP (portal-blind)",
                valueOnUnix: "Captive-portal-aware connectivity gate without a Node runtime or bash boilerplate")
            .StdinDescription("Not used")
            .StdoutDescription("--json envelope (own-data tool); otherwise empty")
            .StderrDescription("Human summary and per-attempt verbose lines")
            .Example("online", "Wait up to 10m for working internet")
            .Example("online --once", "Is the internet up right now? (exit 0/1)")
            .Example("online --internet --url https://api/health", "Network back AND my server healthy")
            .Example("online --url https://x --status 200,204", "Wait for an exact status set")
            .ComposesWith("retry", "online && retry --times 3 dotnet test", "Resume work once the network is back")
            .JsonField("tool", "string", "Tool name (\"online\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("ready", "bool", "Whether every requested check passed")
            .JsonField("timed_out", "bool", "Whether the wait budget was exhausted")
            .JsonField("elapsed_ms", "int", "Wall time in milliseconds")
            .JsonField("attempts", "int", "Poll cycles run")
            .JsonField("checks", "object[]", "Per-check results: { kind, target, ok, detail }");

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        // --- Build options (validation → 125 on any failure) ---
        OnlineOptions? options = BuildOptions(result, out string? optionError);
        if (options is null)
        {
            return result.WriteError(optionError ?? "invalid arguments", stderr);
        }

        bool jsonOutput = result.Has("--json");
        // Summary/verbose go to stderr; the JSON envelope is the only thing on stdout.
        bool useColor = result.ResolveColor(checkStdErr: true);

        try
        {
            return await RunWait(options, version, jsonOutput, useColor, stdout, stderr, seams, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User Ctrl+C — conventional interrupted exit.
            SafeWriteLine(stderr, "online: interrupted");
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net — never leak a framework stack trace / SR resource key to the user.
            string msg = SafeError.Describe(ex);
            SafeWriteLine(stderr, $"online: unexpected error: {ex.GetType().Name}: {msg}");
            return ExitUnexpected;
        }
    }

    private static async Task<int> RunWait(
        OnlineOptions options, string version, bool jsonOutput, bool useColor,
        TextWriter stdout, TextWriter stderr, OnlineSeams? seams, CancellationToken cancellationToken)
    {
        // Build production seams when not injected. One SocketsHttpHandler/HttpClient is shared
        // across all probes in this run (connection reuse across poll cycles). AllowAutoRedirect is
        // OFF: a captive portal's 302→login must be visible as a non-204, not silently followed to a
        // 200. The same client serves --url, so a redirecting health URL simply won't match 2xx
        // (keeps waiting) — documented behaviour, diagnosable with -v.
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        Func<bool> route = seams?.RouteAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        DnsProbe dns = seams?.DnsProbe ?? RealDnsProbe;
        HttpProbe httpProbe = seams?.HttpProbe ?? ((url, ct) => RealHttpProbe(http, url, options.ProbeTimeout, ct));
        Func<IReadOnlyList<string>, IReadOnlyList<string>> order = seams?.EndpointOrder ?? Shuffle;
        Func<DateTimeOffset> now = seams?.Now ?? (() => DateTimeOffset.UtcNow);
        Func<TimeSpan, CancellationToken, Task> sleep = seams?.Sleep ?? ((d, ct) => Task.Delay(d, ct));

        var checks = new List<IReadinessCheck>();
        if (options.CheckInternet)
        {
            checks.Add(new InternetCheck(options.Endpoints, route, dns, httpProbe, order));
        }
        foreach (string url in options.Urls)
        {
            checks.Add(new UrlCheck(url, options.Status, httpProbe));
        }

        Action<int, IReadOnlyList<CheckResult>>? onAttempt = null;
        if (options.Verbose && !jsonOutput)
        {
            onAttempt = (attempt, results) => SafeWriteLine(stderr, Formatting.FormatAttempt(attempt, results, useColor));
        }

        var engine = new WaitEngine(now, sleep);
        WaitResult waitResult = await engine.RunAsync(checks, options, onAttempt, cancellationToken);

        if (jsonOutput)
        {
            SafeWriteLine(stdout, Formatting.FormatJson(waitResult, version));
        }
        else
        {
            SafeWriteLine(stderr, Formatting.FormatSummary(waitResult, useColor));
        }

        if (waitResult.Ready) { return ExitReady; }
        if (waitResult.TimedOut) { return ExitTimedOut; }
        return ExitNotReadyOnce;
    }

    /// <summary>Parses and validates raw args into <see cref="OnlineOptions"/>; null + message on error.</summary>
    private static OnlineOptions? BuildOptions(ParseResult result, out string? error)
    {
        error = null;

        // --status
        StatusSpec status = StatusSpec.Default;
        if (result.Has("--status"))
        {
            if (!StatusSpec.TryParse(result.GetString("--status"), out status, out string? statusError))
            {
                error = statusError ?? "invalid --status";
                return null;
            }
        }

        // --url (repeatable) — each must be an absolute http(s) URL.
        string[] urls = result.GetList("--url");
        foreach (string url in urls)
        {
            if (!IsHttpUrl(url))
            {
                error = $"invalid --url value: '{url}' (must be an absolute http/https URL)";
                return null;
            }
        }

        // --endpoint (repeatable) override, else the built-in 204 list.
        string[] endpointOverride = result.GetList("--endpoint");
        foreach (string ep in endpointOverride)
        {
            if (!IsHttpUrl(ep))
            {
                error = $"invalid --endpoint value: '{ep}' (must be an absolute http/https URL)";
                return null;
            }
        }
        IReadOnlyList<string> endpoints = endpointOverride.Length > 0 ? endpointOverride : DefaultEndpoints.All;

        // Bare online (no --internet, no --url) defaults to --internet.
        bool checkInternet = result.Has("--internet") || urls.Length == 0;

        // --timeout (0 = infinite sentinel TimeSpan.Zero)
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        if (result.Has("--timeout"))
        {
            if (!TryParseTimeout(result.GetString("--timeout"), out timeout))
            {
                error = $"invalid --timeout value: '{result.GetString("--timeout")}' (e.g. 30s, 10m, or 0 for forever)";
                return null;
            }
        }

        // --interval (> 0)
        TimeSpan interval = TimeSpan.FromSeconds(2);
        if (result.Has("--interval"))
        {
            if (!DurationParser.TryParse(result.GetString("--interval"), out interval) || interval <= TimeSpan.Zero)
            {
                error = $"invalid --interval value: '{result.GetString("--interval")}' (must be a positive duration, e.g. 2s)";
                return null;
            }
        }

        // --probe-timeout (> 0)
        TimeSpan probeTimeout = TimeSpan.FromSeconds(3);
        if (result.Has("--probe-timeout"))
        {
            if (!DurationParser.TryParse(result.GetString("--probe-timeout"), out probeTimeout) || probeTimeout <= TimeSpan.Zero)
            {
                error = $"invalid --probe-timeout value: '{result.GetString("--probe-timeout")}' (must be a positive duration, e.g. 3s)";
                return null;
            }
        }

        return new OnlineOptions(
            checkInternet, urls, status, endpoints,
            timeout, interval, probeTimeout,
            once: result.Has("--once"),
            verbose: result.Has("--verbose"));
    }

    /// <summary>Parses a timeout, accepting the literal <c>0</c> as the infinite sentinel
    /// (<see cref="TimeSpan.Zero"/>). Any other value goes through <see cref="DurationParser"/>
    /// and must be positive.</summary>
    private static bool TryParseTimeout(string text, out TimeSpan value)
    {
        if (text.Trim() == "0")
        {
            value = TimeSpan.Zero;   // infinite
            return true;
        }
        if (DurationParser.TryParse(text, out value) && value > TimeSpan.Zero)
        {
            return true;
        }
        value = TimeSpan.Zero;
        return false;
    }

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // ── Production seam implementations ──────────────────────────────────────────────

    private static async Task<bool> RealDnsProbe(string host, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;   // malformed host
        }
    }

    private static async Task<HttpProbeResult> RealHttpProbe(HttpClient http, string url, TimeSpan probeTimeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(probeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
            int status = (int)response.StatusCode;
            byte[] body = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
            return new HttpProbeResult(true, status, body.Length == 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // user cancel — abort the wait
        }
        catch (OperationCanceledException)
        {
            return HttpProbeResult.Unreachable;   // per-probe timeout
        }
        catch (HttpRequestException)
        {
            return HttpProbeResult.Unreachable;   // connect/TLS/DNS-at-request failure
        }
    }

    private static IReadOnlyList<string> Shuffle(IReadOnlyList<string> items)
    {
        string[] arr = items.ToArray();
        // Fisher–Yates with the shared RNG. Randomised order is fed via this seam so tests stay
        // deterministic (they inject identity); Random never appears in the test path (ADR D6/D10).
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { /* downstream pipe closed */ }
        catch (ObjectDisposedException) { /* writer disposed */ }
    }

    private static string GetVersion()
    {
        string raw = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
```

> **Verify-at-implementation:**
> - Confirm `CommandLineParser.ListOption(longName, shortName, placeholder, description)` matches the signature used here (verified in `src/Yort.ShellKit/CommandLineParser.cs:154`).
> - Confirm `.Platform(...)`, `.ComposesWith(...)`, `.JsonField(...)`, `.ExitCodes(...)` chain members exist with these signatures (precedent: `src/Winix.Retry/Cli.cs`). If `.ComposesWith` for `retry` triggers the "composes_with snippet verified against target parser" rule, ensure `online && retry ...` is shell-valid (it is — plain `&&` chaining).
> - Confirm `DurationParser.TryParse("2s", out _)` etc. behave as in retry. The literal `0`-as-infinite is handled before `DurationParser` (DurationParser may reject a bare `0`).

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter CliRunAsyncTests`
Expected: PASS (all cases, including the seam-throws → 126 mapping and JSON-to-stdout).

- [ ] **Step 5: Run the whole library test project**

Run: `dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj`
Expected: PASS (StatusSpec, UrlCheck, InternetCheck, WaitEngine, OnlineOptions, Formatting, CliRunAsync).

- [ ] **Step 6: Commit**

```
git add src/Winix.Online/Cli.cs tests/Winix.Online.Tests/CliRunAsyncTests.cs
git commit -m "feat(online): Cli.RunAsync — parse, validate, wire seams, exit mapping"
```

---

## Task 10: `Program.cs` — thin console entry

**Files:**
- Create: `src/online/Program.cs`

- [ ] **Step 1: Implement `Program.cs`** (mirrors `src/retry/Program.cs` Ctrl+C handling)

```csharp
using System;
using System.Threading;
using Winix.Online;
using Yort.ShellKit;

namespace Online;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C handling.
    /// All parsing, validation, and the wait loop live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Ctrl+C stays in Main: Console.CancelKeyPress is a process-global static event. The named
        // handler is unregistered in finally; the catch guards a second Ctrl+C racing CTS disposal
        // during AOT teardown. Reference: src/retry/Program.cs.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* raced with shutdown — safe to drop */ }
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return Cli.RunAsync(args, Console.Out, Console.Error, cts.Token).GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
```

- [ ] **Step 2: Build the console app**

Run: `dotnet build src/online/online.csproj`
Expected: Build succeeded, 0 warnings. (The man-page `Content Include` will warn/err if `man/man1/online.1` is missing — create a placeholder now if the build fails on it, then fill it in Task 13. To unblock: `mkdir -p src/online/man/man1 && echo ".TH ONLINE 1" > src/online/man/man1/online.1` as a temporary stub.)

- [ ] **Step 3: Manual smoke — `--once` against the real network (online)**

Run: `dotnet run --project src/online -- --once -v`
Expected (when online): per-attempt line, summary "online: ready ...", exit 0. Check: `echo $?` → 0.

- [ ] **Step 4: Manual smoke — `--help` and `--describe`**

Run: `dotnet run --project src/online -- --help`
Expected: usage with all flags, examples, exit codes.
Run: `dotnet run --project src/online -- --describe`
Expected: JSON describe envelope with `maturity":"fresh"`.

- [ ] **Step 5: Commit**

```
git add src/online/Program.cs
git commit -m "feat(online): thin console entry point with Ctrl+C handling"
```

---

## Task 11: Pin + verify connectivity endpoints; real-network integration tests

**Files:**
- Modify: `src/Winix.Online/DefaultEndpoints.cs` (only if verification finds a wrong URL)
- Create: `tests/Winix.Online.Tests/IntegrationTests.cs`

- [ ] **Step 1: Verify each default endpoint returns an empty 204** (real network; do this on a non-captive connection)

Run:
```
curl -s -o /dev/null -w "%{http_code}\n" https://www.gstatic.com/generate_204
curl -s -o /dev/null -w "%{http_code}\n" https://cp.cloudflare.com/generate_204
```
Expected: each prints `204`. If any does not, replace it in `DefaultEndpoints.All` with a verified `generate_204` endpoint and re-run. **Do not ship an unverified URL.**

- [ ] **Step 2: Write opt-in real-network integration tests** `tests/Winix.Online.Tests/IntegrationTests.cs`

Gated on an env var so CI without outbound network stays deterministic; this is the **ship gate** for wire correctness (the unit fakes verify shape only — see CLAUDE.md protocol-fake caution).

```csharp
#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class IntegrationTests
{
    // Opt-in: set WINIX_ONLINE_INTEGRATION=1 to run (requires real, non-captive internet).
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WINIX_ONLINE_INTEGRATION") == "1";

    [SkippableFact]
    public async Task Internet_once_is_ready_on_real_network()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, code);
    }

    [SkippableFact]
    public async Task Url_against_known_204_endpoint_is_ready()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        int code = await Cli.RunAsync(
            new[] { "--url", "https://www.gstatic.com/generate_204", "--status", "204", "--once" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, code);
    }

    [SkippableFact]
    public async Task Url_against_unreachable_host_times_out_124()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        // RFC 5737 TEST-NET-1 — guaranteed non-routable; short budget so the test is quick.
        int code = await Cli.RunAsync(
            new[] { "--url", "https://192.0.2.1/health", "--timeout", "3s", "--interval", "1s", "--probe-timeout", "1s" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(124, code);
    }
}
```

- [ ] **Step 3: Run the opt-in integration suite (locally, online)**

Run (bash):
```
WINIX_ONLINE_INTEGRATION=1 dotnet test tests/Winix.Online.Tests/Winix.Online.Tests.csproj --filter IntegrationTests
```
Expected: 3 passed (real DNS + HTTP). Without the env var: 3 skipped.

- [ ] **Step 4: Commit**

```
git add src/Winix.Online/DefaultEndpoints.cs tests/Winix.Online.Tests/IntegrationTests.cs
git commit -m "test(online): pin verified 204 endpoints + opt-in real-network integration tests"
```

---

## Task 12: Contract-test registration + snapshot

**Files:**
- Modify: `tests/Winix.Contract.Tests/DescribeSurfaces.cs`
- Create: `tests/Winix.Contract.Tests/snapshots/online.describe.json` (generated)
- Modify: `tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj` (add project reference if not already covering all tools)

- [ ] **Step 1: Add the using-alias for the online Cli** near the other aliases in `DescribeSurfaces.cs`

```csharp
using OnlineCli = global::Winix.Online.Cli;
```

- [ ] **Step 2: Register the describe surface** in the `All` dictionary

```csharp
            // ── online ────────────────────────────────────────────────────────────────
            // Signature from Winix.Online.Tests/CliRunAsyncTests.cs:
            //   Cli.RunAsync(args, stdout, stderr, CancellationToken.None)  [public overload]
            ["online"] = args => OnlineCli.RunAsync(
                args, TextWriter.Null, TextWriter.Null, CancellationToken.None),
```

- [ ] **Step 3: Ensure the contract test project references `Winix.Online`**

Check `tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj` includes:
```xml
<ProjectReference Include="..\..\src\Winix.Online\Winix.Online.csproj" />
```
Add it if missing (follow the existing per-tool `ProjectReference` pattern).

- [ ] **Step 4: Generate and commit the snapshot.** Run the contract test once to emit/compare the snapshot.

Run: `dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter online`
Expected: On first run the test framework writes `snapshots/online.describe.json` (or fails asking you to create it — follow the existing tool's snapshot workflow; inspect a sibling like `snapshots/retry.describe.json` for format). Verify the snapshot shows `"maturity":"fresh"` and the full flag set, then re-run to confirm PASS.

- [ ] **Step 5: Commit**

```
git add tests/Winix.Contract.Tests/DescribeSurfaces.cs tests/Winix.Contract.Tests/snapshots/online.describe.json tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj
git commit -m "test(online): register --describe contract surface + snapshot"
```

---

## Task 13: Docs — README, man page, AI guide, llms.txt

**Files:**
- Create: `src/online/README.md`
- Create: `src/online/online.1.md` (pandoc source)
- Create: `src/online/man/man1/online.1` (generated)
- Create: `docs/ai/online.md`
- Modify: `llms.txt`

- [ ] **Step 1: Write `src/online/README.md`** following the existing pattern (see `src/retry/README.md`): description, install (scoop / nuget / winget / from-source), usage examples, options table, exit-code table, colour section. Include the layered-check explanation, the captive-portal note, the `AllowAutoRedirect=off` behaviour for `--url`, and the `--once` agent pattern (`online --internet --url … && resume`). Mirror the design doc's CLI surface table verbatim.

- [ ] **Step 2: Write `src/online/online.1.md`** (pandoc source) with NAME / SYNOPSIS / DESCRIPTION / OPTIONS / EXIT STATUS / EXAMPLES sections. Mirror the README content.

- [ ] **Step 3: Generate the groff man page**

Run: `pandoc -s -t man src/online/online.1.md -o src/online/man/man1/online.1`
Expected: `online.1` regenerated (replaces the Task 10 stub). Safety-diff: `git diff src/online/man/man1/online.1` should show only real content (no stray pandoc artefacts).

- [ ] **Step 4: Write `docs/ai/online.md`** — the agent guide: when to reach for `online` vs `nc --check` / `retry`, the exit-code contract (0/1/124/125), the `--once` polling pattern, and the "network back AND my server healthy" one-call recipe. Follow the format of an existing `docs/ai/*.md`.

- [ ] **Step 5: Add `online` to `llms.txt`** in the structured tool catalogue, matching the existing entry format (one-line description + key flags). Place it near the network tools (`nc`, etc.).

- [ ] **Step 6: Doc↔behaviour reconciliation** (verification oracle — CLAUDE.md). Enumerate every user-facing claim across `--help`, `--describe`, `README.md`, `online.1`, `docs/ai/online.md`, and `llms.txt`, and run the command that should demonstrate each. Hunt specifically for a claim that is FALSE (precedent failures: a documented feature that was stubbed; a no-op flag). Fix any mismatch. Record the reconciliation in the commit message.

- [ ] **Step 7: Commit**

```
git add src/online/README.md src/online/online.1.md src/online/man/man1/online.1 docs/ai/online.md llms.txt
git commit -m "docs(online): README, man page, AI guide, llms.txt + doc/behaviour reconciliation"
```

---

## Task 14: Release wiring — scoop, release.yml, post-publish.yml, smokes

**Files:**
- Create: `bucket/online.json`
- Modify: `.github/workflows/release.yml`
- Modify: `.github/workflows/post-publish.yml`
- Modify: `.github/workflows/manual-smoke.yml`
- Create: `tests/smoke/online/run-smokes.sh` (path per existing smoke-fixture convention — inspect a sibling first)

- [ ] **Step 1: Create `bucket/online.json`** scoop manifest, copying the structure of an existing per-tool manifest (e.g. `bucket/retry.json`) — name, description, homepage, license, AOT-binary URL pattern, `bin: online.exe`, checkver/autoupdate blocks. Do NOT touch `bucket/winix.json`.

- [ ] **Step 2: Add `online` to `.github/workflows/release.yml`** — per `matrix.rid`: a `dotnet publish src/online/online.csproj` step, a `dotnet pack` step, per-tool zip steps (Linux/macOS + Windows), the combined-zip `Copy-Item`, and the `tools: { … }` map entry. Mirror the `retry` lines exactly.

- [ ] **Step 3: Add `online` to `.github/workflows/post-publish.yml`** — an `update_manifest bucket/online.json …` line and a `generate_manifests "online" "Online" "Block until the internet — or a named endpoint — is actually healthy." "network,connectivity,readiness,wait,captive-portal"` line (3–5 winget domain tags, aligned with the csproj `<PackageTags>`).

- [ ] **Step 4: Create the native-capability smoke fixture** `tests/smoke/online/run-smokes.sh` (confirm the exact directory by inspecting an existing fixture). Derive cases from the README options/exit-code surface:
  - `online --once` while online → expect exit 0
  - `online --url <known-204> --status 204 --once` → expect exit 0
  - `online --url https://192.0.2.1/health --timeout 3s --interval 1s --probe-timeout 1s` → expect exit 124
  - `online --status bogus` → expect exit 125
  - `online --help` / `online --describe` → expect exit 0
  Include a Windows (`W-`) section per the glob-expansion-era fixture convention if applicable (online has no positionals, so no glob section needed — note this).

- [ ] **Step 5: Add `online` to `.github/workflows/manual-smoke.yml`** — tool list entry, `runner_for` map entry, and sed retarget rule, matching an existing network tool.

- [ ] **Step 6: Lint the workflow YAML locally** (catch indentation/anchor errors before pushing). If `actionlint` is available: `actionlint .github/workflows/release.yml .github/workflows/post-publish.yml .github/workflows/manual-smoke.yml`. Otherwise eyeball-diff against the `retry` entries.

- [ ] **Step 7: Commit**

```
git add bucket/online.json .github/workflows/release.yml .github/workflows/post-publish.yml .github/workflows/manual-smoke.yml tests/smoke/online/run-smokes.sh
git commit -m "build(online): scoop manifest, release + post-publish wiring, smoke fixture"
```

---

## Task 15: `CLAUDE.md` updates + full-suite verification

**Files:**
- Modify: `CLAUDE.md` (project layout, NuGet IDs list, scoop manifests list)

- [ ] **Step 1: Update `CLAUDE.md` project layout** — add `src/Winix.Online/`, `src/online/`, and `tests/Winix.Online.Tests/` lines in the layout block (alphabetical/grouped with the other network tools).

- [ ] **Step 2: Add `Winix.Online` to the NuGet package IDs list** in `CLAUDE.md`.

- [ ] **Step 3: Add `online.json` to the scoop manifests list** in `CLAUDE.md`.

- [ ] **Step 4: Full-solution build (warnings are errors)**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Full-solution test**

Run: `dotnet test Winix.sln`
Expected: All passing (online's unit tests + contract test + every other tool's suite). Integration tests skip (env var unset).

- [ ] **Step 6: AOT publish smoke (native binary actually works)**

Run: `dotnet publish src/online/online.csproj -c Release -r win-x64`
Then run the published binary: `--once -v` (online → exit 0), `--help`, `--describe`. Confirm the native binary behaves identically to `dotnet run`.

- [ ] **Step 7: Commit**

```
git add CLAUDE.md
git commit -m "docs(online): register tool in CLAUDE.md (layout, package IDs, scoop list)"
```

---

## Self-Review (run before declaring the plan executed)

Spec coverage — every design-doc section maps to a task:
- CLI surface (§2) → Task 9 (parser) + Task 7 (options)
- `--internet` layered semantics (§3) → Task 5
- `--url` health-wait (§3) → Task 4
- Wait loop + exit codes (§3/§4) → Task 6 + Task 9 mapping
- Architecture seams (§5) → Tasks 2, 5, 6, 9 (internal `OnlineSeams`)
- Output routing / JSON envelope (§5) → Task 8 + Task 9 (stdout/stderr split)
- Testing (§6): WaitEngine/InternetCheck/UrlCheck unit + Cli seam-failure + integration + smokes → Tasks 4–9, 11, 14
- Deferred (§7) → not implemented, by design (no tasks — correct)

ADR decisions honoured: D4 (no `-- command` — none added), D5 (route negative-only + 204), D6 (randomised short-circuit via order seam), D7 (5xx/429 keep waiting — UrlCheck tests), D8 (10m default, 0=infinite, 124/1 codes), D9 (`--json` to stdout — CliRunAsync test), D10 (seams internal, testability-only).

Open verify-at-implementation items the executor MUST resolve (do not skip):
1. Default endpoint URLs return empty 204 (Task 11 Step 1).
2. `AnsiColor.Green/Red/Dim/Reset(bool)` signatures (Task 8).
3. `CommandLineParser.ListOption` / `.Platform` / `.ComposesWith` / `.JsonField` / `.ExitCodes` chain signatures (Task 9).
4. `DurationParser.TryParse` behaviour and the bare-`0`-as-infinite handling (Task 9).
5. Contract-test snapshot workflow + exact smoke-fixture directory (Tasks 12, 14).

---

## Execution Handoff

Mandatory gate before ANY code: **adversarial-plan-review** on this plan (global CLAUDE.md rule). After that passes and findings are integrated, choose:

1. **Subagent-Driven (recommended)** — fresh subagent per task, two-stage review between tasks.
2. **Inline Execution** — batch with checkpoints.
