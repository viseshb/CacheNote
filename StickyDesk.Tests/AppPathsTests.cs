using StickyDesk.Core.Infrastructure;

namespace StickyDesk.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void AllPaths_RootedUnderAppRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "stickydesk-paths", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(root, paths.Root);
        Assert.Equal(Path.Combine(root, "data"), paths.DataDir);
        Assert.Equal(Path.Combine(root, "attachments"), paths.AttachmentsDir);
        Assert.Equal(Path.Combine(root, "config"), paths.ConfigDir);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogsDir);
        Assert.Equal(Path.Combine(root, "data", "stickydesk.db"), paths.DatabaseFile);

        // Every data path must live inside the app root (the invariant).
        foreach (var p in new[] { paths.DataDir, paths.AttachmentsDir, paths.ConfigDir, paths.LogsDir })
            Assert.StartsWith(root, p);
    }

    [Fact]
    public void EnsureCreated_CreatesEveryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "stickydesk-paths", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        try
        {
            paths.EnsureCreated();
            Assert.True(Directory.Exists(paths.DataDir));
            Assert.True(Directory.Exists(paths.AttachmentsDir));
            Assert.True(Directory.Exists(paths.ConfigDir));
            Assert.True(Directory.Exists(paths.LogsDir));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
