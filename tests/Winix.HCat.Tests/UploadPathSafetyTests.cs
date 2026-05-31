using System.IO;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class UploadPathSafetyTests
{
    private static bool NeverExists(string _) => false;

    [Fact]
    public void Reduces_to_base_name_and_resolves_under_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "uproot");
        string? target = UploadPathSafety.ResolveTarget(root, "report.pdf", NeverExists);
        Assert.NotNull(target);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "report.pdf")), target);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/dir/file.txt")]   // directory components stripped → "file.txt", still safe; assert it stays under root
    public void Strips_directory_components_and_never_escapes(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "uproot");
        string? target = UploadPathSafety.ResolveTarget(root, name, NeverExists);
        Assert.NotNull(target);
        Assert.StartsWith(Path.GetFullPath(root), target!);
    }

    [Fact]
    public void Rejects_empty_or_dotonly_names()
    {
        string root = Path.Combine(Path.GetTempPath(), "uproot");
        Assert.Null(UploadPathSafety.ResolveTarget(root, "", NeverExists));
        Assert.Null(UploadPathSafety.ResolveTarget(root, "..", NeverExists));
    }

    [Fact]
    public void Suffixes_on_collision()
    {
        string root = Path.Combine(Path.GetTempPath(), "uproot");
        string first = Path.GetFullPath(Path.Combine(root, "a.txt"));
        // a.txt exists, a.2.txt does not
        bool Exists(string p) => p == first;
        string? target = UploadPathSafety.ResolveTarget(root, "a.txt", Exists);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "a.2.txt")), target);
    }

    [Fact]
    public void IsWithinServedTree_detects_containment()
    {
        Assert.True(UploadPathSafety.IsWithinServedTree("/srv/www", "/srv/www"));        // root itself
        Assert.True(UploadPathSafety.IsWithinServedTree("/srv/www", "/srv/www/uploads"));
        Assert.False(UploadPathSafety.IsWithinServedTree("/srv/www", "/srv/other"));
        Assert.False(UploadPathSafety.IsWithinServedTree("/srv/www", "/srv/www-evil")); // sibling-prefix, NOT under
    }

    [Fact]
    public void ResolveTarget_rejects_a_sibling_whose_name_prefixes_the_root()
    {
        // F2: a bare StartsWith(root) check would wrongly accept "<root>-evil"; the boundary check rejects it.
        // (Leaf reduction already closes the practical escape; this pins the defence-in-depth backstop.)
        string root = Path.Combine(Path.GetTempPath(), "uproot");
        // The leaf reduction means a traversal name resolves under root; assert it never lands in "<root>-evil".
        string? target = UploadPathSafety.ResolveTarget(root, "x.txt", NeverExists);
        Assert.NotNull(target);
        Assert.DoesNotContain("uproot-evil", target!);
    }

    [Fact]
    public void IsServedRoot_is_true_only_for_the_root_itself()
    {
        // The single "downloadable" condition: only the served root cannot be excluded from serving.
        // An in-tree subfolder is within the tree but NOT the served root, so it is hidden, not downloadable.
        Assert.True(UploadPathSafety.IsServedRoot("/srv/www", "/srv/www"));
        Assert.True(UploadPathSafety.IsServedRoot("/srv/www", "/srv/www/"));        // trailing-separator normalised
        Assert.False(UploadPathSafety.IsServedRoot("/srv/www", "/srv/www/uploads")); // in-tree subfolder
        Assert.False(UploadPathSafety.IsServedRoot("/srv/www", "/srv/other"));
    }

    [Fact]
    public void IsWithinServedTree_is_case_insensitive_on_windows_only()
    {
        // Step 4: pins the OS-conditional comparer. On Windows (case-insensitive FS) a cased variant
        // must be treated as contained; on case-sensitive *nix it must not.
        bool result = UploadPathSafety.IsWithinServedTree("/srv/www", "/srv/WWW/uploads");
        if (System.OperatingSystem.IsWindows())
        {
            Assert.True(result);
        }
        else
        {
            Assert.False(result);
        }
    }
}
