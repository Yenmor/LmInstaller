namespace LmInstaller.Core.Installation;

/// <summary>
/// Turns arbitrary archive, product or user-provided text into a folder name that is valid on
/// Windows. Reserved device names and trailing dots/spaces are rewritten instead of failing.
/// </summary>
public static class WindowsPathSanitizer
{
    private static readonly char[] InvalidCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string ToSafeFolderName(string? candidate, string fallback = "App", int maxLength = 80)
    {
        if (maxLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        var builder = new System.Text.StringBuilder(candidate.Length);
        foreach (var character in candidate.Trim())
        {
            if (character < ' ' || char.IsControl(character))
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(InvalidCharacters.Contains(character) ? ' ' : character);
        }

        var name = builder.ToString();

        // Collapse whitespace runs introduced above.
        name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        name = name.Trim(' ', '.');
        if (name.Length == 0)
        {
            return fallback;
        }

        if (maxLength > 0 && name.Length > maxLength)
        {
            name = name[..maxLength].Trim(' ', '.');
        }

        if (name.Length == 0)
        {
            return fallback;
        }

        if (IsReservedDeviceName(name))
        {
            name = "_" + name;
        }

        return name;
    }

    public static bool IsReservedDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return ReservedDeviceNames.Contains(name) || ReservedDeviceNames.Contains(stem);
    }
}
