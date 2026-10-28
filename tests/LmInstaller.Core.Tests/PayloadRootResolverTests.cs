using LmInstaller.Core.Pipeline;
using LmInstaller.Core.Tests.Support;

namespace LmInstaller.Core.Tests;

public sealed class PayloadRootResolverTests
{
    [Fact]
    public void Uses_the_single_top_level_folder_as_the_payload_root()
    {
        using var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", "binary");
        temp.WriteFile("MyApp/libs/core.dll", "dll");

        var (payloadRoot, mainExecutable) = PayloadRootResolver.Resolve(temp.Root, "MyApp/bin/MyApp.exe");

        Assert.Equal(Path.Combine(temp.Root, "MyApp"), payloadRoot);
        Assert.Equal("bin/MyApp.exe", mainExecutable);
    }

    [Fact]
    public void Uses_the_extraction_root_when_files_are_spread_out()
    {
        using var temp = new TestDirectory();
        temp.WriteFile("MyApp/MyApp.exe", "binary");
        temp.WriteFile("readme.txt", "docs");

        var (payloadRoot, mainExecutable) = PayloadRootResolver.Resolve(temp.Root, "MyApp/MyApp.exe");

        Assert.Equal(temp.Root, payloadRoot);
        Assert.Equal("MyApp/MyApp.exe", mainExecutable);
    }

    [Fact]
    public void Uses_the_extraction_root_for_a_root_level_executable()
    {
        using var temp = new TestDirectory();
        temp.WriteFile("MyApp.exe", "binary");

        var (payloadRoot, mainExecutable) = PayloadRootResolver.Resolve(temp.Root, "MyApp.exe");

        Assert.Equal(temp.Root, payloadRoot);
        Assert.Equal("MyApp.exe", mainExecutable);
    }
}
