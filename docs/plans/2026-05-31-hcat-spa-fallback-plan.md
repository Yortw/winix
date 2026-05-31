# serve --spa fallback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in `serve --spa` mode where unmatched browser navigations return the app shell (`index.html`, 200) so client-side routers survive deep-link refresh, while asset/API misses keep their real 404.

**Architecture:** A pure `AcceptsHtml` helper (the correctness crux) decides "is this a browser navigation"; `ServeConfig` disables directory browsing in SPA mode and registers a terminal fallback middleware after `UseFileServer` that serves the configured index file for `GET`/`HEAD` navigations that matched no file. `ArgParser` adds serve-only `--spa`/`--spa-index` with usage-error validation.

**Tech Stack:** C# / .NET 10, ASP.NET Core (`UseFileServer`, terminal middleware on `HttpContext`), `PhysicalFileProvider`, xUnit.

Spec: `docs/plans/2026-05-31-hcat-spa-fallback-design.md`. ADR: `docs/plans/2026-05-31-hcat-spa-fallback-adr.md`.

---

### Task 1: `AcceptsHtml` pure helper + tests

The fallback's correctness lives here: explicit `text/html` only, so `*/*` (curl) and specific non-HTML types keep their 404.

**Files:**
- Modify: `src/Winix.HCat/Handlers/ServeConfig.cs` (add `internal static AcceptsHtml`)
- Test: `tests/Winix.HCat.Tests/AcceptsHtmlTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests/Winix.HCat.Tests/AcceptsHtmlTests.cs`:

```csharp
using Winix.HCat.Handlers;
using Xunit;

namespace Winix.HCat.Tests;

public class AcceptsHtmlTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]  // typical browser nav
    [InlineData("application/xml, text/html")]
    [InlineData("text/html; q=1.0")]
    [InlineData("  text/html  ")]                                                   // whitespace tolerated
    public void True_when_text_html_explicitly_listed(string accept)
    {
        Assert.True(ServeConfig.AcceptsHtml(accept));
    }

    [Theory]
    [InlineData("*/*")]                  // curl/wget default — NOT a navigation
    [InlineData("application/json")]
    [InlineData("image/png")]
    [InlineData("text/*")]               // type wildcard is not an explicit text/html
    [InlineData("text/htmlx")]           // must not substring-match
    [InlineData("")]
    [InlineData(null)]
    public void False_when_html_not_explicitly_listed(string? accept)
    {
        Assert.False(ServeConfig.AcceptsHtml(accept));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~AcceptsHtmlTests"`
Expected: FAIL — `ServeConfig.AcceptsHtml` does not exist.

- [ ] **Step 3: Add the helper**

In `src/Winix.HCat/Handlers/ServeConfig.cs`, add this method inside the `ServeConfig` class (e.g. just above `IsUnderPrefix`):

```csharp
    /// <summary>True when <paramref name="acceptHeader"/> explicitly lists a <c>text/html</c> media type — i.e.
    /// the request is a browser navigation. A bare <c>*/*</c> (curl/wget default) or a type wildcard
    /// (<c>text/*</c>) does NOT count, so API clients and asset fetches keep their real 404. Pure so this
    /// (the SPA-fallback correctness crux) is unit-tested without a live request.</summary>
    internal static bool AcceptsHtml(string? acceptHeader)
    {
        if (string.IsNullOrEmpty(acceptHeader))
        {
            return false;
        }
        // Accept is a comma-separated list of media ranges, each optionally with ;q= params. We only need to
        // know whether the exact token "text/html" appears as a listed media type.
        foreach (string part in acceptHeader.Split(','))
        {
            string media = part.Split(';')[0].Trim();
            if (media.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
```

(`StringComparison` is `System`, already imported in `ServeConfig.cs`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~AcceptsHtmlTests"`
Expected: PASS — 12 cases.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.HCat/Handlers/ServeConfig.cs tests/Winix.HCat.Tests/AcceptsHtmlTests.cs
git commit -m "feat(hcat): AcceptsHtml helper for SPA navigation detection + tests"
```

---

### Task 2: `--spa` / `--spa-index` parsing + options + validation

**Files:**
- Modify: `src/Winix.HCat/HCatOptions.cs` (add `Spa`, `SpaIndexFile`)
- Modify: `src/Winix.HCat/ArgParser.cs` (register flags, parse, validate, set serve options)
- Test: `tests/Winix.HCat.Tests/ArgParserTests.cs` (add cases)

- [ ] **Step 1: Write the failing tests**

Append to the `ArgParserTests` class in `tests/Winix.HCat.Tests/ArgParserTests.cs` (before the closing brace):

```csharp
    [Fact]
    public void Spa_flag_parses_for_serve_with_default_index()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "./dist", "--spa" });
        Assert.True(r.Success);
        Assert.True(r.Options!.Spa);
        Assert.Equal("index.html", r.Options.SpaIndexFile);
    }

    [Fact]
    public void Spa_index_overrides_the_fallback_file()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "./dist", "--spa", "--spa-index", "app.html" });
        Assert.True(r.Success);
        Assert.True(r.Options!.Spa);
        Assert.Equal("app.html", r.Options.SpaIndexFile);
    }

    [Fact]
    public void Spa_index_without_spa_is_usage_error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "--spa-index", "app.html" });
        Assert.False(r.Success);
        Assert.Contains("--spa", r.Error!);
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("pipe")]
    public void Spa_outside_serve_is_usage_error(string mode)
    {
        string[] argv = mode == "pipe"
            ? new[] { "pipe", "--spa", "--", "cat" }
            : new[] { "inspect", "--spa" };
        ArgParser.Result r = ArgParser.Parse(argv);
        Assert.False(r.Success);
        Assert.Contains("serve", r.Error!);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~ArgParserTests"`
Expected: FAIL — `HCatOptions.Spa` / `SpaIndexFile` do not exist.

- [ ] **Step 3a: Add the options fields**

In `src/Winix.HCat/HCatOptions.cs`, add after the `UploadDir` property (before `InspectStatus`):

```csharp
    /// <summary>Enable SPA fallback (serve mode): an unmatched browser navigation returns the index file.</summary>
    public bool Spa { get; init; }
    /// <summary>The SPA fallback filename (serve mode); default <c>index.html</c>.</summary>
    public string SpaIndexFile { get; init; } = "index.html";
```

- [ ] **Step 3b: Register the flags**

In `src/Winix.HCat/ArgParser.cs`, in `BuildParser()`, add after the `.Flag("--upload", ...)` line:

```csharp
            .Flag("--spa", "(serve) SPA fallback: unmatched browser navigations return the index file.")
            .Option("--spa-index", null, "FILE", "(serve, with --spa) SPA fallback filename (default index.html).")
```

- [ ] **Step 3c: Parse + validate**

In `src/Winix.HCat/ArgParser.cs`, in `Parse(...)`, immediately after the `--exit-on` block (just before the `--timeout` block), add:

```csharp
        // --spa / --spa-index are serve-only; --spa-index requires --spa (else it silently does nothing).
        bool spa = parsed.Has("--spa");
        string? spaIndex = parsed.Has("--spa-index") ? parsed.GetString("--spa-index") : null;
        if ((spa || spaIndex is not null) && mode != HCatMode.Serve)
        {
            return Fail("--spa/--spa-index are only valid for serve mode");
        }
        if (spaIndex is not null && !spa)
        {
            return Fail("--spa-index requires --spa");
        }
```

- [ ] **Step 3d: Set the serve options**

In `src/Winix.HCat/ArgParser.cs`, in the `case HCatMode.Serve:` block, replace the existing options assignment:

```csharp
                options = options with { Directory = directory, Upload = upload, UploadDir = uploadDir };
```

with:

```csharp
                options = options with
                {
                    Directory = directory,
                    Upload = upload,
                    UploadDir = uploadDir,
                    Spa = spa,
                    SpaIndexFile = spaIndex ?? "index.html",
                };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~ArgParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.HCat/HCatOptions.cs src/Winix.HCat/ArgParser.cs tests/Winix.HCat.Tests/ArgParserTests.cs
git commit -m "feat(hcat): parse serve --spa / --spa-index (serve-only, validated)"
```

---

### Task 3: SPA fallback middleware + directory-browsing-off + startup warning + integration tests

**Files:**
- Modify: `src/Winix.HCat/Handlers/ServeConfig.cs` (browsing off in SPA; terminal fallback middleware)
- Modify: `src/Winix.HCat/HCatServer.cs` (`RunAsync` startup warning when the index file is missing)
- Test: `tests/Winix.HCat.Tests/IntegrationTests.cs` (add SPA cases)

- [ ] **Step 1: Write the failing integration tests**

Append to the `IntegrationTests` class in `tests/Winix.HCat.Tests/IntegrationTests.cs` (before the closing brace). Helper + 7 tests:

```csharp
    // Builds a temp served dir with index.html ("INDEX"), app.html ("APP"), real.txt ("REAL"),
    // and an empty subdir "sub". Returns the dir path (caller deletes).
    private static string MakeSpaDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-spa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "INDEX");
        File.WriteAllText(Path.Combine(dir, "app.html"), "APP");
        File.WriteAllText(Path.Combine(dir, "real.txt"), "REAL");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        return dir;
    }

    private static async Task<(int Status, string Body)> GetWithAccept(string url, string accept)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd(accept);
        using HttpResponseMessage resp = await http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]   // deep-link navigation (Accept: text/html) for a non-file path -> 200 index shell.
    public async Task Spa_navigation_deeplink_returns_index()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/users/42", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // a missing asset fetched as */* keeps its real 404 (no HTML served as JS).
    public async Task Spa_missing_asset_with_wildcard_accept_returns_404()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, _) = await GetWithAccept($"{baseUrl}/missing.js", "*/*");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // an existing real file is served normally — fallback never overrides it.
    public async Task Spa_real_file_is_served_normally()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/real.txt", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("REAL", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // root still serves index.html via the default-document mapping.
    public async Task Spa_root_serves_index()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // directory browsing is OFF in SPA mode: an existing indexless dir navigation -> index shell, not a listing.
    public async Task Spa_existing_dir_without_index_returns_index_not_listing()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/sub/", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);   // the shell, NOT an auto directory listing
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // --spa-index selects a different shell file.
    public async Task Spa_custom_index_file_is_used()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Spa = true, SpaIndexFile = "app.html",
        });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/deep/link", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("APP", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // --upload exclusion wins over SPA fallback: a GET under the excluded upload prefix stays 404.
    public async Task Spa_with_upload_excluded_path_stays_404()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Spa = true, Upload = true,
        });
        try
        {
            // default upload dir is <dir>/uploads, excluded from serving; a navigation there must NOT get the shell.
            var (status, _) = await GetWithAccept($"{baseUrl}/uploads/whatever", "text/html");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~IntegrationTests.Spa"`
Expected: FAIL — SPA mode not implemented; navigations 404 instead of returning the shell.

- [ ] **Step 3a: Disable directory browsing in SPA mode**

In `src/Winix.HCat/Handlers/ServeConfig.cs`, in `Apply`, change the `FileServerOptions` construction:

```csharp
        var options = new FileServerOptions
        {
            FileProvider = provider,
            RequestPath = string.Empty,
            EnableDirectoryBrowsing = true,
        };
```

to:

```csharp
        var options = new FileServerOptions
        {
            FileProvider = provider,
            RequestPath = string.Empty,
            // SPA mode owns routing: no auto directory listing (it would both pre-empt the fallback and leak
            // the app's file tree). Unmatched navigations go to the shell instead (terminal middleware below).
            EnableDirectoryBrowsing = !o.Spa,
        };
```

- [ ] **Step 3b: Register the terminal SPA fallback middleware**

In `src/Winix.HCat/Handlers/ServeConfig.cs`, in `Apply`, immediately AFTER the `app.UseFileServer(options);` line, add:

```csharp
        if (o.Spa)
        {
            string indexFile = o.SpaIndexFile;
            // Terminal: reached ONLY when UseFileServer matched no real file (it short-circuits on a match, so
            // real files always win). Serve the shell for a browser navigation; everything else stays 404. The
            // upload-exclusion guard above runs first and short-circuits, so excluded upload paths never get here.
            app.Run(async context =>
            {
                IFileInfo file = provider.GetFileInfo(indexFile);
                bool isReadMethod = HttpMethods.IsGet(context.Request.Method)
                    || HttpMethods.IsHead(context.Request.Method);
                if (isReadMethod && AcceptsHtml(context.Request.Headers.Accept.ToString()) && file.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength = file.Length;
                    if (HttpMethods.IsGet(context.Request.Method))
                    {
                        await using Stream stream = file.CreateReadStream();
                        await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
                    }
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        }
```

(`IFileInfo` is `Microsoft.Extensions.FileProviders`, `Stream` is `System.IO`, `HttpMethods`/`StatusCodes` are `Microsoft.AspNetCore.Http` — all already imported in `ServeConfig.cs`.)

- [ ] **Step 3c: Startup warning when the index file is missing (diagnostic)**

In `src/Winix.HCat/HCatServer.cs`, in `RunAsync`, immediately after the existing banner write:

```csharp
        banner.Write(Banner.Render(bind, options, qr: RenderQr(bind)));
        banner.Flush();
```

add:

```csharp
        // Diagnostic: an SPA serve whose shell file is absent would 404 every navigation — a confusing silent
        // failure. Warn once at startup (non-fatal; per-request existence is still checked by the fallback).
        if (options.Spa)
        {
            string indexPath = System.IO.Path.Combine(
                System.IO.Path.GetFullPath(options.Directory), options.SpaIndexFile);
            if (!System.IO.File.Exists(indexPath))
            {
                banner.WriteLine(
                    $"hcat: warning: --spa fallback file '{options.SpaIndexFile}' not found in the served directory; navigations will 404 until it exists");
                banner.Flush();
            }
        }
```

(Diagnostic-only stderr output; not covered by a dedicated test — it is strictly weaker than the request path, whose 404-when-missing behaviour IS tested via the fallback. Noted rather than faked.)

- [ ] **Step 4: Run the SPA integration tests to verify they pass**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj --filter "FullyQualifiedName~IntegrationTests.Spa"`
Expected: PASS — 7 tests.

- [ ] **Step 5: Run the full HCat suite + native smoke (no regression)**

Run: `dotnet test tests/Winix.HCat.Tests/Winix.HCat.Tests.csproj`
Expected: PASS — all tests (90 prior + 12 AcceptsHtml + ~6 ArgParser + 7 SPA integration).

Then publish + native smoke:
```bash
dotnet publish src/hcat/hcat.csproj -c Release -r win-x64
```
Create a temp dir with an `index.html`, then:
```
src/hcat/bin/Release/net10.0/win-x64/publish/hcat.exe serve <that-dir> --port 18130 --spa --capture 1 --timeout 8s
# in another shell:
curl -s -H "Accept: text/html" http://127.0.0.1:18130/some/deep/route
```
Expected: the curl returns the `index.html` body (the server then self-exits on capture 1).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.HCat/Handlers/ServeConfig.cs src/Winix.HCat/HCatServer.cs tests/Winix.HCat.Tests/IntegrationTests.cs
git commit -m "feat(hcat): serve --spa fallback middleware (Accept-gated, dir-browsing off, upload-safe)"
```

---

### Task 4: Docs — README, man page, AI guide

**Files:**
- Modify: `src/hcat/README.md`
- Modify: `src/hcat/man/man1/hcat.1`
- Modify: `docs/ai/hcat.md`

- [ ] **Step 1: README — options table rows**

In `src/hcat/README.md`, in the Options table, add after the `--upload-dir` row:

```
| `--spa` | | (serve) SPA fallback: unmatched browser navigations (`Accept: text/html`) return the index file with `200`, so client-side routers survive deep-link refresh. Disables directory listing. Asset/API misses still `404`. |
| `--spa-index` | `FILE` | (serve, with `--spa`) Fallback filename (default `index.html`). |
```

- [ ] **Step 2: README — a Single-page apps subsection**

In `src/hcat/README.md`, immediately before the `### HTTPS` subsection, add:

```markdown
### Single-page apps

```bash
# Serve a built SPA so deep links survive a refresh
hcat serve ./dist --spa
hcat serve ./dist --spa --spa-index app.html   # non-standard entry file
```

With `--spa`, a request that matches no file is treated as client-side routing: a **browser navigation** (one whose `Accept` header includes `text/html`) gets `index.html` with `200`, so frameworks like React/Vue/Angular keep working when you refresh `/users/42`. Requests for missing assets or APIs (`*/*`, `application/json`, etc.) still return a real `404`, and existing files are always served as-is. Directory listing is disabled in `--spa` mode.
```

- [ ] **Step 3: man page — option entries**

In `src/hcat/man/man1/hcat.1`, after the `--upload-dir` `.TP` entry (the line ending `Pointing it at the served root makes uploads downloadable.`), add:

```
.TP
.B \-\-spa
(serve) SPA fallback: a request matching no file, made as a browser navigation
.RB ( Accept:
text/html), returns the index file with status 200 so client\-side routers survive deep\-link refresh.
Asset/API misses (\fB*/*\fR, application/json) still 404. Disables directory listing.
.TP
.B \-\-spa\-index " " \fIFILE\fR
(serve, with \fB\-\-spa\fR) SPA fallback filename (default index.html).
```

- [ ] **Step 4: AI guide — note + example**

In `docs/ai/hcat.md`, in the serve-mode section (after the `--upload`/serve description prose), add a bullet:

```
- `--spa` (serve) enables single-page-app fallback: a request matching no file that is a browser navigation (`Accept: text/html`) returns the index file (`index.html`, or `--spa-index <file>`) with `200`, so client-side routers survive deep-link refresh. Missing assets/APIs (`*/*`, `application/json`) still `404`; existing files are unaffected; directory listing is disabled. Example: `hcat serve ./dist --spa`.
```

- [ ] **Step 5: Verify man page renders (if groff available) and commit**

If `groff` is installed: `groff -man -ww -z src/hcat/man/man1/hcat.1` → no warnings. (If not installed, the release pipeline validates it; the added macros mirror existing entries.)

```bash
git add src/hcat/README.md src/hcat/man/man1/hcat.1 docs/ai/hcat.md
git commit -m "docs(hcat): document serve --spa / --spa-index"
```

---

## Self-Review

**Spec coverage:** flags + validation → Task 2; `AcceptsHtml` gating → Task 1; fallback middleware + browsing-off + GET/HEAD + upload-safe ordering → Task 3 (3a/3b); startup warning → Task 3 (3c); docs → Task 4. All "In" scope items covered. Every integration case from the spec's testing section maps to a Task 3 test.

**Placeholder scan:** none — every code step has complete code; every run step has a command + expected result. The startup warning is real code explicitly noted as diagnostic-only (not a fake/placeholder test).

**Type consistency:** `HCatOptions.Spa` (bool) / `SpaIndexFile` (string) defined in Task 2 Step 3a; read in Task 2 Step 3d and Task 3 Steps 3a–3c with the same names. `ServeConfig.AcceptsHtml(string?)` defined in Task 1 Step 3; called in Task 3 Step 3b with `context.Request.Headers.Accept.ToString()` (string). `provider` (the `PhysicalFileProvider` built earlier in `Apply`) is in scope for the Task 3b middleware. Consistent.
