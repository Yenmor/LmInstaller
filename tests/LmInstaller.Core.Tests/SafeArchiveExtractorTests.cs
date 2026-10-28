using LmInstaller.Core.Archive;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class SafeArchiveExtractorTests
{
    [Fact]
    public void Extract_writes_every_entry_and_preserves_the_directory_tree()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("app.zip"), archive =>
        {
            archive.AddText("MyApp/MyApp.exe", "binary");
            archive.AddText("MyApp/libs/core.dll", "dll");
            archive.AddDirectory("MyApp/empty");
        });

        var destination = temp.CreateSubdirectory("extracted");
        var result = new SafeArchiveExtractor().Extract(zip, destination);

        Assert.Contains("MyApp/MyApp.exe", result.ExtractedFiles);
        Assert.Contains("MyApp/libs/core.dll", result.ExtractedFiles);
        Assert.True(File.Exists(Path.Combine(destination, "MyApp", "MyApp.exe")));
        Assert.True(Directory.Exists(Path.Combine(destination, "MyApp", "empty")));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(destination, "MyApp", "MyApp.exe")));
    }

    [Fact]
    public void Extract_refuses_to_write_outside_the_destination()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("evil.zip"), archive =>
        {
            archive.AddText("../escaped.txt", "evil");
            archive.AddText("MyApp/MyApp.exe", "binary");
        });

        var destination = temp.CreateSubdirectory("extracted");

        Assert.Throws<UnsafeArchiveException>(() => new SafeArchiveExtractor().Extract(zip, destination));
        Assert.False(File.Exists(temp.Combine("escaped.txt")));
    }

    [Fact]
    public void Extract_skips_symbolic_link_entries()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("link.zip"), archive =>
        {
            archive.AddBytes("MyApp/link", System.Text.Encoding.UTF8.GetBytes("../../outside"), ZipFixture.SymlinkAttributes);
            archive.AddText("MyApp/MyApp.exe", "binary");
        });

        var destination = temp.CreateSubdirectory("extracted");
        var result = new SafeArchiveExtractor().Extract(zip, destination);

        Assert.Contains("MyApp/link", result.SkippedEntries);
        Assert.False(File.Exists(Path.Combine(destination, "MyApp", "link")));
        Assert.True(File.Exists(Path.Combine(destination, "MyApp", "MyApp.exe")));
    }

    [Fact]
    public void Extract_enforces_the_total_size_limit_while_streaming()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("big.zip"), archive => archive.AddBytes("big.bin", ZipFixture.Zeros(256 * 1024)));

        var destination = temp.CreateSubdirectory("extracted");
        var extractor = new SafeArchiveExtractor(new ArchiveLimits
        {
            MaxTotalUncompressedBytes = 1024,
            MaxCompressionRatio = double.MaxValue,
            MaxEntryCompressionRatio = double.MaxValue,
            CompressionRatioFreeBytes = long.MaxValue,
        });

        Assert.Throws<ArchiveLimitExceededException>(() => extractor.Extract(zip, destination));
    }
}
