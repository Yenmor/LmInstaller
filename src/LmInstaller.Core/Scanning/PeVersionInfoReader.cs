using System.Text;
using LmInstaller.Core.Models;

namespace LmInstaller.Core.Scanning;

/// <summary>
/// Best-effort reader for the Windows version resource (RT_VERSION) of a PE file.
/// The reader is defensive: any malformed or unexpected structure results in
/// <see cref="ExecutableVersionInfo.Empty"/> instead of an exception.
/// </summary>
public static class PeVersionInfoReader
{
    private const int MaxVersionBlockBytes = 4 * 1024 * 1024;

    public static ExecutableVersionInfo TryRead(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ExecutableVersionInfo.Empty;
        }

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (!PeImageReader.TryLocateVersionResource(stream, out var offset, out var size))
            {
                return ExecutableVersionInfo.Empty;
            }

            if (size <= 0 || size > MaxVersionBlockBytes || offset < 0 || offset + size > stream.Length)
            {
                return ExecutableVersionInfo.Empty;
            }

            var buffer = new byte[size];
            stream.Seek(offset, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, size);
            if (read < size)
            {
                Array.Resize(ref buffer, read);
            }

            return PeVersionResourceParser.Parse(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ExecutableVersionInfo.Empty;
        }
    }
}

/// <summary>Locates the RT_VERSION resource inside a PE image by walking the resource directory.</summary>
internal static class PeImageReader
{
    private const int RtVersion = 16;
    private const int HighBitFlag = unchecked((int)0x80000000);

    public static bool TryLocateVersionResource(Stream stream, out long fileOffset, out int size)
    {
        fileOffset = 0;
        size = 0;

        try
        {
            if (stream.Length < 0x40 || ReadU16(stream, 0) != 0x5A4D)
            {
                return false;
            }

            var peOffset = ReadI32(stream, 0x3C);
            if (peOffset <= 0 || peOffset + 0x18 > stream.Length)
            {
                return false;
            }

            if (ReadU32(stream, peOffset) != 0x00004550)
            {
                return false;
            }

            var numberOfSections = ReadU16(stream, peOffset + 6);
            var sizeOfOptionalHeader = ReadU16(stream, peOffset + 20);
            var optionalOffset = peOffset + 24;
            if (numberOfSections <= 0 || optionalOffset + sizeOfOptionalHeader > stream.Length)
            {
                return false;
            }

            var magic = ReadU16(stream, optionalOffset);
            var pe32Plus = magic == 0x20B;
            var pe32 = magic == 0x10B;
            if (!pe32 && !pe32Plus)
            {
                return false;
            }

            var numberOfRvaAndSizes = ReadI32(stream, optionalOffset + (pe32Plus ? 108 : 92));
            if (numberOfRvaAndSizes < 3)
            {
                return false;
            }

            var resourceEntryOffset = optionalOffset + (pe32Plus ? 112 : 96) + (2 * 8);
            var resourceRva = ReadI32(stream, resourceEntryOffset);
            var resourceSize = ReadI32(stream, resourceEntryOffset + 4);
            if (resourceRva <= 0 || resourceSize <= 0)
            {
                return false;
            }

            var sections = ReadSections(stream, optionalOffset + sizeOfOptionalHeader, numberOfSections);
            if (sections.Count == 0 || !TryRvaToFileOffset(sections, resourceRva, out var resourceDirectoryOffset))
            {
                return false;
            }

            return TryTraverseResourceDirectory(stream, sections, resourceDirectoryOffset, out fileOffset, out size);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentOutOfRangeException)
        {
            fileOffset = 0;
            size = 0;
            return false;
        }
    }

    private static bool TryTraverseResourceDirectory(
        Stream stream,
        List<PeSection> sections,
        long resourceDirectoryOffset,
        out long fileOffset,
        out int size)
    {
        fileOffset = 0;
        size = 0;

        if (!TryFindEntry(stream, resourceDirectoryOffset, RtVersion, out var typeEntry))
        {
            return false;
        }

        if (!TryFindEntry(stream, resourceDirectoryOffset + typeEntry, null, out var nameEntry))
        {
            return false;
        }

        if (!TryFindEntry(stream, resourceDirectoryOffset + nameEntry, null, out var languageEntry))
        {
            return false;
        }

        var dataEntryOffset = resourceDirectoryOffset + languageEntry;
        if (dataEntryOffset + 16 > stream.Length)
        {
            return false;
        }

        var dataRva = ReadI32(stream, dataEntryOffset);
        var dataSize = ReadI32(stream, dataEntryOffset + 4);
        if (dataRva <= 0 || dataSize <= 0 || !TryRvaToFileOffset(sections, dataRva, out var offset))
        {
            return false;
        }

        fileOffset = offset;
        size = dataSize;
        return true;
    }

    private static bool TryFindEntry(Stream stream, long directoryOffset, int? wantedId, out int entryOffset)
    {
        entryOffset = 0;

        if (directoryOffset < 0 || directoryOffset + 16 > stream.Length)
        {
            return false;
        }

        var namedCount = ReadU16(stream, directoryOffset + 12);
        var idCount = ReadU16(stream, directoryOffset + 14);
        var total = namedCount + idCount;
        var entriesOffset = directoryOffset + 16;

        for (var i = 0; i < total; i++)
        {
            var record = entriesOffset + (i * 8);
            if (record + 8 > stream.Length)
            {
                return false;
            }

            var nameField = ReadU32(stream, record);
            var offsetField = ReadU32(stream, record + 4);
            if ((nameField & HighBitFlag) != 0)
            {
                // Named entry (string key); the version resource uses numeric ids.
                continue;
            }

            var id = (int)(nameField & 0x7FFFFFFF);
            if (wantedId.HasValue && id != wantedId.Value)
            {
                continue;
            }

            entryOffset = (int)(offsetField & 0x7FFFFFFF);
            return true;
        }

        return false;
    }

    private static List<PeSection> ReadSections(Stream stream, long sectionsOffset, int numberOfSections)
    {
        var sections = new List<PeSection>(numberOfSections);
        for (var i = 0; i < numberOfSections; i++)
        {
            var offset = sectionsOffset + (i * 40);
            if (offset + 40 > stream.Length)
            {
                break;
            }

            sections.Add(new PeSection(
                ReadI32(stream, offset + 8),
                ReadI32(stream, offset + 12),
                ReadI32(stream, offset + 16),
                ReadI32(stream, offset + 20)));
        }

        return sections;
    }

    private static bool TryRvaToFileOffset(List<PeSection> sections, int rva, out long fileOffset)
    {
        foreach (var section in sections)
        {
            var span = Math.Max(section.VirtualSize, section.RawSize);
            if (section.VirtualAddress <= rva && rva < section.VirtualAddress + span)
            {
                fileOffset = (long)section.RawPointer + (rva - section.VirtualAddress);
                return true;
            }
        }

        fileOffset = 0;
        return false;
    }

    private static ushort ReadU16(Stream stream, long position)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactly(stream, position, buffer);
        return BitConverter.ToUInt16(buffer);
    }

    private static uint ReadU32(Stream stream, long position)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(stream, position, buffer);
        return BitConverter.ToUInt32(buffer);
    }

    private static int ReadI32(Stream stream, long position) => unchecked((int)ReadU32(stream, position));

    private static void ReadExactly(Stream stream, long position, Span<byte> buffer)
    {
        stream.Seek(position, SeekOrigin.Begin);
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer[read..]);
            if (chunk <= 0)
            {
                throw new EndOfStreamException();
            }

            read += chunk;
        }
    }

    private readonly record struct PeSection(int VirtualSize, int VirtualAddress, int RawSize, int RawPointer);
}

/// <summary>Parses a VS_VERSIONINFO block into <see cref="ExecutableVersionInfo"/>.</summary>
internal static class PeVersionResourceParser
{
    private const int FixedFileInfoSize = 52;

    public static ExecutableVersionInfo Parse(byte[] block)
    {
        if (block is null || block.Length < 6)
        {
            return ExecutableVersionInfo.Empty;
        }

        if (!TryReadNode(block, 0, out var root) ||
            !string.Equals(root.Key, "VS_VERSION_INFO", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutableVersionInfo.Empty;
        }

        string? fileVersion = null;
        string? productVersion = null;

        if (root.ValueLengthBytes >= FixedFileInfoSize &&
            root.ValueOffset >= 0 &&
            root.ValueOffset + FixedFileInfoSize <= block.Length)
        {
            var fileVersionMs = BitConverter.ToUInt32(block, root.ValueOffset + 8);
            var fileVersionLs = BitConverter.ToUInt32(block, root.ValueOffset + 12);
            var productVersionMs = BitConverter.ToUInt32(block, root.ValueOffset + 16);
            var productVersionLs = BitConverter.ToUInt32(block, root.ValueOffset + 20);

            fileVersion = FormatVersion(fileVersionMs, fileVersionLs);
            productVersion = FormatVersion(productVersionMs, productVersionLs);
        }

        string? productName = null;
        string? fileDescription = null;
        string? companyName = null;
        string? originalFileName = null;
        string? stringFileVersion = null;
        string? stringProductVersion = null;

        foreach (var child in EnumerateChildren(block, root))
        {
            if (!string.Equals(child.Key, "StringFileInfo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var table in EnumerateChildren(block, child))
            {
                foreach (var item in EnumerateChildren(block, table))
                {
                    var value = ReadTextValue(block, item);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    switch (item.Key.ToLowerInvariant())
                    {
                        case "productname":
                            productName ??= value;
                            break;
                        case "filedescription":
                            fileDescription ??= value;
                            break;
                        case "companyname":
                            companyName ??= value;
                            break;
                        case "fileversion":
                            stringFileVersion ??= value;
                            break;
                        case "productversion":
                            stringProductVersion ??= value;
                            break;
                        case "originalfilename":
                            originalFileName ??= value;
                            break;
                    }
                }
            }
        }

        var result = new ExecutableVersionInfo(
            productName,
            fileDescription,
            companyName,
            stringFileVersion ?? fileVersion,
            stringProductVersion ?? productVersion,
            originalFileName);

        return result;
    }

    private static string FormatVersion(uint ms, uint ls)
    {
        return $"{(ms >> 16) & 0xFFFF}.{ms & 0xFFFF}.{(ls >> 16) & 0xFFFF}.{ls & 0xFFFF}";
    }

    private static IEnumerable<VersionNode> EnumerateChildren(byte[] block, VersionNode parent)
    {
        var offset = parent.ChildrenOffset;
        var end = parent.Start + parent.Length;

        while (offset + 6 <= end)
        {
            if (!TryReadNode(block, offset, out var child) || child.Length <= 0)
            {
                yield break;
            }

            yield return child;

            var next = child.Start + child.Length;
            next = Align4(next);
            if (next <= offset)
            {
                yield break;
            }

            offset = next;
        }
    }

    private static bool TryReadNode(byte[] block, int start, out VersionNode node)
    {
        node = default;

        if (start < 0 || start + 6 > block.Length)
        {
            return false;
        }

        var length = BitConverter.ToUInt16(block, start);
        var valueLength = BitConverter.ToUInt16(block, start + 2);
        var valueType = BitConverter.ToUInt16(block, start + 4);

        if (length <= 0)
        {
            return false;
        }

        var end = Math.Min(block.Length, start + length);
        if (end <= start + 6)
        {
            return false;
        }

        var position = start + 6;
        var keyStart = position;
        while (position + 1 < end && !(block[position] == 0 && block[position + 1] == 0))
        {
            position += 2;
        }

        if (position + 1 >= end)
        {
            return false;
        }

        var key = Encoding.Unicode.GetString(block, keyStart, position - keyStart);
        position += 2;
        position = Align4(position);

        var valueOffset = position;
        var valueLengthBytes = valueType == 1 ? valueLength * 2 : valueLength;
        var childrenOffset = Align4(valueOffset + valueLengthBytes);
        if (childrenOffset > end)
        {
            childrenOffset = end;
        }

        node = new VersionNode(key, start, end - start, valueOffset, valueLengthBytes, valueType, childrenOffset);
        return true;
    }

    private static string? ReadTextValue(byte[] block, VersionNode node)
    {
        if (node.ValueType != 1 || node.ValueLengthBytes <= 0)
        {
            return null;
        }

        if (node.ValueOffset < 0 || node.ValueOffset + node.ValueLengthBytes > block.Length)
        {
            return null;
        }

        var charCount = node.ValueLengthBytes / 2;
        if (charCount <= 0)
        {
            return null;
        }

        if (block[node.ValueOffset + node.ValueLengthBytes - 1] == 0 &&
            block[node.ValueOffset + node.ValueLengthBytes - 2] == 0)
        {
            charCount--;
        }

        if (charCount <= 0)
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(block, node.ValueOffset, charCount * 2);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private readonly record struct VersionNode(
        string Key,
        int Start,
        int Length,
        int ValueOffset,
        int ValueLengthBytes,
        int ValueType,
        int ChildrenOffset);
}
