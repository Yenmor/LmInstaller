using System.Text;

namespace LmInstaller.Core.Tests.Support;

/// <summary>
/// Builds a VS_VERSIONINFO resource block so the parser can be tested without shipping a real
/// Windows executable in the repository.
/// </summary>
internal static class VersionBlockBuilder
{
    public static byte[] Build(
        IReadOnlyDictionary<string, string> strings,
        uint fileVersionMs = 0x00010002,
        uint fileVersionLs = 0x00030004)
    {
        var fixedFileInfo = new byte[52];
        BitConverter.GetBytes(0xFEEF04BDu).CopyTo(fixedFileInfo, 0);
        BitConverter.GetBytes(0x00010000u).CopyTo(fixedFileInfo, 4);
        BitConverter.GetBytes(fileVersionMs).CopyTo(fixedFileInfo, 8);
        BitConverter.GetBytes(fileVersionLs).CopyTo(fixedFileInfo, 12);
        BitConverter.GetBytes(fileVersionMs).CopyTo(fixedFileInfo, 16);
        BitConverter.GetBytes(fileVersionLs).CopyTo(fixedFileInfo, 20);

        var stringChildren = new List<byte>();
        foreach (var (key, value) in strings)
        {
            stringChildren.AddRange(BuildNode(key, (ushort)(value.Length + 1), 1, Encoding.Unicode.GetBytes(value + "\0"), Array.Empty<byte>()));
        }

        var stringTable = BuildNode("040904B0", 0, 1, Array.Empty<byte>(), stringChildren.ToArray());
        var stringFileInfo = BuildNode("StringFileInfo", 0, 1, Array.Empty<byte>(), stringTable);

        return BuildNode("VS_VERSION_INFO", 52, 0, fixedFileInfo, stringFileInfo);
    }

    private static byte[] BuildNode(string key, ushort valueLength, ushort type, byte[] value, byte[] children)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write(valueLength);
        writer.Write(type);
        writer.Write(Encoding.Unicode.GetBytes(key));
        writer.Write((ushort)0);

        while (stream.Length % 4 != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write(value);
        writer.Write(children);

        var unpaddedLength = (ushort)stream.Length;
        while (stream.Length % 4 != 0)
        {
            writer.Write((byte)0);
        }

        var bytes = stream.ToArray();
        BitConverter.GetBytes(unpaddedLength).CopyTo(bytes, 0);
        return bytes;
    }
}
