namespace LmInstaller.Core.Models;

/// <summary>
/// Best-effort version resource data read from a Windows PE file. Every field is optional:
/// the reader returns <see cref="Empty"/> when the information cannot be extracted.
/// </summary>
public sealed record ExecutableVersionInfo(
    string? ProductName = null,
    string? FileDescription = null,
    string? CompanyName = null,
    string? FileVersion = null,
    string? ProductVersion = null,
    string? OriginalFileName = null)
{
    public static readonly ExecutableVersionInfo Empty = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ProductName) &&
        string.IsNullOrWhiteSpace(FileDescription) &&
        string.IsNullOrWhiteSpace(CompanyName) &&
        string.IsNullOrWhiteSpace(FileVersion) &&
        string.IsNullOrWhiteSpace(ProductVersion) &&
        string.IsNullOrWhiteSpace(OriginalFileName);

    /// <summary>Human-friendly name, preferring the product name over the internal file description.</summary>
    public string? BestDisplayName
    {
        get
        {
            foreach (var candidate in new[] { ProductName, FileDescription })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }

            return null;
        }
    }
}
