using System.IO.Compression;

namespace LmInstaller.Core.Tests.Support;

internal static class ZipFixture
{
    public static string Write(string zipPath, Action<ZipArchive> build)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        build(archive);
        return zipPath;
    }

    public static void AddText(this ZipArchive archive, string name, string content) =>
        AddBytes(archive, name, System.Text.Encoding.UTF8.GetBytes(content));

    public static void AddBytes(this ZipArchive archive, string name, byte[] content, int? unixExternalAttributes = null)
    {
        var entry = archive.CreateEntry(name);
        if (unixExternalAttributes is not null)
        {
            entry.ExternalAttributes = unixExternalAttributes.Value;
        }

        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    public static void AddDirectory(this ZipArchive archive, string name)
    {
        var normalized = name.EndsWith('/') ? name : name + "/";
        archive.CreateEntry(normalized);
    }

    /// <summary>External attributes for a Unix symbolic link entry.</summary>
    public static int SymlinkAttributes => unchecked((int)((0xA1FFu) << 16));

    public static byte[] Zeros(int count) => new byte[count];
}
