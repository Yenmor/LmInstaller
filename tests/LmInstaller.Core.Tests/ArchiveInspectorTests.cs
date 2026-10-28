using System.IO.Compression;
using LmInstaller.Core.Archive;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class ArchiveInspectorTests
{
    [Fact]
    public void Inspect_accepts_a_normal_multi_level_archive()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("app.zip"), archive =>
        {
            archive.AddText("MyApp/MyApp.exe", "binary");
            archive.AddText("MyApp/readme.txt", "hello");
            archive.AddText("MyApp/libs/core.dll", "dll");
        });

        var report = new ArchiveInspector().Inspect(zip);

        Assert.True(report.IsSafeToExtract);
        Assert.Empty(report.Violations);
        Assert.Equal(3, report.Entries.Count);
        Assert.Equal("MyApp", report.NameHint);
    }

    [Fact]
    public void Inspect_uses_the_file_stem_for_a_single_root_level_file()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("standalone.zip"), archive => archive.AddText("Tool.exe", "binary"));

        var report = new ArchiveInspector().Inspect(zip);

        Assert.Equal("Tool", report.NameHint);
    }

    [Fact]
    public void Inspect_flags_path_traversal_entries_as_fatal()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("evil.zip"), archive =>
        {
            archive.AddText("../escaped.txt", "evil");
            archive.AddText("MyApp/MyApp.exe", "binary");
        });

        var report = new ArchiveInspector().Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.PathTraversal && v.IsFatal);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\system32\\evil.exe")]
    [InlineData("\\\\server\\share\\evil.exe")]
    public void Inspect_flags_absolute_paths(string entryName)
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("abs.zip"), archive => archive.AddText(entryName, "evil"));

        var report = new ArchiveInspector().Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind is ArchiveViolationKind.AbsolutePath or ArchiveViolationKind.InvalidEntryName);
    }

    [Fact]
    public void Inspect_flags_duplicate_entries()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("dup.zip"), archive =>
        {
            archive.AddText("MyApp/data.txt", "one");
            archive.AddText("MyApp/data.txt", "two");
        });

        var report = new ArchiveInspector().Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.DuplicateEntry);
    }

    [Fact]
    public void Inspect_reports_symlinks_without_failing_the_archive()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("link.zip"), archive =>
        {
            archive.AddText("MyApp/MyApp.exe", "binary");
            archive.AddBytes("MyApp/link", System.Text.Encoding.UTF8.GetBytes("target"), ZipFixture.SymlinkAttributes);
        });

        var report = new ArchiveInspector().Inspect(zip);

        Assert.True(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.SymbolicLink && !v.IsFatal);
        Assert.Contains(report.Entries, e => e.FullName == "MyApp/link" && e.IsSymbolicLink);
    }

    [Fact]
    public void Inspect_enforces_the_entry_count_limit()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("many.zip"), archive =>
        {
            for (var i = 0; i < 10; i++)
            {
                archive.AddText($"file{i}.txt", "x");
            }
        });

        var report = new ArchiveInspector(new ArchiveLimits { MaxEntries = 3 }).Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.TooManyEntries);
    }

    [Fact]
    public void Inspect_enforces_the_compression_ratio_limit()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("bomb.zip"), archive =>
        {
            archive.AddBytes("bomb.bin", ZipFixture.Zeros(512 * 1024));
        });

        var limits = new ArchiveLimits
        {
            CompressionRatioFreeBytes = 0,
            MaxEntryCompressionRatio = 10,
            MaxCompressionRatio = 10,
        };

        var report = new ArchiveInspector(limits).Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.CompressionRatio);
    }

    [Fact]
    public void Inspect_rejects_files_that_are_not_zip_archives()
    {
        using var temp = new TestDirectory();
        var path = temp.WriteFile("not-a-zip.zip", "this is definitely not a zip archive");

        var exception = Assert.Throws<ArchiveFormatException>(() => new ArchiveInspector().Inspect(path));
        Assert.Equal(Path.GetFullPath(path), exception.ArchivePath);
    }

    [Fact]
    public void Inspect_flags_windows_reserved_device_names()
    {
        using var temp = new TestDirectory();
        var zip = ZipFixture.Write(temp.Combine("reserved.zip"), archive => archive.AddText("MyApp/CON.txt", "x"));

        var report = new ArchiveInspector().Inspect(zip);

        Assert.False(report.IsSafeToExtract);
        Assert.Contains(report.Violations, v => v.Kind == ArchiveViolationKind.InvalidEntryName);
    }
}
