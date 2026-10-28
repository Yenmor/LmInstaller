using LmInstaller.Core.Scanning;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class PeVersionInfoReaderTests
{
    [Fact]
    public void TryRead_returns_empty_for_a_missing_file()
    {
        using var temp = new TestDirectory();

        var info = PeVersionInfoReader.TryRead(temp.Combine("missing.exe"));

        Assert.True(info.IsEmpty);
    }

    [Fact]
    public void TryRead_returns_empty_for_a_text_file()
    {
        using var temp = new TestDirectory();
        var path = temp.WriteFile("not-an-exe.exe", "plain text, not a PE image");

        var info = PeVersionInfoReader.TryRead(path);

        Assert.True(info.IsEmpty);
    }

    [Fact]
    public void TryRead_returns_empty_for_a_truncated_pe_header()
    {
        using var temp = new TestDirectory();
        var path = temp.Combine("truncated.exe");
        var bytes = new byte[64];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x1000).CopyTo(bytes, 0x3C);
        File.WriteAllBytes(path, bytes);

        var info = PeVersionInfoReader.TryRead(path);

        Assert.True(info.IsEmpty);
    }

    [Fact]
    public void TryRead_returns_empty_when_the_pe_signature_is_missing()
    {
        using var temp = new TestDirectory();
        var path = temp.Combine("bad-signature.exe");
        var bytes = new byte[256];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        File.WriteAllBytes(path, bytes);

        var info = PeVersionInfoReader.TryRead(path);

        Assert.True(info.IsEmpty);
    }
}
