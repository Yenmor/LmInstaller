using LmInstaller.Core.Installation;

namespace LmInstaller.Core.Tests;

public sealed class WindowsPathSanitizerTests
{
    [Theory]
    [InlineData("MyApp", "MyApp")]
    [InlineData("My App 1.2.3", "My App 1.2.3")]
    [InlineData("bad:name?", "bad name")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("trailing...", "trailing")]
    [InlineData("a\\b/c", "a b c")]
    public void Produces_a_usable_folder_name(string input, string expected)
    {
        Assert.Equal(expected, WindowsPathSanitizer.ToSafeFolderName(input));
    }

    [Fact]
    public void Falls_back_for_an_empty_name()
    {
        Assert.Equal("App", WindowsPathSanitizer.ToSafeFolderName("   "));
        Assert.Equal("Package", WindowsPathSanitizer.ToSafeFolderName("?", "Package"));
    }

    [Fact]
    public void Prefixes_reserved_device_names()
    {
        Assert.Equal("_CON", WindowsPathSanitizer.ToSafeFolderName("CON"));
        Assert.Equal("_NUL", WindowsPathSanitizer.ToSafeFolderName("NUL"));
        Assert.True(WindowsPathSanitizer.IsReservedDeviceName("com1.txt"));
    }

    [Fact]
    public void Truncates_to_the_requested_length()
    {
        var name = WindowsPathSanitizer.ToSafeFolderName(new string('x', 200), maxLength: 10);

        Assert.Equal(10, name.Length);
    }
}
