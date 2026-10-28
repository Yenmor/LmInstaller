namespace LmInstaller.Core.Installation;

/// <summary>How to react when the target folder already exists.</summary>
public enum ConflictPolicy
{
    /// <summary>Stop and let the caller ask the user.</summary>
    Fail,

    /// <summary>Install next to the existing folder using a numbered suffix.</summary>
    UseNewName,

    /// <summary>Replace the existing folder. Requires <see cref="InstallOptions.ConfirmOverwrite"/>.</summary>
    Overwrite,
}

public sealed record InstallOptions
{
    public static readonly InstallOptions Default = new();

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Fail;

    /// <summary>Must be set to true for <see cref="ConflictPolicy.Overwrite"/>; otherwise the install stops.</summary>
    public bool ConfirmOverwrite { get; init; }

    /// <summary>Overrides the folder/product name derived from the archive.</summary>
    public string? DisplayNameOverride { get; init; }

    public int MaxFolderNameLength { get; init; } = 80;
}
