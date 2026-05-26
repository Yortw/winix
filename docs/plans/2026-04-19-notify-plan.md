# notify Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `notify`, a one-shot CLI that sends a desktop notification and/or a push to ntfy.sh in a single invocation, consistent across Windows/macOS/Linux.

**Architecture:** Strategy interface (`IBackend`) with four implementations (WindowsToast, MacOsAppleScript, LinuxNotifySend, Ntfy). A `Dispatcher` selects backends based on platform + flags and runs them in parallel. Console app (`src/notify/`) is a thin orchestrator that owns argv parsing dispatch, exit-code mapping, and output formatting.

**Tech Stack:** .NET 10, AOT-compiled (`PublishAot=true`), xUnit, nullable reference types, warnings as errors. Direct WinRT COM for Windows toast (no PowerShell shellout). `notify-send` shellout for Linux. `osascript` shellout for macOS. `HttpClient` for ntfy.sh. Project conventions: file-level `#nullable enable`, `ProcessStartInfo.ArgumentList` (never string `Arguments`), full braces, query-syntax LINQ, no range/index expressions (`bytes[^1]` etc).

**Reference docs:**
- Design: `docs/plans/2026-04-19-notify-design.md`
- ADR: `docs/plans/2026-04-19-notify-adr.md`
- Most-recent comparable tool (digest): `src/Winix.Digest/`, `src/digest/`, `tests/Winix.Digest.Tests/`
- Most-recent ArgParser pattern: `src/Winix.Digest/ArgParser.cs` and `src/Winix.Ids/ArgParser.cs`
- Suite-wide CLI conventions: `CLAUDE.md` at repo root

---

## Task 1: Project scaffolding

Create the three projects (library, console app, tests), wire them into `Winix.sln`, and put a stub `Program.cs` in place that prints "not yet implemented" and returns the usage-error exit code.

**Files:**
- Create: `src/Winix.Notify/Winix.Notify.csproj`
- Create: `src/notify/notify.csproj`
- Create: `src/notify/Program.cs`
- Create: `src/notify/README.md` (placeholder; full README in T11)
- Create: `tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj`
- Modify: `Winix.sln` (add three project entries)

- [ ] **Step 1: Create `src/Winix.Notify/Winix.Notify.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.Notify.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `src/notify/notify.csproj`**

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
    <ToolCommandName>notify</ToolCommandName>
    <PackageId>Winix.Notify</PackageId>
    <Description>Cross-platform desktop notifications + ntfy.sh push notifications.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Notify\Winix.Notify.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `src/notify/Program.cs` stub**

```csharp
using Yort.ShellKit;

namespace Notify;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine("notify: not yet implemented");
        return ExitCode.UsageError;
    }
}
```

- [ ] **Step 4: Create `src/notify/README.md` placeholder**

```markdown
See [main project README](../../README.md). Full tool README populated in a later commit.
```

- [ ] **Step 5: Create `tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj`**

Look at `tests/Winix.Digest.Tests/Winix.Digest.Tests.csproj` for the canonical shape (xunit, coverlet, project reference, ImplicitUsings off if it's off there, etc.). Mirror exactly, swapping `Digest` → `Notify`.

- [ ] **Step 6: Add the three projects to `Winix.sln`**

Open `Winix.sln`, find where `Winix.Digest` / `digest` / `Winix.Digest.Tests` are listed (Project entries + GlobalSection configuration entries), and add parallel entries for `Winix.Notify`, `notify`, `Winix.Notify.Tests`. Use new GUIDs (e.g. `dotnet new sln add` if you want to let the tool generate them, then move into the right solution folders).

- [ ] **Step 7: Build the solution**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors. The new projects compile.

- [ ] **Step 8: Run notify stub**

Run: `dotnet run --project src/notify/notify.csproj`
Expected: `notify: not yet implemented` on stderr, exit code 125.

- [ ] **Step 9: Commit**

```bash
git add src/Winix.Notify src/notify tests/Winix.Notify.Tests Winix.sln
git commit -m "feat(notify): add project scaffolding"
```

---

## Task 2: Core types + test fakes

Create the records + interface that everything else builds on. Pure data — no logic.

**Files:**
- Create: `src/Winix.Notify/Urgency.cs`
- Create: `src/Winix.Notify/NotifyMessage.cs`
- Create: `src/Winix.Notify/BackendResult.cs`
- Create: `src/Winix.Notify/IBackend.cs`
- Create: `src/Winix.Notify/NotifyOptions.cs`
- Create: `tests/Winix.Notify.Tests/Fakes/FakeBackend.cs`
- Create: `tests/Winix.Notify.Tests/Fakes/FakeHttpMessageHandler.cs`

- [ ] **Step 1: Create `Urgency.cs`**

```csharp
#nullable enable
namespace Winix.Notify;

/// <summary>Urgency levels mapped to platform-specific behaviours per the design urgency table.</summary>
public enum Urgency
{
    /// <summary>Quiet, non-attention-seeking notification. Silent on all backends.</summary>
    Low,
    /// <summary>Default. Standard toast/notification appearance.</summary>
    Normal,
    /// <summary>Attention-seeking. Sound on every backend, ntfy priority 5, Windows urgent scenario.</summary>
    Critical,
}
```

- [ ] **Step 2: Create `NotifyMessage.cs`**

```csharp
#nullable enable
namespace Winix.Notify;

/// <summary>The user-visible payload — title (required), optional body, urgency, optional icon path.</summary>
/// <param name="Title">Notification headline. Always populated.</param>
/// <param name="Body">Optional second line of text.</param>
/// <param name="Urgency">Urgency level; backends translate per the design's urgency table.</param>
/// <param name="IconPath">Optional path to an icon file. Best-effort per backend (libnotify accepts paths/named, Windows toast accepts file paths, macOS osascript ignores).</param>
public sealed record NotifyMessage(
    string Title,
    string? Body,
    Urgency Urgency,
    string? IconPath);
```

- [ ] **Step 3: Create `BackendResult.cs`**

```csharp
#nullable enable
namespace Winix.Notify;

/// <summary>One backend's send outcome. <see cref="Ok"/> false means the backend was attempted but failed; <see cref="Error"/> carries the user-facing reason.</summary>
/// <param name="BackendName">Stable identifier: "windows-toast", "macos-osascript", "linux-notify-send", "ntfy".</param>
/// <param name="Ok">True if the backend successfully delivered the notification.</param>
/// <param name="Error">User-facing failure message when <see cref="Ok"/> is false; null on success.</param>
/// <param name="Detail">Optional structured detail for JSON output (e.g. ntfy server + topic).</param>
public sealed record BackendResult(
    string BackendName,
    bool Ok,
    string? Error,
    IReadOnlyDictionary<string, string>? Detail);
```

- [ ] **Step 4: Create `IBackend.cs`**

```csharp
#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>One notification destination. Implementations are stateless; the dispatcher selects which backends to invoke per call.</summary>
public interface IBackend
{
    /// <summary>Stable backend identifier ("windows-toast", "ntfy", etc.). Used in JSON output and stderr warnings.</summary>
    string Name { get; }

    /// <summary>Sends the message. Should not throw — convert exceptions into <see cref="BackendResult"/> with <c>Ok=false</c>.</summary>
    Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct);
}
```

- [ ] **Step 5: Create `NotifyOptions.cs`**

```csharp
#nullable enable
namespace Winix.Notify;

/// <summary>Parsed CLI options. Constructed by <see cref="ArgParser"/>; consumed by Program.cs and Dispatcher.</summary>
public sealed record NotifyOptions(
    string Title,
    string? Body,
    Urgency Urgency,
    string? IconPath,
    bool DesktopEnabled,
    bool NtfyEnabled,
    string? NtfyTopic,
    string NtfyServer,
    string? NtfyToken,
    bool Strict,
    bool Json)
{
    /// <summary>Convert to the message that backends consume.</summary>
    public NotifyMessage ToMessage() => new(Title, Body, Urgency, IconPath);

    /// <summary>Default values for most fields except Title (required).</summary>
    public static NotifyOptions ForTests(string title) => new(
        Title: title,
        Body: null,
        Urgency: Urgency.Normal,
        IconPath: null,
        DesktopEnabled: true,
        NtfyEnabled: false,
        NtfyTopic: null,
        NtfyServer: "https://ntfy.sh",
        NtfyToken: null,
        Strict: false,
        Json: false);
}
```

- [ ] **Step 6: Create `tests/Winix.Notify.Tests/Fakes/FakeBackend.cs`**

```csharp
#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Notify;

namespace Winix.Notify.Tests.Fakes;

/// <summary>Test double for <see cref="IBackend"/>. Records every call; lets tests force success or failure.</summary>
public sealed class FakeBackend : IBackend
{
    public string Name { get; }
    public bool ShouldSucceed { get; set; } = true;
    public string FailureMessage { get; set; } = "fake failure";
    public int DelayMs { get; set; } = 0;
    public List<NotifyMessage> Received { get; } = new();

    public FakeBackend(string name) { Name = name; }

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, ct);
        }
        Received.Add(message);
        return ShouldSucceed
            ? new BackendResult(Name, true, null, null)
            : new BackendResult(Name, false, FailureMessage, null);
    }
}
```

- [ ] **Step 7: Create `tests/Winix.Notify.Tests/Fakes/FakeHttpMessageHandler.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify.Tests.Fakes;

/// <summary>HttpClient handler that captures the request and returns a canned response. Lets ntfy backend tests run with no real network.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "";
    public Exception? ThrowOnSend { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }
        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody),
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 8: Build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/Winix.Notify tests/Winix.Notify.Tests/Fakes
git commit -m "feat(notify): add core types and test fakes"
```

---

## Task 3: NtfyBackend

Implement the ntfy.sh backend — pure HTTP, easiest to test, gets us a working backend up front.

**Files:**
- Create: `src/Winix.Notify/Backends/NtfyBackend.cs`
- Create: `tests/Winix.Notify.Tests/NtfyBackendTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Notify.Tests/NtfyBackendTests.cs
#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Winix.Notify;
using Winix.Notify.Tests.Fakes;

namespace Winix.Notify.Tests;

public class NtfyBackendTests
{
    [Fact]
    public async Task Send_PostsToCorrectUrl()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, server: "https://ntfy.sh", topic: "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("title", "body", Urgency.Normal, null), CancellationToken.None);

        Assert.Single(fake.Requests);
        var req = fake.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://ntfy.sh/alerts", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Send_BodyIsTheMessageBody()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("title", "the body text", Urgency.Normal, null), CancellationToken.None);

        string body = await fake.Requests[0].Content!.ReadAsStringAsync();
        Assert.Equal("the body text", body);
    }

    [Fact]
    public async Task Send_NullBody_PostsTitleAsBody()
    {
        // ntfy requires a non-empty body; when --body absent, send title as body.
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("the title", null, Urgency.Normal, null), CancellationToken.None);

        string body = await fake.Requests[0].Content!.ReadAsStringAsync();
        Assert.Equal("the title", body);
    }

    [Fact]
    public async Task Send_TitleHeader()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("the title", "body", Urgency.Normal, null), CancellationToken.None);

        Assert.True(fake.Requests[0].Headers.TryGetValues("Title", out var values));
        Assert.Equal("the title", System.Linq.Enumerable.Single(values!));
    }

    [Theory]
    [InlineData(Urgency.Low, "2")]
    [InlineData(Urgency.Normal, "3")]
    [InlineData(Urgency.Critical, "5")]
    public async Task Send_PriorityHeader_MapsUrgency(Urgency urgency, string expected)
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        await backend.SendAsync(new NotifyMessage("t", "b", urgency, null), CancellationToken.None);

        Assert.True(fake.Requests[0].Headers.TryGetValues("Priority", out var values));
        Assert.Equal(expected, System.Linq.Enumerable.Single(values!));
    }

    [Fact]
    public async Task Send_TokenSet_AddsBearerAuthorization()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", token: "tk_abc123");

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        var auth = fake.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("tk_abc123", auth.Parameter);
    }

    [Fact]
    public async Task Send_NoToken_NoAuthorizationHeader()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.Null(fake.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Send_HttpError_ReturnsFailure()
    {
        var fake = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.Forbidden, ResponseBody = "forbidden" };
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("403", result.Error);
        Assert.Equal("ntfy", result.BackendName);
    }

    [Fact]
    public async Task Send_NetworkException_ReturnsFailure()
    {
        var fake = new FakeHttpMessageHandler { ThrowOnSend = new HttpRequestException("connect timeout") };
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.sh", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("connect timeout", result.Error);
    }

    [Fact]
    public async Task Send_CustomServer_UsesIt()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, server: "https://ntfy.example.com", topic: "alerts", token: null);

        await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.Equal("https://ntfy.example.com/alerts", fake.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Send_DetailIncludesServerAndTopic()
    {
        var fake = new FakeHttpMessageHandler();
        var http = new HttpClient(fake);
        var backend = new NtfyBackend(http, "https://ntfy.example.com", "alerts", null);

        var result = await backend.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(result.Detail);
        Assert.Equal("https://ntfy.example.com", result.Detail!["server"]);
        Assert.Equal("alerts", result.Detail["topic"]);
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~NtfyBackendTests`
Expected: compile error (`NtfyBackend` doesn't exist).

- [ ] **Step 3: Implement `NtfyBackend.cs`**

```csharp
// src/Winix.Notify/Backends/NtfyBackend.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a push notification to an ntfy.sh-compatible server (hosted or self-hosted).
/// Stateless — owns no I/O state; the <see cref="HttpClient"/> is injected so tests can supply a fake handler.
/// </summary>
public sealed class NtfyBackend : IBackend
{
    private readonly HttpClient _http;
    private readonly string _server;
    private readonly string _topic;
    private readonly string? _token;

    public string Name => "ntfy";

    /// <summary>
    /// Creates a backend bound to a specific server + topic.
    /// </summary>
    /// <param name="http">HttpClient (production: a static singleton; tests: with a fake handler).</param>
    /// <param name="server">Server base URL, e.g. https://ntfy.sh. Trailing slash optional.</param>
    /// <param name="topic">Topic name. Must be non-empty; ArgParser validates upstream.</param>
    /// <param name="token">Optional bearer token for self-hosted ntfy with access control.</param>
    public NtfyBackend(HttpClient http, string server, string topic, string? token)
    {
        _http = http;
        _server = server.TrimEnd('/');
        _topic = topic;
        _token = token;
    }

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        var detail = new Dictionary<string, string>
        {
            ["server"] = _server,
            ["topic"] = _topic,
        };

        try
        {
            string url = $"{_server}/{_topic}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            // ntfy requires a non-empty body — send title as body when no body provided.
            string bodyText = message.Body ?? message.Title;
            request.Content = new StringContent(bodyText, Encoding.UTF8, "text/plain");
            request.Headers.TryAddWithoutValidation("Title", message.Title);
            request.Headers.TryAddWithoutValidation("Priority", PriorityFor(message.Urgency));
            if (_token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode;
                return new BackendResult(Name, false, $"ntfy POST failed: {code} {response.ReasonPhrase}", detail);
            }

            return new BackendResult(Name, true, null, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new BackendResult(Name, false, $"ntfy POST failed: {ex.Message}", detail);
        }
    }

    private static string PriorityFor(Urgency urgency) => urgency switch
    {
        Urgency.Low => "2",
        Urgency.Normal => "3",
        Urgency.Critical => "5",
        _ => "3",
    };
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~NtfyBackendTests`
Expected: 12 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/Backends/NtfyBackend.cs tests/Winix.Notify.Tests/NtfyBackendTests.cs
git commit -m "feat(notify): add NtfyBackend with HttpClient injection for testability"
```

---

## Task 4: LinuxNotifySendBackend

Shells out to `notify-send`. Has no automated tests beyond a "constructed without error" smoke (real backend behaviour is impossible to verify in CI). Implementation is small.

**Files:**
- Create: `src/Winix.Notify/Backends/LinuxNotifySendBackend.cs`
- Create: `tests/Winix.Notify.Tests/LinuxNotifySendBackendTests.cs`

- [ ] **Step 1: Write the (limited) tests**

```csharp
// tests/Winix.Notify.Tests/LinuxNotifySendBackendTests.cs
#nullable enable
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class LinuxNotifySendBackendTests
{
    [Fact]
    public void Backend_Name_IsLinuxNotifySend()
    {
        var b = new LinuxNotifySendBackend();
        Assert.Equal("linux-notify-send", b.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_OnNonLinuxOrMissingTool_ReturnsFailureWithHint()
    {
        // We can't reliably test the success path in CI, but we CAN verify the
        // failure path produces a helpful error message containing install hints.
        // Run on any platform — if notify-send isn't on PATH, we get the helpful error.
        var b = new LinuxNotifySendBackend();
        var result = await b.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);

        if (!result.Ok)
        {
            Assert.Contains("notify-send", result.Error);
        }
        // If it did succeed (Linux runner with notify-send installed + display), we just
        // assert the name is right — which we already covered. Nothing more to assert.
        Assert.Equal("linux-notify-send", result.BackendName);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~LinuxNotifySendBackendTests`
Expected: compile error.

- [ ] **Step 3: Implement `LinuxNotifySendBackend.cs`**

```csharp
// src/Winix.Notify/Backends/LinuxNotifySendBackend.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a desktop notification on Linux by shelling out to <c>notify-send</c> (libnotify CLI).
/// notify-send is part of <c>libnotify-bin</c> on Debian/Ubuntu and <c>libnotify</c> on Fedora.
/// If the binary isn't on PATH, returns a failure with an install hint per common distro.
/// </summary>
public sealed class LinuxNotifySendBackend : IBackend
{
    public string Name => "linux-notify-send";

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "notify-send",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        // ArgumentList per project convention — never string Arguments (avoids quoting bugs).
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(UrgencyArg(message.Urgency));
        if (message.IconPath is not null)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(message.IconPath);
        }
        psi.ArgumentList.Add(message.Title);
        if (message.Body is not null)
        {
            psi.ArgumentList.Add(message.Body);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new BackendResult(Name, false,
                    "notify-send not found — install libnotify-bin (Debian/Ubuntu) or libnotify (Fedora)",
                    null);
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                return new BackendResult(Name, false,
                    $"notify-send exited {process.ExitCode}: {stderr.Trim()}", null);
            }
            return new BackendResult(Name, true, null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ENOENT / "file not found" surfaces here when Process.Start can't locate the binary.
            return new BackendResult(Name, false,
                "notify-send not found — install libnotify-bin (Debian/Ubuntu) or libnotify (Fedora)",
                null);
        }
    }

    private static string UrgencyArg(Urgency u) => u switch
    {
        Urgency.Low => "low",
        Urgency.Critical => "critical",
        _ => "normal",
    };
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~LinuxNotifySendBackendTests`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/Backends/LinuxNotifySendBackend.cs tests/Winix.Notify.Tests/LinuxNotifySendBackendTests.cs
git commit -m "feat(notify): add Linux notify-send backend with helpful install hint"
```

---

## Task 5: MacOsAppleScriptBackend

Shells out to `osascript`. Same pattern as Linux backend.

**Files:**
- Create: `src/Winix.Notify/Backends/MacOsAppleScriptBackend.cs`
- Create: `tests/Winix.Notify.Tests/MacOsAppleScriptBackendTests.cs`

- [ ] **Step 1: Write the (limited) tests**

```csharp
// tests/Winix.Notify.Tests/MacOsAppleScriptBackendTests.cs
#nullable enable
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class MacOsAppleScriptBackendTests
{
    [Fact]
    public void Backend_Name_IsMacosOsascript()
    {
        var b = new MacOsAppleScriptBackend();
        Assert.Equal("macos-osascript", b.Name);
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("with \"quotes\"", "with \\\"quotes\\\"")]
    [InlineData("with \\backslash", "with \\\\backslash")]
    public void EscapeForApplescript_ProducesCorrectQuoting(string input, string expected)
    {
        // Backslash MUST be doubled before the quote-escape, otherwise " -> \" introduces
        // an unescaped backslash that AppleScript would interpret as a string-escape escape.
        Assert.Equal(expected, MacOsAppleScriptBackend.EscapeForApplescript(input));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~MacOsAppleScriptBackendTests`
Expected: compile error.

- [ ] **Step 3: Implement `MacOsAppleScriptBackend.cs`**

```csharp
// src/Winix.Notify/Backends/MacOsAppleScriptBackend.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a macOS notification by shelling out to <c>osascript</c> with an inline AppleScript snippet:
/// <c>display notification "BODY" with title "TITLE" sound name "Submarine"</c>.
/// The proper notification APIs (UNUserNotificationCenter) require a signed app bundle which a loose CLI binary
/// can't provide — osascript is the only viable path. <c>--icon</c> is silently ignored on macOS for the same
/// bundle-requires-icon-asset reason.
/// </summary>
public sealed class MacOsAppleScriptBackend : IBackend
{
    public string Name => "macos-osascript";

    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        string script = BuildScript(message);

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new BackendResult(Name, false, "osascript not found (unexpected on macOS)", null);
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                return new BackendResult(Name, false,
                    $"osascript exited {process.ExitCode}: {stderr.Trim()}", null);
            }
            return new BackendResult(Name, true, null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new BackendResult(Name, false, "osascript not found (unexpected on macOS)", null);
        }
    }

    // Internal for testing — escape ordering matters: backslash first, then double-quote.
    internal static string EscapeForApplescript(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string BuildScript(NotifyMessage message)
    {
        var sb = new StringBuilder();
        sb.Append("display notification \"");
        sb.Append(EscapeForApplescript(message.Body ?? ""));
        sb.Append("\" with title \"");
        sb.Append(EscapeForApplescript(message.Title));
        sb.Append('"');
        // Critical urgency adds an alert sound; low/normal stay silent.
        if (message.Urgency == Urgency.Critical)
        {
            sb.Append(" sound name \"Submarine\"");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~MacOsAppleScriptBackendTests`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/Backends/MacOsAppleScriptBackend.cs tests/Winix.Notify.Tests/MacOsAppleScriptBackendTests.cs
git commit -m "feat(notify): add macOS osascript backend with AppleScript escape helper"
```

---

## Task 6: AumidShortcut (Windows-only helper)

Idempotent helper that creates a per-user Start Menu shortcut with an `AppUserModelID` property — required for WinRT toast notifications to display.

**Verification point before implementation:** confirm the `IShellLink` + `IPropertyStore` COM interfaces and `PropertyKey` GUID `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5` (the canonical `System.AppUserModel.ID` PKEY). Reference: Microsoft Docs "Application User Model IDs (AppUserModelIDs)" and "How to Pin and Unpin Programs from the Start Menu". The implementation below uses the documented COM contracts; if your tooling refuses to bind to `IShellLinkW`, fall back to the `WScript.Shell` COM ProgID which exposes the same operation through automation.

**Files:**
- Create: `src/Winix.Notify/AumidShortcut.cs`

(No tests — the only meaningful test is "did a toast appear?" which the WindowsToastBackend smoke covers in T11. Unit-testing COM creation in CI is gold-plating.)

- [ ] **Step 1: Implement `AumidShortcut.cs`**

```csharp
// src/Winix.Notify/AumidShortcut.cs
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Notify;

/// <summary>
/// Ensures a per-user Start Menu shortcut exists with the given AppUserModelID,
/// which Windows requires before <c>ToastNotificationManager.CreateToastNotifier(aumid)</c>
/// will display anything. Idempotent — skips file rewrite if the shortcut already exists
/// (file presence check; we don't introspect the AUMID property for speed).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AumidShortcut
{
    /// <summary>The reverse-domain AUMID for notify. Must match what WindowsToastBackend passes to CreateToastNotifier.</summary>
    public const string Aumid = "Yortw.Winix.Notify";

    /// <summary>The shortcut file shown in Start Menu Programs.</summary>
    public const string ShortcutName = "Winix Notify.lnk";

    /// <summary>Idempotently create the shortcut. Returns true if the shortcut existed or was created; false on failure.</summary>
    public static bool EnsureExists()
    {
        try
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string path = Path.Combine(startMenu, ShortcutName);
            if (File.Exists(path))
            {
                return true;
            }
            CreateShortcut(path);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateShortcut(string path)
    {
        string exePath = Environment.ProcessPath ?? "notify.exe";

        // Create COM instance for IShellLinkW (CLSID_ShellLink).
        Guid clsidShellLink = new("00021401-0000-0000-C000-000000000046");
        Guid iidShellLink = new("000214F9-0000-0000-C000-000000000046"); // IShellLinkW
        Guid iidPersistFile = new("0000010B-0000-0000-C000-000000000046"); // IPersistFile
        Guid iidPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

        Type? shellLinkType = Type.GetTypeFromCLSID(clsidShellLink);
        if (shellLinkType is null)
        {
            throw new InvalidOperationException("IShellLink CLSID not registered");
        }

        object shellLink = Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Failed to create IShellLink instance");

        try
        {
            var link = (IShellLinkW)shellLink;
            link.SetPath(exePath);
            link.SetArguments("");
            link.SetDescription("Winix Notify — desktop notifications and ntfy.sh push");

            // Set the AppUserModelID property on the shortcut. Property key:
            // System.AppUserModel.ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
            var propStore = (IPropertyStore)shellLink;
            var pkAumid = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            using (var pv = new PropVariantString(Aumid))
            {
                propStore.SetValue(ref pkAumid, pv.Variant);
                propStore.Commit();
            }

            var persist = (IPersistFile)shellLink;
            persist.Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(shellLink);
        }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
        public PropertyKey(Guid fmtid, int pid) { FormatId = fmtid; PropertyId = pid; }
    }

    // Minimal PropVariant for VT_LPWSTR strings only — covers the AUMID-set use case.
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    private const ushort VT_LPWSTR = 31;

    private sealed class PropVariantString : IDisposable
    {
        public PropVariant Variant;
        public PropVariantString(string s)
        {
            Variant.vt = VT_LPWSTR;
            Variant.pointerValue = Marshal.StringToCoTaskMemUni(s);
        }
        public void Dispose()
        {
            if (Variant.pointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(Variant.pointerValue);
                Variant.pointerValue = IntPtr.Zero;
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors. (The `[SupportedOSPlatform("windows")]` annotation prevents the analyzer warning when calling Windows-only APIs from a multi-platform-targeting project.)

- [ ] **Step 3: Manual smoke (Windows only)**

Add a one-shot test in `tests/Winix.Notify.Tests` named `AumidShortcutSmoke.cs` (or any throwaway scratchpad) that calls `AumidShortcut.EnsureExists()` once. Run on Windows. Verify the file appears at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Winix Notify.lnk`. Then **delete the smoke test file** before committing — this is verification, not regression coverage.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Notify/AumidShortcut.cs
git commit -m "feat(notify): add AumidShortcut helper for Windows toast AUMID registration"
```

---

## Task 7: WindowsToastBackend

Direct WinRT toast via COM. The trickiest backend; relies on T6 AumidShortcut.

**Verification point before implementation:** the canonical .NET 10 + AOT path to WinRT is `Microsoft.Windows.SDK.NET.Ref` (set `<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>` on a Windows-only project) OR direct COM activation via `Type.GetTypeFromCLSID` + interface marshalling. The toast XML payload is well-documented at "Toast content schema". Verify which projection works under AOT with no trim warnings before committing to it. If `Microsoft.Windows.SDK.NET.Ref` works cleanly, prefer that — less hand-rolled COM. If trim warnings appear, fall back to the direct-COM approach below (which is what this task documents as the conservative implementation).

**Files:**
- Create: `src/Winix.Notify/Backends/WindowsToastBackend.cs`

- [ ] **Step 1: Implement `WindowsToastBackend.cs` (direct COM approach)**

```csharp
// src/Winix.Notify/Backends/WindowsToastBackend.cs
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a Windows toast notification via direct WinRT COM activation. Requires the AUMID
/// from <see cref="AumidShortcut"/> to be registered (via a Start Menu shortcut) — handled
/// idempotently on every send.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsToastBackend : IBackend
{
    public string Name => "windows-toast";

    public Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        // Synchronous body — Windows.UI.Notifications.ToastNotifier.Show is fire-and-forget.
        // Wrap to satisfy the async interface.
        try
        {
            AumidShortcut.EnsureExists();
            string xml = BuildToastXml(message);
            ShowToastViaWinRT(AumidShortcut.Aumid, xml);
            return Task.FromResult(new BackendResult(Name, true, null, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BackendResult(Name, false,
                $"Windows toast: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    // Internal for testing the XML composition without hitting WinRT.
    internal static string BuildToastXml(NotifyMessage message)
    {
        // Toast XML schema reference: "ToastGeneric template" on Microsoft Docs.
        // Two text lines (title + body) + optional image + audio mapping.
        var sb = new StringBuilder();
        sb.Append("<toast");
        if (message.Urgency == Urgency.Critical)
        {
            // Win11 honours scenario; harmless on Win10.
            sb.Append(" scenario=\"urgent\"");
        }
        sb.Append("><visual><binding template=\"ToastGeneric\">");
        sb.Append("<text>").Append(EscapeXml(message.Title)).Append("</text>");
        if (message.Body is not null)
        {
            sb.Append("<text>").Append(EscapeXml(message.Body)).Append("</text>");
        }
        if (message.IconPath is not null)
        {
            sb.Append("<image placement=\"appLogoOverride\" src=\"")
                .Append(EscapeXml(message.IconPath)).Append("\"/>");
        }
        sb.Append("</binding></visual>");
        if (message.Urgency == Urgency.Low)
        {
            sb.Append("<audio silent=\"true\"/>");
        }
        sb.Append("</toast>");
        return sb.ToString();
    }

    private static string EscapeXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    // --- Direct COM activation of WinRT ToastNotificationManager ---
    //
    // We avoid the .NET WinRT projection (`Microsoft.Windows.SDK.NET.Ref`) so this
    // library can target plain net10.0 and stay AOT-friendly without TFM gymnastics.
    // The COM contracts below are stable from Windows 8 onwards.

    private static void ShowToastViaWinRT(string aumid, string xml)
    {
        // 1. Get the ToastNotificationManager statics (IToastNotificationManagerStatics).
        // 2. Create a ToastNotifier bound to our AUMID.
        // 3. Build a ToastNotification from a Windows.Data.Xml.Dom.XmlDocument.
        // 4. Call Show(notification).
        //
        // The ActivationFactory + interface IIDs below are documented in the
        // Windows SDK headers (windows.ui.notifications.h, windows.data.xml.dom.h).

        const string runtimeClassToastManager = "Windows.UI.Notifications.ToastNotificationManager";
        const string runtimeClassXmlDocument = "Windows.Data.Xml.Dom.XmlDocument";
        const string runtimeClassToastNotification = "Windows.UI.Notifications.ToastNotification";

        IntPtr managerStatics = RoGetActivationFactory(runtimeClassToastManager,
            new Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")); // IToastNotificationManagerStatics
        IntPtr xmlDocument = RoActivateInstance(runtimeClassXmlDocument);
        IntPtr xmlDocumentIO = QueryInterface(xmlDocument,
            new Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")); // IXmlDocumentIO

        try
        {
            IXmlDocumentIO xmlIo = (IXmlDocumentIO)Marshal.GetObjectForIUnknown(xmlDocumentIO);
            xmlIo.LoadXml(xml);

            IToastNotificationManagerStatics managerS = (IToastNotificationManagerStatics)
                Marshal.GetObjectForIUnknown(managerStatics);
            IToastNotifier notifier = managerS.CreateToastNotifierWithId(aumid);

            // Build ToastNotification factory.
            IntPtr toastFactory = RoGetActivationFactory(runtimeClassToastNotification,
                new Guid("04124B20-82C6-4229-B109-FD9ED4662B53")); // IToastNotificationFactory
            IToastNotificationFactory tnFactory = (IToastNotificationFactory)
                Marshal.GetObjectForIUnknown(toastFactory);
            object toast = tnFactory.CreateToastNotification(xmlIo);

            notifier.Show(toast);
        }
        finally
        {
            if (managerStatics != IntPtr.Zero) Marshal.Release(managerStatics);
            if (xmlDocument != IntPtr.Zero) Marshal.Release(xmlDocument);
            if (xmlDocumentIO != IntPtr.Zero) Marshal.Release(xmlDocumentIO);
        }
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
    private static extern IntPtr RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
    private static extern IntPtr RoActivateInstance(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId);

    private static IntPtr QueryInterface(IntPtr unk, Guid iid)
    {
        Marshal.QueryInterface(unk, ref iid, out IntPtr result);
        return result;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
    private interface IXmlDocumentIO
    {
        void LoadXml([MarshalAs(UnmanagedType.HString)] string xml);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
    private interface IToastNotificationManagerStatics
    {
        IToastNotifier CreateToastNotifier();
        IToastNotifier CreateToastNotifierWithId([MarshalAs(UnmanagedType.HString)] string aumid);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
    private interface IToastNotifier
    {
        void Show([MarshalAs(UnmanagedType.IUnknown)] object notification);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
    private interface IToastNotificationFactory
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object CreateToastNotification([MarshalAs(UnmanagedType.IUnknown)] object xml);
    }
}
```

**Implementer note:** if the COM signatures above don't bind cleanly (interface IIDs do drift between Windows SDK versions), the alternate path is `Microsoft.Windows.SDK.NET.Ref` with `<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>` on the *console app* csproj only (not the library). Then `using Windows.UI.Notifications;` works directly and the implementation collapses to ~30 lines. Trade-off: TFM split between library and exe. Acceptable if direct COM proves too fragile.

- [ ] **Step 2: Add a tiny XML-shape test**

```csharp
// tests/Winix.Notify.Tests/WindowsToastBackendTests.cs
#nullable enable
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class WindowsToastBackendTests
{
    [Fact]
    public void BuildToastXml_TitleOnly_HasOneTextLine()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hello", null, Urgency.Normal, null));
        Assert.Contains("<text>hello</text>", xml);
        Assert.DoesNotContain("scenario=", xml);
        Assert.DoesNotContain("<audio", xml);
    }

    [Fact]
    public void BuildToastXml_TitleAndBody_HasTwoTextLines()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", "world", Urgency.Normal, null));
        Assert.Contains("<text>hi</text>", xml);
        Assert.Contains("<text>world</text>", xml);
    }

    [Fact]
    public void BuildToastXml_Critical_HasUrgentScenario()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Critical, null));
        Assert.Contains("scenario=\"urgent\"", xml);
    }

    [Fact]
    public void BuildToastXml_Low_HasSilentAudio()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Low, null));
        Assert.Contains("<audio silent=\"true\"/>", xml);
    }

    [Fact]
    public void BuildToastXml_EscapesXmlSpecialCharacters()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("a&b<c>d", null, Urgency.Normal, null));
        Assert.Contains("a&amp;b&lt;c&gt;d", xml);
    }

    [Fact]
    public void BuildToastXml_IconPath_AppearsAsAppLogoOverride()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Normal, "C:\\icon.png"));
        Assert.Contains("placement=\"appLogoOverride\"", xml);
        Assert.Contains("src=\"C:\\icon.png\"", xml);
    }
}
```

- [ ] **Step 3: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~WindowsToastBackendTests`
Expected: 6 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Notify/Backends/WindowsToastBackend.cs tests/Winix.Notify.Tests/WindowsToastBackendTests.cs
git commit -m "feat(notify): add Windows toast backend via direct WinRT COM"
```

---

## Task 8: Dispatcher

Picks which backends to invoke and runs them in parallel. The orchestration heart of the tool.

**Files:**
- Create: `src/Winix.Notify/Dispatcher.cs`
- Create: `tests/Winix.Notify.Tests/DispatcherTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Notify.Tests/DispatcherTests.cs
#nullable enable
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Winix.Notify;
using Winix.Notify.Tests.Fakes;

namespace Winix.Notify.Tests;

public class DispatcherTests
{
    private static NotifyMessage Msg() => new("title", "body", Urgency.Normal, null);

    [Fact]
    public async Task Dispatch_OneBackend_Success_ReturnsOneOkResult()
    {
        var b = new FakeBackend("desktop");
        var results = await Dispatcher.SendAsync(new IBackend[] { b }, Msg(), CancellationToken.None);
        Assert.Single(results);
        Assert.True(results[0].Ok);
        Assert.Equal("desktop", results[0].BackendName);
        Assert.Single(b.Received);
    }

    [Fact]
    public async Task Dispatch_TwoBackends_BothSucceed_BothInResults()
    {
        var b1 = new FakeBackend("desktop");
        var b2 = new FakeBackend("ntfy");
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Ok));
    }

    [Fact]
    public async Task Dispatch_OneFails_BothInResults_FailureCarriesError()
    {
        var b1 = new FakeBackend("desktop") { ShouldSucceed = true };
        var b2 = new FakeBackend("ntfy") { ShouldSucceed = false, FailureMessage = "topic not found" };
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Ok);
        Assert.False(results[1].Ok);
        Assert.Equal("topic not found", results[1].Error);
    }

    [Fact]
    public async Task Dispatch_RunsBackendsInParallel_NotSequentially()
    {
        var b1 = new FakeBackend("a") { DelayMs = 100 };
        var b2 = new FakeBackend("b") { DelayMs = 100 };
        var sw = Stopwatch.StartNew();
        await Dispatcher.SendAsync(new IBackend[] { b1, b2 }, Msg(), CancellationToken.None);
        sw.Stop();
        // Sequential would be ~200ms; parallel ~100ms. Allow generous margin for CI noise.
        Assert.InRange(sw.ElapsedMilliseconds, 80, 180);
    }

    [Fact]
    public async Task Dispatch_NoBackends_ReturnsEmptyResults()
    {
        var results = await Dispatcher.SendAsync(System.Array.Empty<IBackend>(), Msg(), CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Dispatch_PreservesBackendOrderInResults()
    {
        var b1 = new FakeBackend("first");
        var b2 = new FakeBackend("second");
        var b3 = new FakeBackend("third");
        var results = await Dispatcher.SendAsync(new IBackend[] { b1, b2, b3 }, Msg(), CancellationToken.None);
        Assert.Equal("first", results[0].BackendName);
        Assert.Equal("second", results[1].BackendName);
        Assert.Equal("third", results[2].BackendName);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~DispatcherTests`
Expected: compile error.

- [ ] **Step 3: Implement `Dispatcher.cs`**

```csharp
// src/Winix.Notify/Dispatcher.cs
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Runs a list of backends in parallel and aggregates results in input order.
/// Each backend is responsible for converting its own exceptions into <see cref="BackendResult"/>;
/// the dispatcher does not catch — a backend that throws will fault the returned task.
/// </summary>
public static class Dispatcher
{
    /// <summary>Send the message to every backend in parallel; return results in the same order as input.</summary>
    public static async Task<IReadOnlyList<BackendResult>> SendAsync(
        IReadOnlyList<IBackend> backends,
        NotifyMessage message,
        CancellationToken ct)
    {
        if (backends.Count == 0)
        {
            return System.Array.Empty<BackendResult>();
        }

        var tasks = backends.Select(b => b.SendAsync(message, ct)).ToArray();
        BackendResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~DispatcherTests`
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/Dispatcher.cs tests/Winix.Notify.Tests/DispatcherTests.cs
git commit -m "feat(notify): add Dispatcher for parallel backend execution"
```

---

## Task 9: Formatting (JSON output)

Pure function: turn `NotifyOptions` + the dispatcher's results into the JSON output document.

**Files:**
- Create: `src/Winix.Notify/Formatting.cs`
- Create: `tests/Winix.Notify.Tests/FormattingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Notify.Tests/FormattingTests.cs
#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class FormattingTests
{
    [Fact]
    public void Json_TitleAndBody_AppearAtTopLevel()
    {
        var opts = NotifyOptions.ForTests("the title") with { Body = "the body" };
        var results = new List<BackendResult>
        {
            new("windows-toast", true, null, null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("the title", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("the body", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void Json_Urgency_LowerCaseString()
    {
        var opts = NotifyOptions.ForTests("t") with { Urgency = Urgency.Critical };
        var results = new List<BackendResult>();
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("critical", doc.RootElement.GetProperty("urgency").GetString());
    }

    [Fact]
    public void Json_BackendsArray_OrderPreservedFromInput()
    {
        var opts = NotifyOptions.ForTests("t");
        var results = new List<BackendResult>
        {
            new("first", true, null, null),
            new("second", true, null, null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var backends = doc.RootElement.GetProperty("backends").EnumerateArray().ToArray();
        Assert.Equal(2, backends.Length);
        Assert.Equal("first", backends[0].GetProperty("name").GetString());
        Assert.Equal("second", backends[1].GetProperty("name").GetString());
    }

    [Fact]
    public void Json_FailedBackend_IncludesError()
    {
        var opts = NotifyOptions.ForTests("t");
        var results = new List<BackendResult>
        {
            new("ntfy", false, "topic not found", null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var b = doc.RootElement.GetProperty("backends")[0];
        Assert.False(b.GetProperty("ok").GetBoolean());
        Assert.Equal("topic not found", b.GetProperty("error").GetString());
    }

    [Fact]
    public void Json_BackendDetail_AppearsInline()
    {
        var opts = NotifyOptions.ForTests("t");
        var detail = new Dictionary<string, string>
        {
            ["server"] = "https://ntfy.sh",
            ["topic"] = "alerts",
        };
        var results = new List<BackendResult>
        {
            new("ntfy", true, null, detail),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var b = doc.RootElement.GetProperty("backends")[0];
        Assert.Equal("https://ntfy.sh", b.GetProperty("server").GetString());
        Assert.Equal("alerts", b.GetProperty("topic").GetString());
    }

    [Fact]
    public void Json_NullBody_OmittedFromOutput()
    {
        var opts = NotifyOptions.ForTests("t");  // Body is null by default
        var results = new List<BackendResult>();
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("body", out _));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~FormattingTests`
Expected: compile error.

- [ ] **Step 3: Implement `Formatting.cs`**

```csharp
// src/Winix.Notify/Formatting.cs
#nullable enable
using System.Collections.Generic;
using Yort.ShellKit;

namespace Winix.Notify;

/// <summary>JSON output composition for <c>--json</c> mode. Pure — no I/O.</summary>
public static class Formatting
{
    /// <summary>Compose the JSON document describing what was sent and the per-backend status.</summary>
    public static string Json(NotifyOptions options, IReadOnlyList<BackendResult> results)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("title", options.Title);
            if (options.Body is not null)
            {
                writer.WriteString("body", options.Body);
            }
            writer.WriteString("urgency", options.Urgency switch
            {
                Urgency.Low => "low",
                Urgency.Critical => "critical",
                _ => "normal",
            });
            writer.WriteStartArray("backends");
            foreach (var r in results)
            {
                writer.WriteStartObject();
                writer.WriteString("name", r.BackendName);
                writer.WriteBoolean("ok", r.Ok);
                if (r.Error is not null)
                {
                    writer.WriteString("error", r.Error);
                }
                if (r.Detail is not null)
                {
                    foreach (var kv in r.Detail)
                    {
                        writer.WriteString(kv.Key, kv.Value);
                    }
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~FormattingTests`
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/Formatting.cs tests/Winix.Notify.Tests/FormattingTests.cs
git commit -m "feat(notify): add JSON formatting for --json output"
```

---

## Task 10: ArgParser

Parses argv into `NotifyOptions`. Q-matrix validation: at least one backend must be configured, ntfy needs a topic when enabled, etc.

**Files:**
- Create: `src/Winix.Notify/ArgParser.cs`
- Create: `tests/Winix.Notify.Tests/ArgParserTests.cs`

Before writing this: open `src/Winix.Digest/ArgParser.cs` for the working reference pattern from a recently-shipped tool. The shape (Result record + Parse method + private BuildParser) is the suite convention.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Winix.Notify.Tests/ArgParserTests.cs
#nullable enable
using System;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class ArgParserTests
{
    // --- Title positional ---

    [Fact]
    public void Parse_NoArgs_Errors()
    {
        var r = ArgParser.Parse(System.Array.Empty<string>());
        Assert.False(r.Success);
        Assert.Contains("TITLE is required", r.Error);
    }

    [Fact]
    public void Parse_TitleOnly_Succeeds()
    {
        var r = ArgParser.Parse(new[] { "hello" });
        Assert.True(r.Success);
        Assert.Equal("hello", r.Options!.Title);
        Assert.Null(r.Options.Body);
    }

    [Fact]
    public void Parse_TitleAndBody_BothPopulated()
    {
        var r = ArgParser.Parse(new[] { "hello", "world" });
        Assert.True(r.Success);
        Assert.Equal("hello", r.Options!.Title);
        Assert.Equal("world", r.Options.Body);
    }

    [Fact]
    public void Parse_TooManyPositionals_Errors()
    {
        var r = ArgParser.Parse(new[] { "a", "b", "c" });
        Assert.False(r.Success);
        Assert.Contains("at most TITLE and BODY", r.Error);
    }

    // --- Urgency ---

    [Theory]
    [InlineData("low", Urgency.Low)]
    [InlineData("normal", Urgency.Normal)]
    [InlineData("critical", Urgency.Critical)]
    public void Parse_UrgencyFlag_MapsCorrectly(string value, Urgency expected)
    {
        var r = ArgParser.Parse(new[] { "--urgency", value, "hi" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Urgency);
    }

    [Fact]
    public void Parse_UrgencyDefault_IsNormal()
    {
        var r = ArgParser.Parse(new[] { "hi" });
        Assert.Equal(Urgency.Normal, r.Options!.Urgency);
    }

    [Fact]
    public void Parse_UrgencyUnknown_Errors()
    {
        var r = ArgParser.Parse(new[] { "--urgency", "shouty", "hi" });
        Assert.False(r.Success);
        Assert.Contains("unknown --urgency", r.Error);
    }

    // --- Icon ---

    [Fact]
    public void Parse_IconFlag_PopulatesOption()
    {
        var r = ArgParser.Parse(new[] { "--icon", "/tmp/i.png", "hi" });
        Assert.True(r.Success);
        Assert.Equal("/tmp/i.png", r.Options!.IconPath);
    }

    // --- ntfy options ---

    [Fact]
    public void Parse_NtfyTopic_EnablesNtfy()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "hi" });
        Assert.True(r.Success);
        Assert.True(r.Options!.NtfyEnabled);
        Assert.Equal("alerts", r.Options.NtfyTopic);
    }

    [Fact]
    public void Parse_NtfyServerOverride_AppliedWhenNtfyEnabled()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "--ntfy-server", "https://ntfy.example.com", "hi" });
        Assert.True(r.Success);
        Assert.Equal("https://ntfy.example.com", r.Options!.NtfyServer);
    }

    [Fact]
    public void Parse_NtfyServer_DefaultIsNtfySh()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "hi" });
        Assert.Equal("https://ntfy.sh", r.Options!.NtfyServer);
    }

    [Fact]
    public void Parse_NtfyToken_PopulatesOption()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "--ntfy-token", "tk_abc", "hi" });
        Assert.True(r.Success);
        Assert.Equal("tk_abc", r.Options!.NtfyToken);
    }

    [Fact]
    public void Parse_NtfyEnvFallback_TopicComesFromEnv()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        try
        {
            var r = ArgParser.Parse(new[] { "hi" });
            Assert.True(r.Success);
            Assert.True(r.Options!.NtfyEnabled);
            Assert.Equal("envtopic", r.Options.NtfyTopic);
        }
        finally { Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null); }
    }

    [Fact]
    public void Parse_NtfyServerEnvFallback_AppliedWhenNoFlag()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_SERVER", "https://envserver.example.com");
        try
        {
            var r = ArgParser.Parse(new[] { "hi" });
            Assert.True(r.Success);
            Assert.Equal("https://envserver.example.com", r.Options!.NtfyServer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null);
            Environment.SetEnvironmentVariable("NOTIFY_NTFY_SERVER", null);
        }
    }

    // --- Backend selection ---

    [Fact]
    public void Parse_DefaultsTo_DesktopOnly()
    {
        var r = ArgParser.Parse(new[] { "hi" });
        Assert.True(r.Options!.DesktopEnabled);
        Assert.False(r.Options.NtfyEnabled);
    }

    [Fact]
    public void Parse_NoDesktop_DisablesDesktop()
    {
        var r = ArgParser.Parse(new[] { "--no-desktop", "--ntfy", "alerts", "hi" });
        Assert.True(r.Success);
        Assert.False(r.Options!.DesktopEnabled);
        Assert.True(r.Options.NtfyEnabled);
    }

    [Fact]
    public void Parse_NoNtfy_DisablesNtfy_EvenIfEnvSet()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        try
        {
            var r = ArgParser.Parse(new[] { "--no-ntfy", "hi" });
            Assert.True(r.Success);
            Assert.False(r.Options!.NtfyEnabled);
        }
        finally { Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null); }
    }

    [Fact]
    public void Parse_NoBackendsConfigured_Errors()
    {
        var r = ArgParser.Parse(new[] { "--no-desktop", "hi" });
        Assert.False(r.Success);
        Assert.Contains("no backends configured", r.Error);
    }

    // --- Strict + JSON ---

    [Fact]
    public void Parse_Strict_PopulatesFlag()
    {
        var r = ArgParser.Parse(new[] { "--strict", "hi" });
        Assert.True(r.Options!.Strict);
    }

    [Fact]
    public void Parse_Json_PopulatesFlag()
    {
        var r = ArgParser.Parse(new[] { "--json", "hi" });
        Assert.True(r.Options!.Json);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~ArgParserTests`
Expected: compile error.

- [ ] **Step 3: Implement `ArgParser.cs`**

```csharp
// src/Winix.Notify/ArgParser.cs
#nullable enable
using System;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Notify;

/// <summary>
/// Parses <c>notify</c> CLI arguments into <see cref="NotifyOptions"/>.
/// Pure — no I/O. ShellKit prints --help/--version/--describe automatically; signalled via <see cref="Result.IsHandled"/>.
/// </summary>
public static class ArgParser
{
    /// <summary>Parse outcome — Success when Options is populated; otherwise Error or IsHandled.</summary>
    public sealed record Result(
        NotifyOptions? Options,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor)
    {
        public bool Success => Options is not null;
    }

    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        var parsed = parser.Parse(argv);
        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(null, error, false, 0, useColor);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        // --- Title + body ---
        string[] positionals = parsed.Positionals;
        if (positionals.Length == 0)
        {
            return Fail("TITLE is required");
        }
        if (positionals.Length > 2)
        {
            return Fail("at most TITLE and BODY positionals are allowed");
        }
        string title = positionals[0];
        string? body = positionals.Length >= 2 ? positionals[1] : null;

        // --- Urgency ---
        Urgency urgency = Urgency.Normal;
        if (parsed.Has("--urgency"))
        {
            string raw = parsed.GetString("--urgency");
            switch (raw)
            {
                case "low": urgency = Urgency.Low; break;
                case "normal": urgency = Urgency.Normal; break;
                case "critical": urgency = Urgency.Critical; break;
                default: return Fail($"unknown --urgency '{raw}' (expected: low, normal, critical)");
            }
        }

        // --- Icon ---
        string? icon = parsed.Has("--icon") ? parsed.GetString("--icon") : null;

        // --- ntfy: flag wins, env is fallback ---
        string? ntfyTopic = parsed.Has("--ntfy") ? parsed.GetString("--ntfy") : Environment.GetEnvironmentVariable("NOTIFY_NTFY_TOPIC");
        string ntfyServer = parsed.Has("--ntfy-server")
            ? parsed.GetString("--ntfy-server")
            : (Environment.GetEnvironmentVariable("NOTIFY_NTFY_SERVER") ?? "https://ntfy.sh");
        string? ntfyToken = parsed.Has("--ntfy-token") ? parsed.GetString("--ntfy-token") : Environment.GetEnvironmentVariable("NOTIFY_NTFY_TOKEN");

        bool ntfyEnabled = !string.IsNullOrEmpty(ntfyTopic) && !parsed.Has("--no-ntfy");
        bool desktopEnabled = !parsed.Has("--no-desktop");

        if (!desktopEnabled && !ntfyEnabled)
        {
            return Fail("no backends configured (use --ntfy TOPIC or remove --no-desktop)");
        }

        bool strict = parsed.Has("--strict");
        bool json = parsed.Has("--json");

        var options = new NotifyOptions(
            Title: title,
            Body: body,
            Urgency: urgency,
            IconPath: icon,
            DesktopEnabled: desktopEnabled,
            NtfyEnabled: ntfyEnabled,
            NtfyTopic: ntfyEnabled ? ntfyTopic : null,
            NtfyServer: ntfyServer,
            NtfyToken: ntfyToken,
            Strict: strict,
            Json: json);

        return new Result(options, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("notify", ResolveVersion())
            .Description("Cross-platform desktop notifications + ntfy.sh push notifications.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "notify-send", "osascript", "BurntToast" },
                valueOnWindows: "Native gap-fill — Windows has no first-class notification CLI; users currently install BurntToast or third-party binaries.",
                valueOnUnix: "One consistent flag surface across notify-send (Linux) and osascript (macOS), plus optional ntfy.sh push to phone/web in the same call.")
            .ExitCodes(
                (0, "Success — at least one backend succeeded"),
                (1, "Strict mode — at least one configured backend failed"),
                (ExitCode.UsageError, "Usage error: bad flags, missing TITLE, no backends configured"),
                (ExitCode.NotExecutable, "All backends failed"))
            .StdinDescription("Not used")
            .StdoutDescription("Empty by default; JSON document with --json")
            .StderrDescription("Per-backend failure warnings (best-effort mode); also usage errors")
            .Example("notify \"build done\"", "Send a desktop notification")
            .Example("notify \"tests done\" \"5 of 200 failed\"", "Title and body")
            .Example("notify \"deploy done\" --urgency critical", "Critical urgency — sound + attention")
            .Example("notify \"alert\" --ntfy myalerts", "Send to desktop AND push to ntfy.sh/myalerts")
            .Example("NOTIFY_NTFY_TOPIC=alerts notify \"see you\"", "Env-set ntfy topic — applies to all calls in the shell")
            .Example("notify \"server warn\" --no-desktop --ntfy phone", "Push only — useful in headless CI")
            .ComposesWith("anything", "long-cmd; notify \"done\"", "Append to a long-running command")
            .ComposesWith("timeit", "timeit slow-script.sh && notify \"done\"", "Time + alert pattern")
            .JsonField("title", "string", "The notification title")
            .JsonField("body", "string", "Optional second line of text")
            .JsonField("urgency", "string", "low / normal / critical")
            .JsonField("backends", "array", "Per-backend status (name, ok, error?, detail fields)")
            .Option("--urgency", null, "LEVEL", "Urgency: low, normal, critical (default: normal)")
            .Option("--icon", null, "PATH", "Icon path (best-effort per backend; macOS ignores)")
            .Option("--ntfy", null, "TOPIC", "Send to ntfy.sh on TOPIC (env: NOTIFY_NTFY_TOPIC)")
            .Option("--ntfy-server", null, "URL", "Override ntfy server URL (env: NOTIFY_NTFY_SERVER, default https://ntfy.sh)")
            .Option("--ntfy-token", null, "TOKEN", "Bearer token for self-hosted ntfy (env: NOTIFY_NTFY_TOKEN)")
            .Flag("--no-desktop", "Suppress the desktop backend")
            .Flag("--no-ntfy", "Suppress ntfy even if env var is set")
            .Flag("--strict", "Exit non-zero if any configured backend fails (default: best-effort)");
    }

    private static string ResolveVersion()
    {
        string raw = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Notify.Tests/Winix.Notify.Tests.csproj --filter FullyQualifiedName~ArgParserTests`
Expected: 19 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Notify/ArgParser.cs tests/Winix.Notify.Tests/ArgParserTests.cs
git commit -m "feat(notify): add ArgParser with Q-matrix validation and env-var fallbacks"
```

---

## Task 11: Console app Program.cs

Wire it all together. Parse → build backend list → dispatch → format → exit.

**Files:**
- Modify: `src/notify/Program.cs` (replace stub)

- [ ] **Step 1: Replace `Program.cs` with the real implementation**

```csharp
// src/notify/Program.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Winix.Notify;
using Yort.ShellKit;

namespace Notify;

internal sealed class Program
{
    // Static HttpClient — recommended pattern; AOT-friendly; lifetime spans the (one-shot) process.
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    });

    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        var parse = ArgParser.Parse(args);
        if (parse.IsHandled)
        {
            return parse.HandledExitCode;
        }
        if (!parse.Success)
        {
            Console.Error.WriteLine($"notify: {parse.Error}");
            Console.Error.WriteLine("Run 'notify --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        try
        {
            var backends = BuildBackends(opts);
            if (backends.Count == 0)
            {
                // Defensive — ArgParser should already have caught this.
                Console.Error.WriteLine("notify: no backends configured");
                return ExitCode.UsageError;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IReadOnlyList<BackendResult> results = Dispatcher.SendAsync(backends, opts.ToMessage(), cts.Token)
                .GetAwaiter().GetResult();

            // Per-backend stderr warnings for failures (regardless of strict mode).
            foreach (var r in results)
            {
                if (!r.Ok)
                {
                    Console.Error.WriteLine($"notify: warning: {r.BackendName}: {r.Error}");
                }
            }

            if (opts.Json)
            {
                Console.Out.WriteLine(Formatting.Json(opts, results));
            }

            return ResolveExitCode(opts.Strict, results);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"notify: error: {ex.Message}");
            return 1;
        }
    }

    private static List<IBackend> BuildBackends(NotifyOptions opts)
    {
        var list = new List<IBackend>();
        if (opts.DesktopEnabled)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#pragma warning disable CA1416 // Validate platform compatibility — guarded by IsOSPlatform check above.
                list.Add(new WindowsToastBackend());
#pragma warning restore CA1416
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                list.Add(new MacOsAppleScriptBackend());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                list.Add(new LinuxNotifySendBackend());
            }
            // Other Unixes — no desktop backend, ntfy still available if configured.
        }
        if (opts.NtfyEnabled && opts.NtfyTopic is not null)
        {
            list.Add(new NtfyBackend(SharedHttp.Value, opts.NtfyServer, opts.NtfyTopic, opts.NtfyToken));
        }
        return list;
    }

    private static int ResolveExitCode(bool strict, IReadOnlyList<BackendResult> results)
    {
        bool anyOk = false;
        bool anyFail = false;
        foreach (var r in results)
        {
            if (r.Ok) anyOk = true;
            else anyFail = true;
        }
        if (strict && anyFail) return 1;
        if (!anyOk) return ExitCode.NotExecutable; // 126 — all backends failed
        return ExitCode.Success;
    }
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Winix.sln`
Expected: all tests pass (no regressions in other tools, all new notify tests green).

- [ ] **Step 4: Manual smoke tests**

```bash
dotnet run --project src/notify/notify.csproj -- "hello world"
# expected: a Windows toast appears with title "hello world" (Windows)
#           OR notify-send fires (Linux) OR osascript notification (macOS)
#           exit 0

dotnet run --project src/notify/notify.csproj -- "build done" "tests passed"
# expected: notification with two lines
#           exit 0

dotnet run --project src/notify/notify.csproj -- "low priority" --urgency low
# expected: silent notification

dotnet run --project src/notify/notify.csproj -- "critical" --urgency critical
# expected: attention-grabbing notification, sound on macOS/Win11

dotnet run --project src/notify/notify.csproj -- "json check" --json
# expected: {"title":"json check","urgency":"normal","backends":[{"name":"...","ok":true}]}

dotnet run --project src/notify/notify.csproj -- --no-desktop "no backends"
# expected: stderr "notify: no backends configured (use --ntfy TOPIC or remove --no-desktop)"
#           exit 125

dotnet run --project src/notify/notify.csproj -- --ntfy winix-test-2026 "ntfy hello"
# Manual: subscribe to ntfy.sh/winix-test-2026 in browser/app, run this, observe push.
# expected: desktop fires AND ntfy push arrives
#           exit 0

dotnet run --project src/notify/notify.csproj -- --describe
# expected: JSON metadata document for AI agents

dotnet run --project src/notify/notify.csproj -- --no-desktop --ntfy winix-test-2026 --strict "push only"
# expected: only ntfy fires (no desktop attempt); exit 0
```

**If any smoke test produces unexpected output, stop and investigate before committing.**

- [ ] **Step 5: Commit**

```bash
git add src/notify/Program.cs
git commit -m "feat(notify): implement console app Program.cs"
```

---

## Task 12: Docs

Match the patterns from `src/digest/` and `src/ids/`.

**Files:**
- Replace: `src/notify/README.md` (overwrite the Task 1 placeholder)
- Create: `src/notify/man/man1/notify.1`
- Modify: `src/notify/notify.csproj` (add the man-page `<Content Include>`)
- Create: `docs/ai/notify.md`
- Modify: `llms.txt` (add notify entry after `digest`)

- [ ] **Step 1: Read reference files first**

Open: `src/digest/README.md`, `src/digest/man/man1/digest.1`, `docs/ai/digest.md`, `src/digest/digest.csproj`, `llms.txt`.

- [ ] **Step 2: Write `src/notify/README.md`**

Match the structure of `src/digest/README.md`. Sections:
- H1 `# notify` with one-line description.
- Install — Scoop / Winget / .NET Tool / Direct Download.
- Usage with synopsis: `notify TITLE [BODY] [options]`.
- Examples — every flag combination shown in the design doc, plus the composability examples.
- Options table.
- Backend behaviour table (Windows / macOS / Linux per row, urgency / icon / sound columns) — captures platform differences honestly.
- ntfy.sh integration — dedicated section. Cover topic-as-password warning (public ntfy.sh topics are world-readable), self-hosting note, env-var conventions.
- Headless / SSH usage — recommend `--no-desktop --ntfy TOPIC` pattern, mention the D-Bus error users will see if they leave desktop on under SSH.
- Exit codes table.
- Differences from `notify-send` — short list (cross-platform, ntfy integration, --json, --describe).
- Related tools — `clip`, `digest`, `timeit`, `peep`, `retry`.
- See Also — `man notify`, `notify --describe`, link to Hanselman's Toasty.

- [ ] **Step 3: Write `src/notify/man/man1/notify.1`**

Groff man page modelled on `src/digest/man/man1/digest.1`. TH header: `.TH NOTIFY 1 "2026-04-19" "Winix" "User Commands"`. Sections: NAME, SYNOPSIS, DESCRIPTION, OPTIONS, EXIT STATUS, EXAMPLES, BACKEND BEHAVIOUR (per-platform notes — this is a notify-specific section), NTFY INTEGRATION, ENVIRONMENT, SEE ALSO.

- [ ] **Step 4: Update `src/notify/notify.csproj` to include the man page**

Add to the csproj, alongside the existing `<None Include="README.md" .../>`:

```xml
<ItemGroup>
  <Content Include="man\man1\notify.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\notify.1" />
</ItemGroup>
```

- [ ] **Step 5: Write `docs/ai/notify.md`**

AI agent guide, ~100-150 lines, modelled on `docs/ai/digest.md`. Cover: what it does, when to use / when not to use, basic invocation examples, ntfy.sh invocation examples (highlight env var pattern), JSON output shape, platform behaviour notes (Windows AUMID one-time setup, macOS no-icon, Linux notify-send dep), composability (long-cmd && notify, with timeit/peep/retry), `--describe` reference.

- [ ] **Step 6: Add `notify` to `llms.txt`**

Add after the `digest` entry:

```
- [notify](docs/ai/notify.md): Cross-platform desktop notifications + ntfy.sh push. Windows toast (no PowerShell), Linux notify-send, macOS osascript, and ntfy.sh in the same call. Pairs with timeit/peep/retry for "long task done" alerts.
```

- [ ] **Step 7: Verify build + publish output**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet publish src/notify/notify.csproj -c Release -r win-x64`
Verify: `src/notify/bin/Release/net10.0/win-x64/publish/share/man/man1/notify.1` exists.

- [ ] **Step 8: Commit**

```bash
git add src/notify/README.md src/notify/man src/notify/notify.csproj docs/ai/notify.md llms.txt
git commit -m "docs(notify): add README, man page, AI guide, llms.txt entry"
```

---

## Task 13: Pipeline — scoop manifest, release workflows, CLAUDE.md

Wire `notify` into the release infrastructure. Pure infrastructure, no library code.

**Files:**
- Create: `bucket/notify.json`
- Modify: `.github/workflows/release.yml` — 6 insertions
- Modify: `.github/workflows/post-publish.yml` — 2 insertions
- Modify: `CLAUDE.md` — 3 sections

- [ ] **Step 1: Read reference files**

Open: `bucket/digest.json` (template), `.github/workflows/release.yml` (find every `digest` reference), `.github/workflows/post-publish.yml` (same), `CLAUDE.md` (project layout, NuGet package IDs, scoop manifests).

- [ ] **Step 2: Create `bucket/notify.json`**

Model on `bucket/digest.json`. Set `version` to the v0.4.0 placeholder (release pipeline rewrites it).

```json
{
  "version": "0.4.0",
  "description": "Cross-platform desktop notifications + ntfy.sh push.",
  "homepage": "https://github.com/Yortw/winix",
  "license": "MIT",
  "architecture": {
    "64bit": {
      "url": "https://github.com/Yortw/winix/releases/download/v0.4.0/notify-win-x64.zip",
      "hash": "",
      "bin": "notify.exe"
    }
  },
  "checkver": "github",
  "autoupdate": {
    "architecture": {
      "64bit": {
        "url": "https://github.com/Yortw/winix/releases/download/v$version/notify-win-x64.zip"
      }
    }
  }
}
```

- [ ] **Step 3: Modify `.github/workflows/release.yml`**

Find every occurrence of `digest` (the most recent tool) and add a parallel `notify` entry in the same place. Six places to update:

1. `dotnet pack` step — `Pack notify` after `Pack digest`
2. `dotnet publish` per-RID step — `Publish notify` after `Publish digest`
3. Linux/macOS zip staging — `notify-${{ matrix.rid }}.zip` line after the `digest` zip line
4. Windows zip staging — `Compress-Archive` for `notify` after `digest`
5. Combined Winix zip — `Copy-Item notify.exe` after `digest.exe`
6. Tools map in the JSON metadata generation:
   ```
   notify:  { description: "Cross-platform desktop notifications + ntfy.sh push.", packages: { winget: "Winix.Notify",   scoop: "notify",   brew: "notify",   dotnet: "Winix.Notify"   } }
   ```

- [ ] **Step 4: Modify `.github/workflows/post-publish.yml`**

Two insertions:

1. `update_manifest bucket/notify.json aot/notify-win-x64.zip` after the `digest` line
2. `generate_manifests "notify" "Notify" "Cross-platform desktop notifications + ntfy.sh push."` after the `digest` line

- [ ] **Step 5: Modify `CLAUDE.md`**

Three sections:

1. Project layout — add:
   ```
   src/Winix.Notify/          — class library (backends, dispatcher, arg parser, formatting)
   src/notify/                — console app entry point
   tests/Winix.Notify.Tests/  — xUnit tests
   ```
   Insert near the `digest` entries.

2. NuGet package IDs list — add `Winix.Notify` at the end.

3. Scoop manifests list — add `notify.json` at the end.

- [ ] **Step 6: Verify build**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add bucket/notify.json .github/workflows/release.yml .github/workflows/post-publish.yml CLAUDE.md
git commit -m "ci(notify): integrate notify into release and post-publish pipelines"
```

---

## Self-Review

**1. Spec coverage.**

- Goal / positioning → Task 12 (README, AI guide).
- Project structure (§Architecture) → Task 1 (scaffolding), Task 2 (core types), Tasks 3–9 (library), Task 11 (orchestrator), Task 12 (docs), Task 13 (distribution).
- CLI interface (§CLI Interface) → Task 10 (ArgParser).
- Backends (§Backend Implementations) → Task 3 (Ntfy), Task 4 (Linux), Task 5 (macOS), Task 6 (AumidShortcut), Task 7 (Windows toast).
- Urgency table (§Urgency → backend mapping) → exercised in Task 3 (ntfy priority), Task 4 (notify-send -u), Task 5 (osascript sound name), Task 7 (Windows scenario / silent audio).
- Error handling (§Error Handling) → Task 10 (ArgParser fails 125 for no-backends), Task 11 (Program.cs best-effort + strict + per-backend stderr warnings).
- JSON output (§JSON Output) → Task 9 (Formatting) + Task 11 (Program.cs wires it).
- Testing (§Testing) → Tasks 3, 4, 5, 7, 8, 9, 10 each include the prescribed tests.
- Distribution (§Distribution) → Task 13.

All spec sections have a task.

**2. Placeholder scan.**

Two "verification point" notes remain — Task 6 (IShellLink/IPropertyStore COM contracts) and Task 7 (WinRT projection mechanism + interface IIDs). These are *verification points*, not TODOs — each names the exact API and documentation reference to cross-check.

No "implement later" / "TBD" / "add appropriate X" placeholders.

**3. Type consistency.**

- `NotifyMessage` (Task 2): 4 properties (`Title`, `Body`, `Urgency`, `IconPath`). Used consistently across Tasks 3, 4, 5, 7, 8, 9.
- `NotifyOptions` (Task 2): 11 properties + `ToMessage()` + `ForTests()`. Used in Tasks 9, 10, 11.
- `BackendResult` (Task 2): `(BackendName, Ok, Error, Detail)`. Used in Tasks 3, 4, 5, 7, 8, 9.
- `IBackend` (Task 2): `Name` + `SendAsync(NotifyMessage, CancellationToken) → Task<BackendResult>`. Implemented by all four backends; consumed by Dispatcher (T8) and Program.cs (T11).
- `Urgency` enum: 3 values (`Low`, `Normal`, `Critical`). Mapped to per-backend behaviour consistently per the design's table.
- `ArgParser.Result` record: 5 fields. Used in Task 10 test + Task 11 consumer.

No drift.

**4. Other observations:**

- The Windows toast XML escaping (Task 7) covers `&`, `<`, `>`, `"`. If user titles ever contain `'`, it doesn't matter for double-quoted XML attributes — but worth noting in case the schema ever changes.
- The static `HttpClient` in Program.cs is `Lazy<>` so it's only constructed if any backend that needs it is built (saves a few ms when running pure-desktop). Good for one-shot CLIs.
- Manual smoke tests in Task 11 include an actual ntfy.sh round-trip — implementer needs to subscribe in browser/phone app first to verify.
- Task 6 (AumidShortcut) creates one user-visible artifact (the .lnk in Start Menu). Mention in README so users aren't surprised.
