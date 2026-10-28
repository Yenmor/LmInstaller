using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class PeVersionResourceParserTests
{
    [Fact]
    public void Parse_reads_string_table_and_fixed_file_info()
    {
        var block = VersionBlockBuilder.Build(new Dictionary<string, string>
        {
            ["ProductName"] = "示例应用",
            ["FileDescription"] = "Example Application",
            ["CompanyName"] = "Example Corp",
            ["OriginalFilename"] = "MyApp.exe",
        });

        var info = PeVersionResourceParser.Parse(block);

        Assert.Equal("示例应用", info.ProductName);
        Assert.Equal("Example Application", info.FileDescription);
        Assert.Equal("Example Corp", info.CompanyName);
        Assert.Equal("MyApp.exe", info.OriginalFileName);
        Assert.Equal("1.2.3.4", info.FileVersion);
        Assert.Equal("1.2.3.4", info.ProductVersion);
        Assert.Equal("示例应用", info.BestDisplayName);
    }

    [Fact]
    public void Parse_prefers_the_string_table_file_version()
    {
        var block = VersionBlockBuilder.Build(new Dictionary<string, string>
        {
            ["ProductName"] = "App",
            ["FileVersion"] = "9.9.9.9",
        });

        var info = PeVersionResourceParser.Parse(block);

        Assert.Equal("9.9.9.9", info.FileVersion);
    }

    [Fact]
    public void Parse_returns_empty_for_random_bytes()
    {
        var info = PeVersionResourceParser.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        Assert.True(info.IsEmpty);
    }

    [Fact]
    public void Parse_returns_empty_for_a_truncated_block()
    {
        var block = VersionBlockBuilder.Build(new Dictionary<string, string> { ["ProductName"] = "App" });
        var truncated = block[..Math.Min(20, block.Length)];

        var info = PeVersionResourceParser.Parse(truncated);

        Assert.True(info.IsEmpty);
    }
}
