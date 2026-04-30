# protect / unprotect Format Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Date:** 2026-04-29
**Branch:** `fix/protect-format-hardening` (off `release/v0.4.0`)
**Goal:** Close the four Critical findings (C1 cross-file AEAD substitution, C2 DPAPI ignores AAD, C3 platform-gated test pattern, C4 silent key overwrite) plus eight Important findings from the round-1 fresh-eyes review of `protect`/`unprotect`. The tool is still unshipped on `release/v0.4.0`, so the WPRT format can be modified in place without a version bump.

**Architecture:** Extend the 6-byte WPRT header to 22 bytes by appending a 16-byte random `FileId` generated per file. Bind `FileId` into AAD on AEAD backends and into the DPAPI plaintext envelope alongside `chunkIndex`. Centralize header serialization in `Header.cs` so all five current hand-built header literals route through one producer. Refuse-closed on wrong-size stored keys, dest overwrite, unknown invocation names. Migrate platform-gated tests to `[SkippableFact] + Skip.IfNot`. Add cross-platform smoke tests on WSL and macOS.

**Tech Stack:** .NET 10, AOT-compiled, xUnit + `Xunit.SkippableFact`. Existing libraries unchanged. Files affected:
- `src/Winix.Protect/{Header,AadContext,AeadBackend,DpapiBackend,ChunkWriter,ChunkReader,Cli,InPlaceExecutor,RoundTripVerifier,IProtectBackend,ArgParser,ProtectOptions}.cs`
- `tests/Winix.Protect.Tests/*.cs` (all 9 files)
- `src/protect/README.md`, `src/unprotect/README.md`, `src/protect/man/man1/protect.1`, `src/unprotect/man/man1/unprotect.1`

**Reference docs:**
- ADR: `docs/plans/2026-04-29-protect-format-hardening-adr.md`
- Original implementation plan: `docs/plans/2026-04-21-protect-plan.md`
- CLAUDE.md (root) — conventions including Skip.IfNot pattern, full braces, ArgumentList
- `src/Winix.SecretStore/` — backend store abstraction (unchanged)
- `src/Yort.ShellKit/CommandLineParser` — used by `ArgParser` (unchanged)

**TDD discipline:** every behavioural change goes test-first. Refactors that don't change behaviour (centralizing header serialization, etc.) verify by running the existing suite green.

**Commit per task.** Each task ends with a commit. Conform to the existing `fix(protect):` / `test(protect):` / `refactor(protect):` prefix convention seen in `git log`.

---

## Task 1: Centralize header serialization (refactor — no behaviour change)

Move the hand-built `[(byte)'W',(byte)'P',(byte)'R',(byte)'T',0x01,(byte)backend.Marker]` literal from five call sites into `Header.cs` as the single producer. Existing tests must remain green.

**Files:**
- Modify: `src/Winix.Protect/Header.cs`
- Modify: `src/Winix.Protect/Cli.cs:125,139,185`
- Modify: `src/Winix.Protect/InPlaceExecutor.cs:34,71`
- Modify: `src/Winix.Protect/RoundTripVerifier.cs:30`

- [ ] **Step 1: Add `Header.SerializeForAad(PlatformMarker)` returning the current 6-byte form**

In `src/Winix.Protect/Header.cs`, add after the existing `Write` method:

```csharp
/// <summary>
/// Build the canonical "header bytes" used as AAD input on the AEAD path. Wraps the literal byte
/// composition so callers cannot drift from the on-wire format.
/// </summary>
public static byte[] SerializeForAad(PlatformMarker marker)
{
    return [(byte)'W', (byte)'P', (byte)'R', (byte)'T', CurrentVersion, (byte)marker];
}
```

- [ ] **Step 2: Replace the five hand-built header literals with the helper**

`src/Winix.Protect/Cli.cs:125,139` (the two `header` arrays in `RunProtect`) and `Cli.cs:185` (in `RunUnprotect`):

Before:
```csharp
byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)backend.Marker];
```
After:
```csharp
byte[] header = Header.SerializeForAad(backend.Marker);
```

`src/Winix.Protect/Cli.cs:185` uses `hdr.Marker` instead — replace with `Header.SerializeForAad(hdr.Marker)`.

`src/Winix.Protect/InPlaceExecutor.cs:34` and `:71` — same substitution.

`src/Winix.Protect/RoundTripVerifier.cs:30` — same substitution using `hdr.Marker`.

- [ ] **Step 3: Run the test suite — must remain green**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`
Expected: `Passed: 51, Failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Protect/Header.cs src/Winix.Protect/Cli.cs src/Winix.Protect/InPlaceExecutor.cs src/Winix.Protect/RoundTripVerifier.cs
git commit -m "refactor(protect): centralize header byte serialization in Header.SerializeForAad"
```

---

## Task 2: Extend the WPRT header with a 16-byte FileId

Grow the header from 6 bytes to 22 bytes by appending a `FileId`. Update writer/reader/result type. No AAD wiring yet — that's Task 3. Test the round-trip directly.

**Files:**
- Modify: `src/Winix.Protect/Header.cs`
- Modify: `tests/Winix.Protect.Tests/HeaderTests.cs`

- [ ] **Step 1: Update `HeaderTests.cs:17` to expect a 22-byte header**

Replace the assertion in `Write_EmitsMagicVersionAndMarker` with the FileId-aware version, and add a new test asserting `Read` returns the same FileId that was written:

```csharp
[Fact]
public void Write_EmitsMagicVersionMarkerAndFileId()
{
    using MemoryStream stream = new();
    byte[] fileId = new byte[16];
    for (int i = 0; i < 16; i++) fileId[i] = (byte)i;
    Header.Write(stream, PlatformMarker.WindowsDpapiUser, fileId);
    byte[] bytes = stream.ToArray();
    Assert.Equal(22, bytes.Length);
    Assert.Equal((byte)'W', bytes[0]);
    Assert.Equal((byte)'P', bytes[1]);
    Assert.Equal((byte)'R', bytes[2]);
    Assert.Equal((byte)'T', bytes[3]);
    Assert.Equal((byte)0x01, bytes[4]);
    Assert.Equal((byte)PlatformMarker.WindowsDpapiUser, bytes[5]);
    for (int i = 0; i < 16; i++) Assert.Equal((byte)i, bytes[6 + i]);
}

[Fact]
public void RoundTrip_PreservesFileId()
{
    byte[] fileId = new byte[16];
    System.Security.Cryptography.RandomNumberGenerator.Fill(fileId);
    using MemoryStream stream = new();
    Header.Write(stream, PlatformMarker.MacKeychainUser, fileId);
    stream.Position = 0;
    Header.ReadResult result = Header.Read(stream);
    Assert.Equal(fileId, result.FileId);
}
```

Also update existing `RoundTrip_AllMarkers` to pass a FileId and assert on it, and update `Read_TruncatedHeader_Throws`'s short-stream length to still be < new length (`new byte[] { (byte)'W', (byte)'P' }` is fine).

- [ ] **Step 2: Run the failing tests to verify they fail**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~HeaderTests" --nologo`
Expected: FAIL — `Header.Write(Stream, PlatformMarker, byte[])` overload doesn't exist; `ReadResult.FileId` doesn't exist.

- [ ] **Step 3: Update `Header.cs` to add the FileId field and overloads**

Replace the contents of `src/Winix.Protect/Header.cs` with:

```csharp
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>Reads and writes the 22-byte <c>protect</c> file header: magic | version | platform marker | file-id.</summary>
public static class Header
{
    private static readonly byte[] Magic = [(byte)'W', (byte)'P', (byte)'R', (byte)'T'];
    private const byte CurrentVersion = 0x01;

    /// <summary>Length of the FileId in bytes.</summary>
    public const int FileIdLength = 16;

    /// <summary>Byte offset of the FileId within the serialized header.</summary>
    public const int FileIdOffset = 4 + 1 + 1; // magic(4) + version(1) + marker(1)

    /// <summary>The full header length in bytes.</summary>
    public const int Length = FileIdOffset + FileIdLength;

    /// <summary>Copy the FileId out of a serialized header. Caller must pass exactly <see cref="Length"/> bytes.</summary>
    public static byte[] ExtractFileId(byte[] headerBytes)
    {
        if (headerBytes is null) throw new ArgumentNullException(nameof(headerBytes));
        if (headerBytes.Length != Length)
        {
            throw new ArgumentException($"headerBytes must be {Length} bytes (got {headerBytes.Length}).", nameof(headerBytes));
        }
        byte[] fileId = new byte[FileIdLength];
        Array.Copy(headerBytes, FileIdOffset, fileId, 0, FileIdLength);
        return fileId;
    }

    /// <summary>Output of <see cref="Read"/>. <see cref="FileId"/> is the 16-byte per-file binding token.</summary>
    public readonly record struct ReadResult(byte Version, PlatformMarker Marker, byte[] FileId);

    /// <summary>Generate a fresh random <see cref="FileIdLength"/>-byte file id.</summary>
    public static byte[] NewFileId()
    {
        byte[] id = new byte[FileIdLength];
        RandomNumberGenerator.Fill(id);
        return id;
    }

    /// <summary>Write a v1 header with the given platform marker and FileId. <paramref name="fileId"/> must be exactly <see cref="FileIdLength"/> bytes.</summary>
    public static void Write(Stream stream, PlatformMarker marker, byte[] fileId)
    {
        if (fileId is null) throw new ArgumentNullException(nameof(fileId));
        if (fileId.Length != FileIdLength)
        {
            throw new ArgumentException($"FileId must be {FileIdLength} bytes (got {fileId.Length}).", nameof(fileId));
        }
        stream.Write(Magic, 0, Magic.Length);
        stream.WriteByte(CurrentVersion);
        stream.WriteByte((byte)marker);
        stream.Write(fileId, 0, FileIdLength);
    }

    /// <summary>Read and validate the header. Returns the parsed marker and FileId.</summary>
    /// <exception cref="FormatException">Magic, version, or marker is invalid.</exception>
    /// <exception cref="EndOfStreamException">Stream is shorter than <see cref="Length"/> bytes.</exception>
    public static ReadResult Read(Stream stream)
    {
        byte[] buffer = new byte[Length];
        int read = 0;
        while (read < Length)
        {
            int n = stream.Read(buffer, read, Length - read);
            if (n == 0)
            {
                throw new EndOfStreamException($"Expected {Length} header bytes; got {read}.");
            }
            read += n;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (buffer[i] != Magic[i])
            {
                throw new FormatException("Bad magic — not a protect file.");
            }
        }

        byte version = buffer[4];
        if (version != CurrentVersion)
        {
            throw new FormatException($"Unsupported version: 0x{version:X2}. This build understands version 0x{CurrentVersion:X2}.");
        }

        byte markerByte = buffer[5];
        if (!IsKnownMarker(markerByte))
        {
            throw new FormatException($"Unknown platform marker: 0x{markerByte:X2}.");
        }

        byte[] fileId = new byte[FileIdLength];
        Array.Copy(buffer, FileIdOffset, fileId, 0, FileIdLength);
        return new ReadResult(version, (PlatformMarker)markerByte, fileId);
    }

    /// <summary>Build the canonical "header bytes" used as AAD input on the AEAD path.</summary>
    public static byte[] SerializeForAad(PlatformMarker marker, byte[] fileId)
    {
        if (fileId is null) throw new ArgumentNullException(nameof(fileId));
        if (fileId.Length != FileIdLength)
        {
            throw new ArgumentException($"FileId must be {FileIdLength} bytes (got {fileId.Length}).", nameof(fileId));
        }
        byte[] result = new byte[Length];
        result[0] = (byte)'W';
        result[1] = (byte)'P';
        result[2] = (byte)'R';
        result[3] = (byte)'T';
        result[4] = CurrentVersion;
        result[5] = (byte)marker;
        Array.Copy(fileId, 0, result, FileIdOffset, FileIdLength);
        return result;
    }

    private static bool IsKnownMarker(byte b)
    {
        return b == (byte)PlatformMarker.WindowsDpapiUser
            || b == (byte)PlatformMarker.WindowsDpapiMachine
            || b == (byte)PlatformMarker.MacKeychainUser
            || b == (byte)PlatformMarker.MacKeychainMachine
            || b == (byte)PlatformMarker.LinuxLibsecretUser;
    }
}
```

The old single-arg `SerializeForAad(PlatformMarker)` from Task 1 is **replaced** by the two-arg version. Callers in `Cli.cs`, `InPlaceExecutor.cs`, `RoundTripVerifier.cs` will be updated in Task 3 to thread FileId through.

- [ ] **Step 4: Run HeaderTests — verify they pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~HeaderTests" --nologo`
Expected: PASS for HeaderTests. Other tests will currently fail — that's expected; they're fixed in subsequent tasks.

- [ ] **Step 5: Update existing call-sites in `Cli.cs`, `InPlaceExecutor.cs`, `RoundTripVerifier.cs` to pass a FileId**

The single-arg `Header.SerializeForAad(PlatformMarker)` from Task 1 no longer compiles (signature changed). Update each of the five call sites to pass a freshly-generated FileId:

```csharp
// In Cli.RunProtect (twice — outputPath branch and stdout branch):
byte[] fileId = Header.NewFileId();
byte[] header = Header.SerializeForAad(backend.Marker, fileId);

// In Cli.RunUnprotect:
Header.ReadResult hdr = Header.Read(input);
IProtectBackend backend = BackendFactory.CreateForMarker(hdr.Marker);
byte[] headerBytes = Header.SerializeForAad(hdr.Marker, hdr.FileId);

// In InPlaceExecutor.ExecuteEncrypt:
byte[] fileId = Header.NewFileId();
byte[] header = Header.SerializeForAad(backend.Marker, fileId);

// In InPlaceExecutor.ExecuteDecrypt:
Header.ReadResult hdr = Header.Read(source);
byte[] headerBytes = Header.SerializeForAad(hdr.Marker, hdr.FileId);

// In RoundTripVerifier.Verify:
byte[] headerBytes = Header.SerializeForAad(hdr.Marker, hdr.FileId);
```

Add length validation at the top of `ChunkWriter.Write` and `ChunkReader.Read`:

```csharp
if (headerBytes is null) throw new ArgumentNullException(nameof(headerBytes));
if (headerBytes.Length != Header.Length)
{
    throw new ArgumentException($"headerBytes must be {Header.Length} bytes (got {headerBytes.Length}).", nameof(headerBytes));
}
```

- [ ] **Step 6: Update existing test header literals to use SerializeForAad with a zero FileId**

`tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs`, `tests/Winix.Protect.Tests/RoundTripVerifierTests.cs`, `tests/Winix.Protect.Tests/AeadBackendTests.cs`: replace every `byte[] header = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, (byte)PlatformMarker.X];` literal with:

```csharp
byte[] header = Header.SerializeForAad(PlatformMarker.X, new byte[16]);
```

Wherever these tests do `new MemoryStream(encrypted, 6, encrypted.Length - 6)` to skip the header, change `6` to `Header.Length` (= 22). The literal `6` appears at six call sites in `ChunkWriterReaderTests.cs` and at lines 21, 33, 46, 57, 58, 71 of `AeadBackendTests.cs` (within `AadContext` constructions — replace with `Header.SerializeForAad(...)` calls).

Existing `HeaderTests.cs:Read_TruncatedHeader_Throws` passes a 2-byte stream — still strictly less than 22 bytes, still throws `EndOfStreamException`. No change needed.

- [ ] **Step 7: Run the test suite — must be green**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`
Expected: All tests pass on Windows. Note: substitution attack still succeeds at this point because the AEAD AAD now includes FileId via `headerBytes`, but **DPAPI** still ignores AAD entirely (Task 4 fixes that). C1 (AEAD substitution) is *closed* by the end of this commit because the AEAD AAD now carries the per-file FileId; C2 (DPAPI) remains.

- [ ] **Step 8: Commit**

```bash
git add src/Winix.Protect/Header.cs src/Winix.Protect/Cli.cs src/Winix.Protect/InPlaceExecutor.cs src/Winix.Protect/RoundTripVerifier.cs src/Winix.Protect/ChunkWriter.cs src/Winix.Protect/ChunkReader.cs tests/Winix.Protect.Tests/HeaderTests.cs tests/Winix.Protect.Tests/AeadBackendTests.cs tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs tests/Winix.Protect.Tests/RoundTripVerifierTests.cs
git commit -m "fix(protect): expand WPRT header with random FileId; AEAD AAD now binds per-file (closes C1)"
```

---

## Task 3: Add cross-file substitution + small-input regression tests (proves C1 + closes F4)

Task 2 already binds the FileId into the AEAD AAD via `headerBytes`. This task adds the explicit attack-path tests that pin the contract, plus the empty/one-byte source regressions surfaced by adversarial-review F4.

**Files:**
- Create: `tests/Winix.Protect.Tests/CrossFileSubstitutionTests.cs`
- Modify: `tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs`

- [ ] **Step 1: Add a backend-level AEAD substitution test**

Append to `tests/Winix.Protect.Tests/AeadBackendTests.cs`:

```csharp
[Fact]
public void Decrypt_ChunkFromDifferentFileId_Throws()
{
    TestAeadBackend backend = new(new NullSecretStore());

    byte[] fileIdA = Header.NewFileId();
    byte[] fileIdB = Header.NewFileId();

    AadContext aadA = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, fileIdA), 0, true);
    AadContext aadB = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, fileIdB), 0, true);

    byte[] chunkFromB = backend.EncryptChunk([1, 2, 3], aadB, isFinal: true);

    Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
        () => backend.DecryptChunk(chunkFromB, aadA));
}
```

- [ ] **Step 2: Add cross-file substitution test at ChunkWriter/ChunkReader level**

Create `tests/Winix.Protect.Tests/CrossFileSubstitutionTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class CrossFileSubstitutionTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    [Fact]
    public void Aead_ChunkSplicedFromAnotherFile_FailsTagVerification()
    {
        NullSecretStore shared = new();
        TestAeadBackend backendA = new(shared);
        TestAeadBackend backendB = new(shared);

        byte[] inputA = System.Text.Encoding.UTF8.GetBytes("FILE A CONTENT — chunk-zero only");
        byte[] inputB = System.Text.Encoding.UTF8.GetBytes("FILE B CONTENT — same length here");

        byte[] hdrA = Header.SerializeForAad(backendA.Marker, Header.NewFileId());
        byte[] hdrB = Header.SerializeForAad(backendB.Marker, Header.NewFileId());

        using MemoryStream cipherA = new();
        using MemoryStream cipherB = new();
        ChunkWriter.Write(new MemoryStream(inputA), cipherA, backendA, hdrA);
        ChunkWriter.Write(new MemoryStream(inputB), cipherB, backendB, hdrB);

        byte[] cA = cipherA.ToArray();
        byte[] cB = cipherB.ToArray();

        // Splice B's encrypted chunk over A's at the same offset (after the 22-byte header).
        byte[] spliced = new byte[cA.Length];
        Array.Copy(cA, spliced, cA.Length);
        Array.Copy(cB, Header.Length, spliced, Header.Length, cB.Length - Header.Length);

        using MemoryStream readStream = new(spliced, Header.Length, spliced.Length - Header.Length);
        using MemoryStream sink = new();
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => ChunkReader.Read(readStream, sink, backendA, hdrA));
    }
}
```

- [ ] **Step 3: Verify the existing AEAD empty/one-byte round-trip tests still pass after Task 2**

`ChunkWriterReaderTests.cs` already has `RoundTrip_EmptyPayload_Works` and `RoundTrip_SingleByte_Works` for the AEAD path. Run:

```
dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~ChunkWriterReaderTests" --nologo
```

Expected: PASS for AEAD round-trip and the existing DPAPI multi-chunk regression test (Windows-only).

- [ ] **Step 4: Add explicit empty/one-byte regression tests for the **DPAPI** path (currently absent)**

Append to `ChunkWriterReaderTests.cs` (or an equivalent DPAPI-specific test file). Use the existing `if (!OnWindows) return;` pattern — the SkippableFact migration is Task 6.

```csharp
[Fact]
public void Dpapi_RoundTrip_OneByte_Works()
{
    if (!OnWindows) return;
#pragma warning disable CA1416
    DpapiBackend backend = new(Scope.User);
#pragma warning restore CA1416
    byte[] header = Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, Header.NewFileId());

    byte[] input = [0x42];

    using MemoryStream cipherStream = new();
    using MemoryStream sourceStream = new(input);
    ChunkWriter.Write(sourceStream, cipherStream, backend, header);

    byte[] encrypted = cipherStream.ToArray();
    using MemoryStream readStream = new(encrypted, Header.Length, encrypted.Length - Header.Length);
    using MemoryStream outStream = new();
    ChunkReader.Read(readStream, outStream, backend, header);

    Assert.Equal(input, outStream.ToArray());
}
```

(`Dpapi_RoundTrip_EmptyPayload_Works` already exists; verify it covers the empty case after the format change.)

- [ ] **Step 5: Run all new tests**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~CrossFileSubstitutionTests|FullyQualifiedName~Dpapi_RoundTrip_OneByte_Works|FullyQualifiedName~Decrypt_ChunkFromDifferentFileId_Throws" --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/Winix.Protect.Tests/AeadBackendTests.cs tests/Winix.Protect.Tests/CrossFileSubstitutionTests.cs tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs
git commit -m "test(protect): cross-file substitution + small-input regressions (pins C1, closes F4 partial)"
```

---

## Task 4: Bind FileId + chunkIndex into the DPAPI plaintext envelope (closes C2)

DPAPI doesn't accept AAD, so the binding goes inside the framed plaintext that's passed to `ProtectedData.Protect`. New envelope: `[isFinal(1) | FileId(16) | chunkIndex(8) | plaintext]`. On decrypt, verify FileId matches `aad.HeaderBytes[6..22]` and chunkIndex matches `aad.ChunkIndex`. Mismatch → throw `CryptographicException`.

**Files:**
- Modify: `src/Winix.Protect/DpapiBackend.cs`
- Modify: `tests/Winix.Protect.Tests/DpapiBackendTests.cs`

- [ ] **Step 1: Write failing tests for DPAPI intra-file reorder + cross-file substitution**

Append to `tests/Winix.Protect.Tests/DpapiBackendTests.cs`:

```csharp
[Fact]
public void IntraFileChunkReorder_ThrowsOnDecrypt()
{
    if (!OnWindows) return;
    DpapiBackend backend = new(Scope.User);

    byte[] fileId = Header.NewFileId();
    byte[] header = Header.SerializeForAad(backend.Marker, fileId);

    AadContext aad0 = new(header, 0, false);
    AadContext aad1 = new(header, 1, true);

    byte[] chunk0 = backend.EncryptChunk([0xAA, 0xBB], aad0, isFinal: false);
    byte[] chunk1 = backend.EncryptChunk([0xCC, 0xDD], aad1, isFinal: true);

    // Try to decrypt chunk1 in the position of chunk0 (chunkIndex=0, isFinal=false).
    Assert.Throws<System.Security.Cryptography.CryptographicException>(
        () => backend.DecryptChunk(chunk1, aad0));
}

[Fact]
public void CrossFileChunkSubstitution_ThrowsOnDecrypt()
{
    if (!OnWindows) return;
    DpapiBackend backend = new(Scope.User);

    byte[] fileIdA = Header.NewFileId();
    byte[] fileIdB = Header.NewFileId();
    byte[] hdrA = Header.SerializeForAad(backend.Marker, fileIdA);
    byte[] hdrB = Header.SerializeForAad(backend.Marker, fileIdB);

    AadContext aadA = new(hdrA, 0, true);
    AadContext aadB = new(hdrB, 0, true);

    byte[] chunkFromB = backend.EncryptChunk([0xAA, 0xBB], aadB, isFinal: true);

    Assert.Throws<System.Security.Cryptography.CryptographicException>(
        () => backend.DecryptChunk(chunkFromB, aadA));
}
```

(Both tests use the legacy `if (!OnWindows) return;` pattern — that's the *current* convention in the file; it's migrated to SkippableFact en bloc in Task 6.)

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~DpapiBackendTests.IntraFileChunkReorder_ThrowsOnDecrypt" --nologo`
Expected: FAIL — current DPAPI ignores AAD entirely so the chunks decrypt successfully.

- [ ] **Step 3: Update `DpapiBackend.cs` to embed and verify FileId + chunkIndex**

Replace the `EncryptChunk`/`DecryptChunk` bodies:

```csharp
public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
{
    if (aad.HeaderBytes is null || aad.HeaderBytes.Length != Header.Length)
    {
        throw new ArgumentException($"AadContext.HeaderBytes must be {Header.Length} bytes.", nameof(aad));
    }

    // Envelope: [isFinal(1) | FileId(16) | chunkIndex_be(8) | plaintext]
    byte[] framed = new byte[1 + Header.FileIdLength + 8 + plaintext.Length];
    framed[0] = isFinal ? (byte)1 : (byte)0;
    Array.Copy(aad.HeaderBytes, Header.FileIdOffset, framed, 1, Header.FileIdLength);
    long idx = aad.ChunkIndex;
    framed[17] = (byte)(idx >> 56);
    framed[18] = (byte)(idx >> 48);
    framed[19] = (byte)(idx >> 40);
    framed[20] = (byte)(idx >> 32);
    framed[21] = (byte)(idx >> 24);
    framed[22] = (byte)(idx >> 16);
    framed[23] = (byte)(idx >> 8);
    framed[24] = (byte)idx;
    Array.Copy(plaintext, 0, framed, 25, plaintext.Length);
    return ProtectedData.Protect(framed, optionalEntropy: null, _scope);
}

public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
{
    if (aad.HeaderBytes is null || aad.HeaderBytes.Length != Header.Length)
    {
        throw new ArgumentException($"AadContext.HeaderBytes must be {Header.Length} bytes.", nameof(aad));
    }

    byte[] framed = ProtectedData.Unprotect(chunkPayload, optionalEntropy: null, _scope);
    if (framed.Length < 1 + Header.FileIdLength + 8)
    {
        throw new CryptographicException("DPAPI payload too short (envelope header missing).");
    }

    bool isFinal = framed[0] == 1;

    // Verify FileId binding.
    for (int i = 0; i < Header.FileIdLength; i++)
    {
        if (framed[1 + i] != aad.HeaderBytes[Header.FileIdOffset + i])
        {
            throw new CryptographicException(
                "DPAPI chunk does not belong to this file (FileId mismatch — chunk substitution attempted).");
        }
    }

    // Verify chunkIndex binding.
    long idx = ((long)framed[17] << 56)
             | ((long)framed[18] << 48)
             | ((long)framed[19] << 40)
             | ((long)framed[20] << 32)
             | ((long)framed[21] << 24)
             | ((long)framed[22] << 16)
             | ((long)framed[23] << 8)
             |  (long)framed[24];
    if (idx != aad.ChunkIndex)
    {
        throw new CryptographicException(
            $"DPAPI chunk position mismatch (expected index {aad.ChunkIndex}, got {idx} — chunk reorder attempted).");
    }

    byte[] plaintext = new byte[framed.Length - 1 - Header.FileIdLength - 8];
    Array.Copy(framed, 25, plaintext, 0, plaintext.Length);
    return (plaintext, isFinal);
}
```

- [ ] **Step 4: Run all DPAPI tests — verify they pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~DpapiBackendTests" --nologo`
Expected: PASS for all (Windows-only; on Linux/macOS the early-return makes them skip).

- [ ] **Step 5: Run the full DPAPI round-trip suite via ChunkWriterReaderTests — must remain green on Windows**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`
Expected: 51+ tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Protect/DpapiBackend.cs tests/Winix.Protect.Tests/DpapiBackendTests.cs
git commit -m "fix(protect): bind FileId + chunkIndex into DPAPI envelope (closes C2)"
```

---

## Task 5: Fail closed on wrong-size stored key (closes C4)

`AeadBackend.GetOrCreateKey` currently silently overwrites a stored key whose length is not 32 bytes. Throw instead.

**Files:**
- Modify: `src/Winix.Protect/AeadBackend.cs`
- Modify: `tests/Winix.Protect.Tests/AeadBackendTests.cs`

- [ ] **Step 1: Write failing test asserting throw on wrong-size existing key**

Append to `tests/Winix.Protect.Tests/AeadBackendTests.cs`:

```csharp
[Fact]
public void Key_WrongSizeExisting_ThrowsInsteadOfOverwriting()
{
    NullSecretStore store = new();
    // Plant a wrong-size value at the slot the backend will look at.
    store.Set("test-namespace", "test-key", new byte[16]);

    TestAeadBackend backend = new(store);

    AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
        () => backend.EncryptChunk([1, 2, 3], aad, isFinal: true));
    Assert.Contains("test-namespace", ex.Message);
    Assert.Contains("test-key", ex.Message);

    // Ensure the planted key was NOT overwritten.
    byte[]? existing = store.Get("test-namespace", "test-key");
    Assert.NotNull(existing);
    Assert.Equal(16, existing!.Length);
}
```

- [ ] **Step 2: Verify test fails**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~Key_WrongSizeExisting_ThrowsInsteadOfOverwriting" --nologo`
Expected: FAIL — current code silently overwrites and proceeds.

- [ ] **Step 3: Update `AeadBackend.GetOrCreateKey` to throw on wrong size**

Replace the `GetOrCreateKey` body:

```csharp
private byte[] GetOrCreateKey()
{
    if (_cachedKey is not null) return _cachedKey;
    byte[]? existing = _store.Get(_namespace, _keyName);
    if (existing is not null)
    {
        if (existing.Length != KeySize)
        {
            throw new InvalidOperationException(
                $"Existing key in '{_namespace}/{_keyName}' has wrong size ({existing.Length} bytes; expected {KeySize}). " +
                $"Refusing to overwrite — encrypted files using this key would become permanently undecryptable. " +
                $"Manually delete the keychain/libsecret entry to regenerate.");
        }
        _cachedKey = existing;
        return existing;
    }
    byte[] fresh = new byte[KeySize];
    RandomNumberGenerator.Fill(fresh);
    _store.Set(_namespace, _keyName, fresh);
    _cachedKey = fresh;
    return fresh;
}
```

- [ ] **Step 4: Run test — verify pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~Key_WrongSizeExisting_ThrowsInsteadOfOverwriting" --nologo`
Expected: PASS.

- [ ] **Step 5: Update `Cli.cs` exception catch tree to surface this as a runtime error**

The new `InvalidOperationException` is already caught by the existing `catch (InvalidOperationException ex)` at `Cli.cs:59-63`, returning `RuntimeErrorExit (126)` and printing the message. No change needed — verify by reading the catch tree.

- [ ] **Step 6: F8 part 1 — explicitly deferred (no Cli-level test added)**

Round-2 review accepted that adding a `_backendFactoryOverride` injection seam to `Cli.Run` purely for one test is over-engineering. Instead: rely on Step 1's unit-level test, which already asserts the `InvalidOperationException` message contains the namespace and key name. The Cli catch tree at `Cli.cs:59-63` calls `ex.Message` directly into `Formatting.RuntimeError`, so the message reaches stderr unchanged — verified by code inspection. No placeholder Cli-level test is added; the contract is locked at the library boundary. (Closes adversarial F8 part 1.)

- [ ] **Step 7: Commit**

```bash
git add src/Winix.Protect/AeadBackend.cs tests/Winix.Protect.Tests/AeadBackendTests.cs
git commit -m "fix(protect): refuse to overwrite wrong-size stored key (closes C4, F8 part 1)"
```

---

## Task 6: Migrate platform-gated tests from `if (!OnX) return;` to `[SkippableFact] + Skip.IfNot` (closes C3)

`Xunit.SkippableFact` is already a CLAUDE.md-required pattern. Currently 5 sites in `DpapiBackendTests.cs`, 3 in `ChunkWriterReaderTests.cs`, 2 in `BackendFactoryTests.cs`, and the 2 newly-added DPAPI tests from Task 4 use the early-return pattern. All must convert. Per CLAUDE.md: keep a redundant `if (!IsX()) return;` after `Skip.IfNot` to satisfy the CA1416 analyzer; comment that it's deliberate.

**Files:**
- Modify: `tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj`
- Modify: `tests/Winix.Protect.Tests/DpapiBackendTests.cs`
- Modify: `tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs`
- Modify: `tests/Winix.Protect.Tests/BackendFactoryTests.cs`

- [ ] **Step 1: Verify Xunit.SkippableFact PackageReference exists**

Run: `grep -l "Xunit.SkippableFact" tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj` (Grep tool, not bash)

If not present, add to `tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj` inside the existing `<ItemGroup>` containing `PackageReference` entries:

```xml
<PackageReference Include="Xunit.SkippableFact" Version="1.5.*" />
```

(Look at `tests/Winix.EnvVault.Tests/Winix.EnvVault.Tests.csproj` for the pinned version actually in use across the suite.)

- [ ] **Step 2: Convert each `if (!OnWindows) return;` site to `[SkippableFact] + Skip.IfNot`**

For every test method shaped like:

```csharp
[Fact]
public void XYZ()
{
    if (!OnWindows) return;
    // body
}
```

change to:

```csharp
[SkippableFact]
public void XYZ()
{
    Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
    if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
    // body
}
```

(Add `using Xunit;` if not already present — `[SkippableFact]` lives in `Xunit` namespace via the SkippableFact package's attribute.)

Conversion sites:
- `DpapiBackendTests.cs`: lines 15-26, 28-33, 35-40, 42-51, 53-63 (existing) plus the two added in Task 4
- `ChunkWriterReaderTests.cs`: lines 93, 117, 144 (`Dpapi_*_Works`)
- `BackendFactoryTests.cs`: lines 25-31 (`Create_MachineScope_Linux_Throws` — substitute `OperatingSystem.IsLinux()`), lines 33-40 (`CreateForMarker_WrongPlatform_Throws` — substitute `OperatingSystem.IsWindows()`)

- [ ] **Step 3: Run the full suite on Windows — all tests pass, none falsely skipped**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo`
Expected: All Windows tests run; no Linux-only tests run (correctly skipped).

- [ ] **Step 4: Commit**

```bash
git add tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj tests/Winix.Protect.Tests/DpapiBackendTests.cs tests/Winix.Protect.Tests/ChunkWriterReaderTests.cs tests/Winix.Protect.Tests/BackendFactoryTests.cs
git commit -m "test(protect): migrate platform-gated tests to SkippableFact (closes C3)"
```

---

## Task 7: `--force` flag + refuse-closed destination overwrite (closes I1)

Today `protect file.json` and `unprotect file.prot` silently truncate a pre-existing destination. Default to refusing; require `--force`/`-f` to opt in.

**Files:**
- Modify: `src/Winix.Protect/ProtectOptions.cs`
- Modify: `src/Winix.Protect/ArgParser.cs`
- Modify: `src/Winix.Protect/Cli.cs`
- Modify: `src/Winix.Protect/InPlaceExecutor.cs`
- Create: `tests/Winix.Protect.Tests/CliOverwriteTests.cs`
- Modify: `src/protect/README.md`, `src/unprotect/README.md`, `src/protect/man/man1/protect.1`, `src/unprotect/man/man1/unprotect.1`

- [ ] **Step 1: Write failing test asserting `protect FILE` refuses to overwrite existing `FILE.prot`**

Create `tests/Winix.Protect.Tests/CliOverwriteTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class CliOverwriteTests
{
    [Fact]
    public void Protect_DefaultRefusesToOverwriteExistingProtFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "plaintext");
            File.WriteAllBytes(output, [0xDE, 0xAD, 0xBE, 0xEF]);

            int exit = Winix.Protect.Cli.Run([input], "protect");

            Assert.Equal(125, exit); // UsageError
            byte[] dest = File.ReadAllBytes(output);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, dest);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Protect_WithForce_OverwritesExistingProtFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "plaintext");
            File.WriteAllBytes(output, [0xDE, 0xAD, 0xBE, 0xEF]);

            int exit = Winix.Protect.Cli.Run([input, "--force"], "protect");

            // 0 on Windows/Mac; on Linux without libsecret-tools the keystore lookup may fail with 126.
            // Either way, the overwrite is what we're verifying — assert the file changed.
            byte[] dest = File.ReadAllBytes(output);
            Assert.NotEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, dest);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
```

- [ ] **Step 2: Run failing test**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~CliOverwriteTests" --nologo`
Expected: FAIL — both: `Protect_DefaultRefusesToOverwriteExistingProtFile` because no `--force` flag exists, and `Protect_WithForce_OverwritesExistingProtFile` because the flag isn't recognised.

- [ ] **Step 3: Add `Force` to `ProtectOptions`**

```csharp
public sealed record ProtectOptions(
    SubCommand SubCommand,
    string? InputPath,
    string? OutputPath,
    bool InPlace,
    bool RemoveSource,
    Scope Scope,
    bool NoVerify,
    bool Force);
```

- [ ] **Step 4: Wire `--force`/`-f` flag through `ArgParser`**

In `BuildParser`, add the flag definition near the other `Flag(...)` calls:

```csharp
.Flag("--force", "-f", "Overwrite an existing destination file. Without this flag, the tool refuses to clobber existing data.")
```

In `Parse`, add `bool force = parsed.Has("--force");` and pass to `ProtectOptions`.

- [ ] **Step 5: Update `Cli.cs` file-writes — always use `FileMode.CreateNew`; on `--force`, `File.Delete` first**

This replaces the original "up-front `File.Exists` guard" approach. The guard had two problems flagged by adversarial-review F2: (a) TOCTOU race between `Exists` and the open, (b) `FileMode.Create` on `--force` follows symlinks and can write through `/tmp/x` → `/etc/passwd`. `FileMode.CreateNew` on POSIX maps to `O_CREAT | O_EXCL`, which **refuses to follow symlinks** even when the symlink target doesn't exist — that's the security property we need.

Three sites: `Cli.cs:122` (RunProtect outputPath), `Cli.cs:195` (RunUnprotect outputPath). `InPlaceExecutor.cs` already uses `FileMode.CreateNew` for its temp files.

Pattern at each site, before opening the FileStream:

```csharp
// On --force, remove any existing file or symlink at the destination, THEN exclusively-create.
// File.Delete on a symlink unlinks the symlink itself, not its target, so this is safe.
// The window between Delete and CreateNew is small and CreateNew is O_EXCL on POSIX —
// if an attacker plants a symlink in that window, CreateNew throws EEXIST and we report it.
if (opts.Force && File.Exists(outputPath))
{
    File.Delete(outputPath);
}
```

Then change the FileStream construction:

```csharp
using FileStream dest = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
```

(CreateNew always — no `--force` → `Create` switch.)

- [ ] **Step 6: Catch `IOException` from `CreateNew` and translate into a usage error when caused by an existing file**

In `Cli.Run`, *above* the existing `catch (IOException ex)`:

```csharp
catch (IOException ex) when (
    !string.IsNullOrEmpty(opts.OutputPath) && File.Exists(opts.OutputPath))
{
    // CreateNew threw because the destination exists. Report cleanly as a usage error.
    Console.Error.WriteLine(Formatting.UsageError(invocationName,
        $"destination already exists: {opts.OutputPath}. Use --force to overwrite, or specify a different -o path."));
    return ExitCode.UsageError;
}
```

(The `when` clause's `File.Exists` is a *post-throw* check. By the time the catch matches, the throw has already happened, so this check is race-safe in the sense that "yes, the file exists at this moment" is sufficient evidence the throw was an EEXIST.)

- [ ] **Step 7: Add a symlink-attack regression test (Linux/macOS only)**

Append to `tests/Winix.Protect.Tests/CliOverwriteTests.cs`:

```csharp
[SkippableFact]
public void Protect_WithForce_DoesNotFollowSymlinkAtDestination()
{
    Skip.IfNot(!OperatingSystem.IsWindows(), "POSIX symlink semantics; Windows symlinks need admin/dev mode.");

    string dir = Path.Combine(Path.GetTempPath(), $"winix-symlink-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        string sensitiveTarget = Path.Combine(dir, "sensitive.txt");
        File.WriteAllText(sensitiveTarget, "DO NOT TOUCH");

        string input = Path.Combine(dir, "secrets.json");
        File.WriteAllText(input, "plaintext");

        string outputPath = Path.Combine(dir, "decoy.prot");
        // Plant a symlink at the destination pointing at the sensitive file.
        File.CreateSymbolicLink(outputPath, sensitiveTarget);

        int exit = Winix.Protect.Cli.Run([input, "-o", outputPath, "--force"], "protect");

        // Either the operation succeeds (replacing the symlink with a real .prot file)
        // OR it fails — but in NEITHER case should the sensitive target's contents change.
        string sensitiveAfter = File.ReadAllText(sensitiveTarget);
        Assert.Equal("DO NOT TOUCH", sensitiveAfter);
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
}
```

(`File.CreateSymbolicLink` is .NET 6+. The `[SkippableFact]` requires the Xunit.SkippableFact package added in Task 6 — if Task 7 runs before Task 6, use the placeholder `[Fact]` with `if (OperatingSystem.IsWindows()) return;` and convert in Task 6.)

- [ ] **Step 8: Run tests — verify pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~CliOverwriteTests" --nologo`
Expected: PASS.

- [ ] **Step 9: Update README and man page**

`src/protect/README.md` and `src/unprotect/README.md`: add `--force` row to the options table and a one-line note that the default refuses to overwrite, with a security note that the default refuses to follow symlinks. Same for `src/protect/man/man1/protect.1` and `src/unprotect/man/man1/unprotect.1` — add the flag in the OPTIONS section.

- [ ] **Step 10: Commit**

```bash
git add src/Winix.Protect/ProtectOptions.cs src/Winix.Protect/ArgParser.cs src/Winix.Protect/Cli.cs tests/Winix.Protect.Tests/CliOverwriteTests.cs src/protect/README.md src/unprotect/README.md src/protect/man/man1/protect.1 src/unprotect/man/man1/unprotect.1
git commit -m "fix(protect): refuse to overwrite existing destination without --force; symlink-safe (closes I1, F2)"
```

---

## Task 8: Cleanup partial output files on encrypt/decrypt failure (closes I2 + adversarial F7)

Non-`--in-place` paths leave a half-written `.prot` (encrypt) or `.json` (decrypt) on disk if the writer/reader throws midway. Wrap in try/catch that deletes the dest before rethrowing — and add a deterministic test using a throwing backend, so the cleanup property is verified, not asserted-by-inspection.

**Files:**
- Modify: `src/Winix.Protect/Cli.cs`
- Create: `tests/Winix.Protect.Tests/PartialOutputCleanupTests.cs`
- Modify: `tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj` (add `InternalsVisibleTo` if not already present, so tests can call internal `Cli.RunProtectFile`)

- [ ] **Step 1: Extract the file-output block of `Cli.RunProtect` into an `internal` testable helper**

In `src/Winix.Protect/Cli.cs`, refactor `RunProtect`'s `outputPath` branch into:

```csharp
internal static void RunProtectFile(
    Stream input,
    string outputPath,
    IProtectBackend backend,
    bool noVerify,
    bool force)
{
    if (force && File.Exists(outputPath))
    {
        File.Delete(outputPath);
    }

    try
    {
        byte[] sourceHash;
        using (FileStream dest = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] fileId = Header.NewFileId();
            byte[] header = Header.SerializeForAad(backend.Marker, fileId);
            using TeeStream tee = new(input, hasher);
            ChunkWriter.Write(tee, dest, backend, header);
            sourceHash = hasher.GetCurrentHash();
        }

        if (!noVerify)
        {
            using FileStream encrypted = new(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            RoundTripVerifier.Verify(encrypted, backend, sourceHash);
        }
    }
    catch
    {
        try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        throw;
    }
}
```

`RunProtect` calls this helper. Mirror the same pattern as `RunUnprotectFile` for the decrypt path.

- [ ] **Step 2: Confirm `InternalsVisibleTo` allows the tests to see internals**

`src/Winix.Protect/Winix.Protect.csproj` already has `<InternalsVisibleTo Include="Winix.Protect.Tests" />` per the original Task 1 plan (verify by reading the csproj). If not, add it.

- [ ] **Step 3: Add a throwing-backend test double**

Create `tests/Winix.Protect.Tests/PartialOutputCleanupTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class PartialOutputCleanupTests
{
    private sealed class ThrowingBackend : IProtectBackend
    {
        private int _calls;
        public PlatformMarker Marker => PlatformMarker.MacKeychainUser;
        public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
        {
            if (++_calls == 2)
            {
                throw new InvalidOperationException("simulated mid-stream failure");
            }
            byte[] chunk = new byte[1 + 12 + 4 + plaintext.Length + 16];
            chunk[0] = isFinal ? (byte)1 : (byte)0;
            // bogus IV / length / tag fields; never read because we throw before the second chunk.
            return chunk;
        }
        public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
            => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public void RunProtectFile_BackendThrowsMidStream_DeletesPartialOutput()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 200 KB plaintext forces multiple chunks at the default 64 KB size.
            byte[] payload = new byte[200_000];
            Random.Shared.NextBytes(payload);

            string outputPath = Path.Combine(dir, "x.prot");
            using ThrowingBackend backend = new();

            Assert.Throws<InvalidOperationException>(
                () => Winix.Protect.Cli.RunProtectFile(
                    new MemoryStream(payload),
                    outputPath,
                    backend,
                    noVerify: true,
                    force: false));

            Assert.False(File.Exists(outputPath), "partial output file should have been deleted");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
```

- [ ] **Step 4: Run the test — verify pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~PartialOutputCleanupTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Apply the same extraction + test to `RunUnprotectFile`**

Refactor `Cli.RunUnprotect`'s outputPath branch into `internal static void RunUnprotectFile(...)`. Add a parallel `RunUnprotectFile_BackendThrowsMidStream_DeletesPartialOutput` test using a `ThrowingDecryptBackend`.

- [ ] **Step 6: Add a helper-level `--rm` ordering test (closes adversarial F5)**

The `--rm` decision happens in `Cli.RunProtect` *after* `RunProtectFile` returns successfully. If `RunProtectFile` throws, the catch tree returns the runtime-error exit code without reaching the `--rm` `File.Delete` — so the source-preservation property is enforced at the *control flow* level, not by an extra check.

Lock that contract at the helper level. Append to `PartialOutputCleanupTests.cs`:

```csharp
[Fact]
public void RunProtectFile_BackendThrowsMidStream_DoesNotTouchInputPath()
{
    // Closes F5: source preservation is a *control-flow* property — RunProtectFile
    // never has the InputPath, so it cannot delete it. This test pins the contract:
    // the helper signature must remain "no input-path knowledge → no delete possible."
    string dir = Path.Combine(Path.GetTempPath(), $"winix-rm-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        string input = Path.Combine(dir, "secrets.json");
        byte[] payload = new byte[200_000];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(input, payload);

        string outputPath = Path.Combine(dir, "x.prot");
        using ThrowingBackend backend = new();

        Assert.Throws<InvalidOperationException>(
            () => Winix.Protect.Cli.RunProtectFile(
                File.OpenRead(input),
                outputPath,
                backend,
                noVerify: true,
                force: false));

        Assert.True(File.Exists(input), "input file must still exist after encrypt failure");
        Assert.False(File.Exists(outputPath), "partial output should have been deleted (Task 8 Step 4 contract)");
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
}
```

This is a real assertion (control-flow property + post-exception state inspection), not a Cli-level injection. Combined with code inspection of `Cli.RunProtect` (the only place `--rm`'s `File.Delete(opts.InputPath)` is called, gated to run after the helper returns), the F5 contract is locked.

- [ ] **Step 7: Run the full suite — verify nothing regresses**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`

- [ ] **Step 8: Commit**

```bash
git add src/Winix.Protect/Cli.cs tests/Winix.Protect.Tests/PartialOutputCleanupTests.cs
git commit -m "fix(protect): delete partial output on encrypt/decrypt failure (closes I2, F5, F7)"
```

---

## Task 9: Bottom catch returns 126, not 1 (closes I3)

`Cli.cs:89-93`'s `catch (Exception ex)` returns `1`. Documentation lists 0/125/126. Use `RuntimeErrorExit` (126).

**Files:**
- Modify: `src/Winix.Protect/Cli.cs`

- [ ] **Step 1: Update `Cli.cs:92`**

```csharp
catch (Exception ex)
{
    Console.Error.WriteLine(Formatting.RuntimeError(invocationName, $"unexpected error: {ex.Message}"));
    return RuntimeErrorExit;
}
```

- [ ] **Step 2: Bottom-catch test is explicitly deferred (closes adversarial F12)**

The `catch (Exception ex)` bottom-catch has no clean injection seam without adding test-only DI to `Cli.Run`. Per the ADR's "Decisions Explicitly Deferred" entry, this is verified by code inspection only. No test is added in this task. Commit message must include "verified by code inspection — Cli.cs catch tree, exit returns RuntimeErrorExit".

- [ ] **Step 3: Run tests — verify nothing regresses**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Protect/Cli.cs
git commit -m "fix(protect): return 126 from bottom catch to match documented exit codes (closes I3)"
```

---

## Task 10: Reject unknown invocation names (closes I5)

`Cli.Run(args, invocationName)` defaults to `Protect` for any name ≠ `"unprotect"`. A renamed binary or third entry point silently encrypts. Throw on unknown names.

**Files:**
- Modify: `src/Winix.Protect/Cli.cs`
- Modify: `tests/Winix.Protect.Tests/CliOverwriteTests.cs` (or new file)

- [ ] **Step 1: Write failing test**

Append to `CliOverwriteTests.cs`:

```csharp
[Fact]
public void Run_UnknownInvocationName_Returns126AndNamesTheBadInvocation()
{
    StringWriter capturedErr = new();
    TextWriter originalErr = Console.Error;
    Console.SetError(capturedErr);
    try
    {
        int exit = Winix.Protect.Cli.Run([], "protect-rename");
        Assert.Equal(126, exit);
        string err = capturedErr.ToString();
        Assert.Contains("protect-rename", err);
        Assert.Contains("must be 'protect' or 'unprotect'", err);
    }
    finally { Console.SetError(originalErr); }
}
```

(Closes adversarial F8 part 2 — explicit exit-code assertion plus stderr-contains-name assertion.)

- [ ] **Step 2: Update `Cli.Run` to validate invocationName**

Add at the top of `Cli.Run`, before the `SubCommand` derivation:

```csharp
SubCommand subCommand = invocationName switch
{
    "protect" => SubCommand.Protect,
    "unprotect" => SubCommand.Unprotect,
    _ => throw new ArgumentException(
        $"Cli.Run requires invocationName 'protect' or 'unprotect' (got '{invocationName}').",
        nameof(invocationName)),
};
```

This throws to the caller (`Program.Main`); to ensure the CLI returns non-zero rather than crashing, wrap the dispatch in the existing try/catch tree by using the bottom-catch (now 126).

Actually — `Program.Main` is `static int Main(string[] args) => Winix.Protect.Cli.Run(args, "protect");`. An exception escaping `Cli.Run` propagates out of `Main` and the runtime returns -1 with a stack trace. That's fine for *binary* invocation (the literal won't drift), but the test calls `Cli.Run` directly. So wrap the validation inside the try block and return the appropriate exit code:

Replace the dispatch block:

```csharp
SubCommand subCommand;
if (invocationName == "protect") subCommand = SubCommand.Protect;
else if (invocationName == "unprotect") subCommand = SubCommand.Unprotect;
else
{
    Console.Error.WriteLine($"{invocationName}: invocation name must be 'protect' or 'unprotect' (got '{invocationName}'). Refusing to default to encrypt.");
    return RuntimeErrorExit;
}
```

Place this *before* `ConsoleEnv.EnableAnsiIfNeeded()` so the validation is the very first thing.

- [ ] **Step 3: Run test — verify pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~Run_UnknownInvocationName_Returns126AndNamesTheBadInvocation" --nologo`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Winix.Protect/Cli.cs tests/Winix.Protect.Tests/CliOverwriteTests.cs
git commit -m "fix(protect): reject unknown invocation names instead of defaulting to encrypt (closes I5)"
```

---

## Task 11: Flush(true) before File.Move in InPlaceExecutor (closes I6)

`File.Move` is atomic at the rename layer but doesn't fsync. Add `dest.Flush(flushToDisk: true)` before the `using` closes.

**Files:**
- Modify: `src/Winix.Protect/InPlaceExecutor.cs`

- [ ] **Step 1: Update `ExecuteEncrypt`'s using block**

Replace:

```csharp
using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
{
    byte[] fileId = Header.NewFileId();
    byte[] header = Header.SerializeForAad(backend.Marker, fileId);
    using TeeReadStream teeSource = new(source, hasher);
    ChunkWriter.Write(teeSource, dest, backend, header);
    sourceHash = hasher.GetCurrentHash();
}
```

with:

```csharp
using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
{
    using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    {
        byte[] fileId = Header.NewFileId();
        byte[] header = Header.SerializeForAad(backend.Marker, fileId);
        using TeeReadStream teeSource = new(source, hasher);
        ChunkWriter.Write(teeSource, dest, backend, header);
        // FlushFileBuffers / fsync before close so the rename below promotes durable bytes.
        dest.Flush(flushToDisk: true);
    }
    sourceHash = hasher.GetCurrentHash();
}
```

Apply the same pattern to `ExecuteDecrypt`.

- [ ] **Step 2: Run tests — verify nothing regresses**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`

- [ ] **Step 3: Commit**

```bash
git add src/Winix.Protect/InPlaceExecutor.cs
git commit -m "fix(protect): flush-to-disk in InPlaceExecutor before atomic rename (closes I6)"
```

---

## Task 12: AeadBackend implements IDisposable, zeros key on dispose (closes I7)

Cache key bytes are sensitive material. Add disposal that calls `CryptographicOperations.ZeroMemory` on the cached key. Update `IProtectBackend` to extend `IDisposable`. Update `Cli.Run` to `using` the backend.

**Files:**
- Modify: `src/Winix.Protect/IProtectBackend.cs`
- Modify: `src/Winix.Protect/AeadBackend.cs`
- Modify: `src/Winix.Protect/DpapiBackend.cs`
- Modify: `src/Winix.Protect/Cli.cs`

- [ ] **Step 1: Update `IProtectBackend` to extend `IDisposable`**

```csharp
public interface IProtectBackend : IDisposable
{
    PlatformMarker Marker { get; }
    byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal);
    (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad);
}
```

- [ ] **Step 2: Implement `Dispose` in `AeadBackend`**

Add:

```csharp
public void Dispose()
{
    if (_cachedKey is not null)
    {
        CryptographicOperations.ZeroMemory(_cachedKey);
        _cachedKey = null;
    }
    GC.SuppressFinalize(this);
}
```

- [ ] **Step 3: Implement `Dispose` in `DpapiBackend`**

```csharp
public void Dispose() { /* no managed resources to release */ }
```

- [ ] **Step 4: Update `Cli.Run` to `using` the backend**

Three sites where `IProtectBackend backend = BackendFactory.Create(opts.Scope);` or `BackendFactory.CreateForMarker(...)` is followed by use. Wrap each in `using`:

```csharp
using IProtectBackend backend = BackendFactory.Create(opts.Scope);
```

- [ ] **Step 5: Add `Dispose_ZeroesCachedKey` test (closes adversarial F9)**

`AeadBackend._cachedKey` is private. Use `InternalsVisibleTo` (already configured) plus an `internal byte[]? PeekCachedKeyForTests()` accessor on `AeadBackend` to expose it for verification. Add the accessor:

```csharp
// Test-only accessor; preserves invariant that production code never reads _cachedKey directly.
internal byte[]? PeekCachedKeyForTests() => _cachedKey;
```

Append to `tests/Winix.Protect.Tests/AeadBackendTests.cs`:

```csharp
[Fact]
public void Dispose_ZeroesCachedKey()
{
    NullSecretStore store = new();
    TestAeadBackend backend = new(store);

    // Force key materialisation by encrypting one chunk.
    AadContext aad = new(Header.SerializeForAad(PlatformMarker.MacKeychainUser, new byte[16]), 0, true);
    backend.EncryptChunk([1, 2, 3], aad, isFinal: true);

    byte[]? before = backend.PeekCachedKeyForTests();
    Assert.NotNull(before);
    Assert.Equal(32, before!.Length);
    bool allZeroBefore = true;
    foreach (byte b in before) { if (b != 0) { allZeroBefore = false; break; } }
    Assert.False(allZeroBefore, "key should be random pre-Dispose");

    // Capture the buffer reference so we can inspect it after Dispose nulls _cachedKey.
    byte[] capturedRef = before!;
    backend.Dispose();

    foreach (byte b in capturedRef)
    {
        Assert.Equal(0, b);
    }
}
```

- [ ] **Step 6: Run tests — verify nothing regresses**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --nologo --verbosity quiet`

- [ ] **Step 7: Commit**

```bash
git add src/Winix.Protect/IProtectBackend.cs src/Winix.Protect/AeadBackend.cs src/Winix.Protect/DpapiBackend.cs src/Winix.Protect/Cli.cs tests/Winix.Protect.Tests/AeadBackendTests.cs
git commit -m "fix(protect): IProtectBackend : IDisposable; zero key on dispose (closes I7, F9)"
```

---

## Task 13: Defensive tests + better CryptographicException messages (closes I8 + nit)

Add tests for: truncated header (< 22 bytes), EOF mid-header, AuthenticationTagMismatch distinct from generic CryptographicException error message.

**Files:**
- Modify: `src/Winix.Protect/Cli.cs`
- Modify: `tests/Winix.Protect.Tests/HeaderTests.cs` (already covers truncated; verify still good after Task 2)
- Create: `tests/Winix.Protect.Tests/CliErrorHandlingTests.cs`

- [ ] **Step 1: Add `EndOfStreamException` catch to `Cli.Run`**

Add to the catch tree (between `IOException` and `Exception`):

```csharp
catch (EndOfStreamException ex)
{
    Console.Error.WriteLine(Formatting.RuntimeError(invocationName,
        $"ciphertext is truncated or not a protect file: {ex.Message}"));
    return RuntimeErrorExit;
}
```

- [ ] **Step 2: Refine the `CryptographicException` message to distinguish auth-tag-mismatch from generic**

Replace the existing catch:

```csharp
catch (System.Security.Cryptography.AuthenticationTagMismatchException ex)
{
    Console.Error.WriteLine(Formatting.RuntimeError(invocationName,
        $"authentication failed — file is corrupted or tampered with ({ex.GetType().Name})."));
    return RuntimeErrorExit;
}
catch (CryptographicException ex)
{
    Console.Error.WriteLine(Formatting.RuntimeError(invocationName,
        $"decryption failed — this file was encrypted by a different user or on a different machine ({ex.GetType().Name})."));
    return RuntimeErrorExit;
}
```

(The `AuthenticationTagMismatchException` catch must be ABOVE the broader `CryptographicException`.)

- [ ] **Step 3: Add CliErrorHandlingTests**

```csharp
#nullable enable
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class CliErrorHandlingTests
{
    [Fact]
    public void Unprotect_TruncatedFile_ReturnsRuntimeError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-trunc-{System.Guid.NewGuid():N}.prot");
        File.WriteAllBytes(path, [(byte)'W', (byte)'P']);
        try
        {
            int exit = Winix.Protect.Cli.Run([path], "unprotect");
            Assert.Equal(126, exit);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Unprotect_BadMagic_ReturnsRuntimeError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-badmagic-{System.Guid.NewGuid():N}.prot");
        // 22 bytes of garbage that don't match WPRT magic.
        File.WriteAllBytes(path, new byte[22]);
        try
        {
            int exit = Winix.Protect.Cli.Run([path], "unprotect");
            Assert.Equal(126, exit);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [SkippableFact]
    public void Unprotect_TamperedCiphertext_StderrSaysAuthenticationFailed()
    {
        // Closes adversarial F6: distinguishes auth-tag-mismatch ("file tampered") from
        // generic CryptographicException ("different user/machine").
        // Run on platforms where AEAD is exercised end-to-end (mac/linux); on Windows the
        // DPAPI path produces a different message via its own check.
        Skip.IfNot(!OperatingSystem.IsWindows(), "AEAD path; Windows uses DPAPI envelope error path.");

        string dir = Path.Combine(Path.GetTempPath(), $"winix-tamper-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "round-trip me");

            int encExit = Winix.Protect.Cli.Run([input, "-o", output], "protect");
            Assert.Equal(0, encExit);

            // Flip a byte in the ciphertext body (after the 22-byte header).
            byte[] bytes = File.ReadAllBytes(output);
            bytes[Header.Length + 30] ^= 0x01;
            File.WriteAllBytes(output, bytes);

            StringWriter capturedErr = new();
            TextWriter originalErr = Console.Error;
            Console.SetError(capturedErr);
            try
            {
                int decExit = Winix.Protect.Cli.Run([output], "unprotect");
                Assert.Equal(126, decExit);
                string err = capturedErr.ToString();
                Assert.Contains("authentication", err, StringComparison.OrdinalIgnoreCase);
            }
            finally { Console.SetError(originalErr); }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
```

- [ ] **Step 4: Run tests — verify pass**

Run: `dotnet test tests/Winix.Protect.Tests/Winix.Protect.Tests.csproj --filter "FullyQualifiedName~CliErrorHandlingTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Protect/Cli.cs tests/Winix.Protect.Tests/CliErrorHandlingTests.cs
git commit -m "fix(protect): handle truncated/bad-magic ciphertext + distinct auth-tag error message (closes I8)"
```

---

## Task 14: Update README, man page, --describe metadata to reflect new format + flags

Document: `--force` flag, the new 22-byte header layout (briefly, in the "WPRT Format" section), the integrity model now binds per-file, and the explicit "different OS user with same machine scope" caveat.

**Files:**
- Modify: `src/protect/README.md`
- Modify: `src/unprotect/README.md`
- Modify: `src/protect/man/man1/protect.1`
- Modify: `src/unprotect/man/man1/unprotect.1`
- Modify: `src/Winix.Protect/ArgParser.cs` (the `Section("WPRT Format", ...)` text)

- [ ] **Step 1: Update `ArgParser.cs` `Section("WPRT Format", ...)`**

Replace the current text with:

```csharp
.Section("WPRT Format",
    "Header (22 bytes): magic 'WPRT' + version 0x01 + backend-marker byte (0x01 DPAPI-user, 0x02 DPAPI-machine, 0x10 Keychain-user, 0x11 Keychain-machine, 0x20 libsecret-user) + 16-byte random FileId.\n" +
    "Body: 64 KB chunks. AEAD path (Keychain/libsecret): AES-256-GCM with AAD = header || chunkIndex || isFinal — every chunk is bound to this specific file at this specific position.\n" +
    "DPAPI path (Windows): the same FileId+chunkIndex binding lives inside the protected blob, so chunk reorder and cross-file substitution are detected even though DPAPI itself has no AAD slot.\n" +
    "Final chunk carries a truncation-detection flag.")
```

- [ ] **Step 2: Update READMEs**

`src/protect/README.md` and `src/unprotect/README.md`: in the options table, add the `--force` row. In the "How It Works" section (or equivalent), update the chunk description to mention per-file FileId binding. Add a new "Integrity Model" section noting the threat-model boundary (write access without key access — backups, sync services).

- [ ] **Step 3: Update man pages**

Add `--force` to the OPTIONS section. Reference the WPRT format header length update if the man page mentions it.

- [ ] **Step 4: Commit**

```bash
git add src/protect/README.md src/unprotect/README.md src/protect/man/man1/protect.1 src/unprotect/man/man1/unprotect.1 src/Winix.Protect/ArgParser.cs
git commit -m "docs(protect): update README, man page, --describe for FileId-bound integrity model"
```

---

## Task 14b: Fix AEAD-backend secret-store namespace contract violation (Linux smoke regression)

**Discovered during Task 15 Step 3 (WSL smoke) on 2026-04-30. Plan-to-code divergence — recorded here per `feedback_plan_to_code_divergence_must_be_recorded.md`.**

`AeadKeychainBackend` and `AeadLibsecretBackend` both pass `"winix-protect"` as the secret-store namespace. On 2026-04-22 (commit `6340999`, post-envvault), `LinuxLibsecretStore` was tightened to require namespaces of form `<tool>/<sub...>` (validated by `LinuxNamespace.ExtractTool`). `"winix-protect"` lacks a slash, so on first encrypt/decrypt the libsecret backend throws `ArgumentException("Namespace must be of form '<tool>/<sub...>' (got 'winix-protect')")` and `protect` exits with code 126 ("unexpected error").

**Why the existing tests didn't catch it:**
- `LinuxNamespaceTests` cover the helper's edge cases (empty, no-slash, leading-slash) but the production-side contract — that *every backend's namespace constant* satisfies the helper — wasn't asserted.
- macOS `MacOsKeychainStore` and Windows `DpapiBackend` don't validate namespaces, so the bug is Linux-specific.
- No end-to-end Linux integration test of `protect`/`unprotect` exists (libsecret needs a running secret service, hard to provide in CI).

**Why no migration shim is needed:**
- Linux protect has been broken since 2026-04-22 (first encrypt fails before any key is stored), so no Linux user has a key under the old namespace.
- macOS/protect is unreleased (release/v0.4.0 still untagged), so no macOS user has a key under the old namespace either.

**Files:**
- Create: `src/Winix.Protect/SecretLayout.cs` (single-source-of-truth for the AEAD namespace constant)
- Modify: `src/Winix.Protect/AeadKeychainBackend.cs`
- Modify: `src/Winix.Protect/AeadLibsecretBackend.cs`
- Create: `tests/Winix.Protect.Tests/AeadBackendNamespaceContractTests.cs`

- [ ] **Step 1: Add a failing regression test that locks the contract**

Create `tests/Winix.Protect.Tests/AeadBackendNamespaceContractTests.cs` that asserts the AEAD backends' namespace constant satisfies `LinuxNamespace.ExtractTool` (i.e. has a non-empty tool prefix followed by a slash). Test must reference `SecretLayout.KeyNamespace` directly so a future drift in the constant trips the test.

- [ ] **Step 2: Run the test — verify it fails (the namespace constant doesn't exist yet, so the test file doesn't compile)**

- [ ] **Step 3: Add `SecretLayout.cs` with `internal const string KeyNamespace = "winix-protect/keys"`**

Single-source-of-truth so both AEAD backends stay aligned.

- [ ] **Step 4: Update `AeadKeychainBackend.cs` and `AeadLibsecretBackend.cs` to pass `SecretLayout.KeyNamespace` instead of the literal `"winix-protect"`**

- [ ] **Step 5: Run the test — verify it passes; run the full Winix.Protect.Tests suite to confirm no regressions**

- [ ] **Step 6: Re-publish the linux-x64 binary and re-run the WSL smoke from Task 15 Step 3**

Confirm `protect FILE --rm` followed by `unprotect FILE.prot` round-trips successfully end-to-end on Linux.

- [ ] **Step 7: Commit**

```bash
git commit -m "fix(protect): align AEAD backend namespace with libsecret <tool>/<sub> contract

Linux smoke (Task 15 Step 3) revealed AeadLibsecretBackend was passing
'winix-protect' as the secret-store namespace, which has failed
LinuxNamespace.ExtractTool's <tool>/<sub...> contract since 6340999.
Bug shipped because no end-to-end Linux test existed and the helper-level
unit tests didn't assert that backend constants satisfy the contract.

- New SecretLayout.KeyNamespace = 'winix-protect/keys' (single source).
- Both AeadKeychainBackend and AeadLibsecretBackend now use it.
- New AeadBackendNamespaceContractTests locks the contract.
- No migration: protect is unreleased on macOS, and Linux has been
  broken since the contract tightened, so no users have stored keys
  under the old namespace.

Plan-to-code divergence recorded as Task 14b in
docs/plans/2026-04-29-protect-format-hardening-plan.md and as a new
decision in the companion ADR."
```

---

## Task 15: Cross-platform smoke + retire the protect-format-hardening branch via merge

Verify the changes work end-to-end on WSL and macOS via manual smoke tests using the playbook from `schedule`. Then merge back to `release/v0.4.0`.

**Files:**
- (no code changes; verification only)

- [ ] **Step 1: Build on Windows**

Run: `dotnet build Winix.sln -c Debug --nologo`
Expected: Build succeeds, no warnings.

- [ ] **Step 2: Windows smoke**

```bash
dotnet publish src/protect/protect.csproj -c Release -r win-x64 --nologo
echo "hello world" > /tmp/win-smoke.txt
./src/protect/bin/Release/net10.0/win-x64/publish/protect.exe /tmp/win-smoke.txt --rm
ls -la /tmp/win-smoke.txt.prot
./src/unprotect/bin/Release/net10.0/win-x64/publish/unprotect.exe /tmp/win-smoke.txt.prot
diff /tmp/win-smoke.txt /tmp/win-smoke.txt.original  # expect identical
```

- [ ] **Step 3: WSL smoke (Claude runs)**

```bash
wsl -e bash -c 'cd /mnt/d/projects/winix && dotnet publish src/protect/protect.csproj -c Release -r linux-x64 --nologo'
wsl -e bash -c 'echo "hello from wsl" > /tmp/wsl-smoke.txt && /mnt/d/projects/winix/src/protect/bin/Release/net10.0/linux-x64/publish/protect /tmp/wsl-smoke.txt --rm'
wsl -e bash -c '/mnt/d/projects/winix/src/unprotect/bin/Release/net10.0/linux-x64/publish/unprotect /tmp/wsl-smoke.txt.prot'
wsl -e bash -c 'cat /tmp/wsl-smoke.txt'  # expect "hello from wsl"
```

If `libsecret-tools` is not installed in WSL, smoke test will fail with a keystore error — note this and either install (`sudo apt install libsecret-tools`) or document as a precondition.

- [ ] **Step 4: macOS smoke (user runs, on physical Mac)**

Provide the user with:

```bash
git pull origin fix/protect-format-hardening
dotnet publish src/protect/protect.csproj -c Release -r osx-arm64 --nologo
echo "hello from mac" > /tmp/mac-smoke.txt
./src/protect/bin/Release/net10.0/osx-arm64/publish/protect /tmp/mac-smoke.txt --rm
./src/unprotect/bin/Release/net10.0/osx-arm64/publish/unprotect /tmp/mac-smoke.txt.prot
cat /tmp/mac-smoke.txt
```

User reports back: round-trips cleanly OR reports specific failure.

- [ ] **Step 5: Run the full suite once more, on Windows**

Run: `dotnet test Winix.sln --nologo`
Expected: total tests for the suite (currently ~2057), all passing or skipped-correctly.

- [ ] **Step 6: Update memory entry for protect status**

Update `C:\Users\troy.ONTEMPO\.claude\projects\d--projects-winix\memory\` — find the relevant memory file and append a note that protect/unprotect format-hardening completed on this date with the FileId binding change.

- [ ] **Step 7: Merge `fix/protect-format-hardening` back into `release/v0.4.0`**

Use a non-fast-forward merge to preserve the branch history.

```bash
git checkout release/v0.4.0
git merge --no-ff fix/protect-format-hardening -m "merge: protect/unprotect format hardening (C1-C4 + I1-I8)"
```

- [ ] **Step 8: Delete the local branch**

```bash
git branch -d fix/protect-format-hardening
```

(Don't push to remote yet — that's a separate user decision per CLAUDE.md autonomy boundaries.)

---

## Self-Review Checklist

Before handing off to execution, run through:

1. **Spec coverage** — every Critical and Important from round-1 review + adversarial-review findings has a task:
   - C1 (AEAD substitution) → Tasks 1–3
   - C2 (DPAPI no-AAD) → Task 4
   - C3 (test pattern) → Task 6
   - C4 (key overwrite) → Task 5
   - I1 (overwrite) → Task 7
   - I2 (partial output) → Task 8
   - I3 (exit code 1) → Task 9
   - I4 (header drift) → Tasks 1–3
   - I5 (invocation name) → Task 10
   - I6 (fsync) → Task 11
   - I7 (Disposable) → Task 12
   - I8 (defensive tests) → Task 13
   - Cross-platform smoke → Task 15
   - Docs → Task 14
   - **Adversarial F1** (FileIdOffset constant) → Task 2 + Task 4
   - **Adversarial F2** (TOCTOU + symlink-follow on `--force`) → Task 7 (rewritten)
   - **Adversarial F4** (empty/one-byte source) → Task 3 (added)
   - **Adversarial F5** (`--rm` ordering) → Task 8 Step 6
   - **Adversarial F6** (auth-tag-mismatch stderr distinction) → Task 13
   - **Adversarial F7** (partial-output cleanup test seam) → Task 8 (rewritten)
   - **Adversarial F8** (Cli-level error tests) → Task 5 Step 6, Task 10 Step 1
   - **Adversarial F9** (Dispose zeroes key) → Task 12 Step 5
   - **Adversarial F3, F10, F11, F12, F13** → ADR "Decisions Explicitly Deferred" table

2. **Placeholder scan** — clean. Round-2 review flagged two `Assert.True(true)` placeholders in Tasks 5 and 8; both are now resolved. Task 5 Step 6 explicitly defers Cli-level testing to the unit-level message-content assertion in Step 1. Task 8 Step 6 replaces the placeholder with a real control-flow contract test (`RunProtectFile_BackendThrowsMidStream_DoesNotTouchInputPath`). No remaining "TBD by executor" gates.

3. **Type consistency** — `Header.SerializeForAad(PlatformMarker)` from Task 1 is *replaced* (not augmented) by `Header.SerializeForAad(PlatformMarker, byte[])` in Task 2. Task 1's commit lands the single-arg helper that callers depend on; Task 2's commit changes the signature, updates all callers, and updates all tests. Tests in Task 1 stay green by virtue of the single-arg helper existing; tests in Task 2 stay green because the callers and test helpers update simultaneously, AND the test header-bytes literals get the zero-FileId form so existing round-trip tests keep working. This intermediate state ("AEAD AAD now binds FileId via headerBytes") is itself green and bisectable per adversarial F13.

4. **Risks not captured** — `FileMode.CreateNew` is `O_CREAT | O_EXCL` on POSIX, so it refuses to follow symlinks even when the target doesn't exist; Task 7 Step 5 documents this. Windows has separate symlink semantics that require admin / dev mode to create; the adversarial test gates `[SkippableFact]` to non-Windows for that reason.

---

## Execution Handoff

Plan complete. Two execution options:

**1. Subagent-Driven (recommended for the format-change tasks)** — Each major task gets a fresh subagent, with reviewer between. Best when correctness on a specific commit matters.

**2. Inline Execution** — Continue in this session. Faster wall-clock, less ceremony. Best for the mechanical refactoring tasks (1, 6, 14).

**Reminder:** per `CLAUDE.md`, the next step is to invoke `adversarial-plan-review` BEFORE any code changes. That dispatches a fresh subagent (not the planner), runs in at most two passes, and produces findings in the four-category taxonomy (Plan blocker / Test gap / Explicit defer / Not applicable).
