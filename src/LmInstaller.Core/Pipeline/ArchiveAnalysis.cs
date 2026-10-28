using LmInstaller.Core.Archive;
using LmInstaller.Core.Installation;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Models;

namespace LmInstaller.Core.Pipeline;

/// <summary>
/// Result of analysing one archive. Holds the temporary workspace until disposed; the UI keeps it
/// alive between "analyse" and the user's confirmation, then either installs or discards it.
/// </summary>
public sealed class ArchiveAnalysis : IDisposable
{
    private bool _disposed;

    public required TempWorkspace Workspace { get; init; }

    public required string ArchivePath { get; init; }

    public required long ArchiveSizeBytes { get; init; }

    public string? ArchiveSha256 { get; init; }

    public required ArchiveInspectionReport Inspection { get; init; }

    public required ExtractionResult Extraction { get; init; }

    public required IReadOnlyList<CandidateExecutable> Candidates { get; init; }

    public required SelectionOutcome Selection { get; init; }

    /// <summary>Suggested folder name before sanitisation, e.g. the archive name.</summary>
    public required string SuggestedAppName { get; init; }

    public string ExtractionRoot => Extraction.DestinationRoot;

    public bool RequiresUserDecision => Selection.RequiresUserDecision;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Workspace.Dispose();
    }
}
