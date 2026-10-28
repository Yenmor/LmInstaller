using System.Security.Cryptography;
using LmInstaller.Core.Archive;
using LmInstaller.Core.Installation;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Models;
using LmInstaller.Core.Scanning;

namespace LmInstaller.Core.Pipeline;

/// <summary>
/// The core install flow shared by the main window and any desktop entry point:
/// inspect the archive, extract it safely into a temporary workspace, scan candidates,
/// ask the LLM (with rule-based fallback), then copy the confirmed payload into the app root.
/// </summary>
public sealed class InstallerPipeline
{
    private readonly ArchiveInspector _inspector;
    private readonly SafeArchiveExtractor _extractor;
    private readonly ExecutableCandidateScanner _scanner;
    private readonly IMainExecutableSelector _selector;
    private readonly ITempWorkspaceFactory _workspaceFactory;
    private readonly AppInstaller? _installer;

    public InstallerPipeline(
        ArchiveInspector? inspector = null,
        SafeArchiveExtractor? extractor = null,
        ExecutableCandidateScanner? scanner = null,
        IMainExecutableSelector? selector = null,
        ITempWorkspaceFactory? workspaceFactory = null,
        AppInstaller? installer = null)
    {
        _inspector = inspector ?? new ArchiveInspector();
        _extractor = extractor ?? new SafeArchiveExtractor();
        _scanner = scanner ?? new ExecutableCandidateScanner();
        _selector = selector ?? new MainExecutableSelector();
        _workspaceFactory = workspaceFactory ?? new TempWorkspaceFactory();
        _installer = installer;
    }

    public async Task<ArchiveAnalysis> AnalyzeAsync(
        string archivePath,
        bool computeHash = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException("找不到压缩包文件。", fullArchivePath);
        }

        var inspection = await Task.Run(() => _inspector.Inspect(fullArchivePath), cancellationToken)
            .ConfigureAwait(false);

        if (!inspection.IsSafeToExtract)
        {
            throw new UnsafeArchiveException(fullArchivePath, inspection.Violations);
        }

        var workspace = _workspaceFactory.Create();
        try
        {
            var extractionRoot = workspace.CreateSubdirectory("extracted");
            var extraction = await Task.Run(
                () => _extractor.Extract(fullArchivePath, extractionRoot),
                cancellationToken).ConfigureAwait(false);

            var candidates = await Task.Run(
                () => _scanner.Scan(extractionRoot, inspection.NameHint),
                cancellationToken).ConfigureAwait(false);

            var context = new SelectionContext(extractionRoot, candidates, inspection.NameHint);
            var selection = await _selector.SelectAsync(context, cancellationToken).ConfigureAwait(false);

            string? hash = null;
            if (computeHash)
            {
                hash = await Task.Run(() => ComputeSha256(fullArchivePath), cancellationToken).ConfigureAwait(false);
            }

            return new ArchiveAnalysis
            {
                Workspace = workspace,
                ArchivePath = fullArchivePath,
                ArchiveSizeBytes = inspection.ArchiveSizeBytes,
                ArchiveSha256 = hash,
                Inspection = inspection,
                Extraction = extraction,
                Candidates = candidates,
                Selection = selection,
                SuggestedAppName = inspection.NameHint is { Length: > 0 } hint
                    ? hint
                    : Path.GetFileNameWithoutExtension(fullArchivePath),
            };
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Copies the confirmed payload into the application root and writes the directory metadata.
    /// </summary>
    public InstallResult Install(
        ArchiveAnalysis analysis,
        MainExecutableSelection selection,
        string appRootPath,
        InstallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(selection);

        if (_installer is null)
        {
            throw new InstallException("未提供 AppInstaller，无法执行安装。");
        }

        var candidate = analysis.Candidates.FirstOrDefault(
            c => string.Equals(c.RelativePath, selection.RelativePath, StringComparison.OrdinalIgnoreCase));

        if (candidate is null)
        {
            throw new InstallException("所选主程序不在分析结果中：" + selection.RelativePath);
        }

        var (payloadRoot, mainExecutableRelativeToPayload) = PayloadRootResolver.Resolve(
            analysis.ExtractionRoot,
            candidate.RelativePath);

        var request = new InstallRequest
        {
            AppRootPath = appRootPath,
            SourceDirectory = payloadRoot,
            MainExecutableRelativePath = mainExecutableRelativeToPayload,
            ProposedName = selection.SuggestedName ?? analysis.SuggestedAppName,
            SourceArchivePath = analysis.ArchivePath,
            SourceArchiveSha256 = analysis.ArchiveSha256,
            VersionInfo = candidate.VersionInfo,
            Options = options ?? InstallOptions.Default,
        };

        return _installer.Install(request);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
