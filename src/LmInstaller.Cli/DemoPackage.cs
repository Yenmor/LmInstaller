using System.IO.Compression;
using System.Text;

namespace LmInstaller.Cli;

/// <summary>
/// Builds a small sample ZIP so the whole pipeline can be exercised without downloading anything.
/// The ".exe" files are placeholders (text), so no version information is read from them and
/// nothing is ever executed.
/// </summary>
internal static class DemoPackage
{
    public static string Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "MyApp-1.2.3.zip");

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        Add(archive, "MyApp/MyApp.exe", new string('M', 120_000));
        Add(archive, "MyApp/updater.exe", "updater placeholder");
        Add(archive, "MyApp/unins000.exe", "uninstaller placeholder");
        Add(archive, "MyApp/helper.exe", "helper placeholder");
        Add(archive, "MyApp/libs/core.dll", "library placeholder");
        Add(archive, "MyApp/readme.txt", "这是演示用的示例压缩包，文件内容都是占位文本。\n");

        return path;
    }

    /// <summary>Builds an archive that tries to escape the extraction directory.</summary>
    public static string CreateUnsafe(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "EvilApp.zip");

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Add(archive, "../escaped.txt", "这个文件不应该被写到解压目录之外");
        Add(archive, "MyApp/MyApp.exe", "placeholder");
        return path;
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
