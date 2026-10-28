using System.Text;
using System.Text.Json;
using LmInstaller.Core.Archive;
using LmInstaller.Core.Models;

namespace LmInstaller.Core.Llm;

public sealed record WorkspaceToolResult(string Content, bool IsError = false);

/// <summary>
/// Read-only tools exposed to the LLM. Every path argument is resolved inside the temporary
/// workspace; the model can never reach the rest of the file system, and nothing is executed.
/// </summary>
public interface IWorkspaceToolHost
{
    IReadOnlyList<LlmToolDefinition> Tools { get; }

    Task<WorkspaceToolResult> InvokeAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default);
}

/// <summary>Creates the read-only tool host for a concrete analysis workspace.</summary>
public interface IWorkspaceToolHostFactory
{
    IWorkspaceToolHost Create(SelectionContext context);
}

/// <summary>Default factory: exposes the extracted files plus the already-scanned candidates.</summary>
public sealed class ReadOnlyWorkspaceToolHostFactory : IWorkspaceToolHostFactory
{
    private readonly WorkspaceToolLimits _limits;

    public ReadOnlyWorkspaceToolHostFactory(WorkspaceToolLimits? limits = null)
    {
        _limits = limits ?? WorkspaceToolLimits.Default;
    }

    public IWorkspaceToolHost Create(SelectionContext context) =>
        new ReadOnlyWorkspaceToolHost(context.ExtractionRoot, context.Candidates, _limits);
}

public sealed record WorkspaceToolLimits
{
    public static readonly WorkspaceToolLimits Default = new();

    public int MaxDirectoryEntries { get; init; } = 400;

    public int MaxTextBytes { get; init; } = 8_192;

    public int MaxFindResults { get; init; } = 200;

    public int MaxTextFileBytes { get; init; } = 2 * 1024 * 1024;
}

public sealed class ReadOnlyWorkspaceToolHost : IWorkspaceToolHost
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".xml", ".ini", ".cfg", ".conf", ".log", ".nfo",
        ".url", ".html", ".htm", ".csv", ".tsv", ".yml", ".yaml", ".toml", ".bat", ".cmd",
        ".ps1", ".sh", ".reg", ".properties", ".version", ".manifest",
    };

    private readonly string _root;
    private readonly IReadOnlyList<CandidateExecutable> _candidates;
    private readonly WorkspaceToolLimits _limits;

    public ReadOnlyWorkspaceToolHost(
        string workspaceRoot,
        IReadOnlyList<CandidateExecutable>? candidates = null,
        WorkspaceToolLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _candidates = candidates ?? Array.Empty<CandidateExecutable>();
        _limits = limits ?? WorkspaceToolLimits.Default;
    }

    public IReadOnlyList<LlmToolDefinition> Tools { get; } = new[]
    {
        new LlmToolDefinition(
            "list_directory",
            "列出解压目录中某个相对路径下的文件与子目录。path 省略或留空表示根目录。只读操作。",
            """
            {"type":"object","properties":{"path":{"type":"string","description":"相对于解压根目录的路径，可为空"}},"required":[]}
            """),
        new LlmToolDefinition(
            "read_text_file",
            "读取解压目录中某个文本文件的开头内容（例如 readme、说明、配置）。二进制文件会被拒绝。",
            """
            {"type":"object","properties":{"path":{"type":"string","description":"相对于解压根目录的文件路径"},"maxBytes":{"type":"integer","description":"最多读取的字节数"}},"required":["path"]}
            """),
        new LlmToolDefinition(
            "get_file_info",
            "获取解压目录中某个文件的大小、时间以及（如果是可执行文件）版本信息。",
            """
            {"type":"object","properties":{"path":{"type":"string","description":"相对于解压根目录的文件路径"}},"required":["path"]}
            """),
        new LlmToolDefinition(
            "find_executables",
            "在已扫描到的候选可执行文件中按名称或扩展名过滤，返回相对路径与规则评分。",
            """
            {"type":"object","properties":{"nameContains":{"type":"string","description":"文件名包含的文本，可为空"},"extension":{"type":"string","description":"扩展名，例如 .exe，可为空"},"limit":{"type":"integer","description":"最多返回条数"}},"required":[]}
            """),
    };

    public Task<WorkspaceToolResult> InvokeAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var arguments = document.RootElement;

            var result = toolName switch
            {
                "list_directory" => ListDirectory(GetString(arguments, "path")),
                "read_text_file" => ReadTextFile(GetString(arguments, "path"), GetInt(arguments, "maxBytes")),
                "get_file_info" => GetFileInfo(GetString(arguments, "path")),
                "find_executables" => FindExecutables(
                    GetString(arguments, "nameContains"),
                    GetString(arguments, "extension"),
                    GetInt(arguments, "limit")),
                _ => Error($"未知工具：{toolName}"),
            };

            return Task.FromResult(result);
        }
        catch (JsonException)
        {
            return Task.FromResult(Error("工具参数不是有效的 JSON。"));
        }
    }

    private WorkspaceToolResult ListDirectory(string? relativePath)
    {
        if (!TryResolve(relativePath, out var fullPath, out var normalized, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(fullPath))
        {
            return Error($"目录不存在或不是目录：{normalized}");
        }

        var entries = new List<object>();
        var truncated = false;

        foreach (var directory in Directory.EnumerateDirectories(fullPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count >= _limits.MaxDirectoryEntries)
            {
                truncated = true;
                break;
            }

            entries.Add(new { name = Path.GetFileName(directory), type = "directory" });
        }

        if (!truncated)
        {
            foreach (var file in Directory.EnumerateFiles(fullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (entries.Count >= _limits.MaxDirectoryEntries)
                {
                    truncated = true;
                    break;
                }

                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Size stays 0 when the file cannot be inspected.
                }

                entries.Add(new { name = Path.GetFileName(file), type = "file", size });
            }
        }

        return Json(new { path = normalized, truncated, entries });
    }

    private WorkspaceToolResult ReadTextFile(string? relativePath, int requestedBytes)
    {
        if (!TryResolve(relativePath, out var fullPath, out var normalized, out var error))
        {
            return error!;
        }

        if (!File.Exists(fullPath))
        {
            return Error($"文件不存在：{normalized}");
        }

        var extension = Path.GetExtension(fullPath);
        if (extension.Length > 0 && !TextExtensions.Contains(extension))
        {
            return Error($"拒绝读取非文本文件（{extension}）：{normalized}");
        }

        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"无法读取文件信息：{normalized}");
        }

        if (size > _limits.MaxTextFileBytes)
        {
            return Error($"文件过大，不予读取（{size} 字节）：{normalized}");
        }

        var maxBytes = requestedBytes <= 0
            ? _limits.MaxTextBytes
            : Math.Min(requestedBytes, _limits.MaxTextBytes);

        byte[] buffer;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)Math.Min(size, maxBytes);
            buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            if (read < buffer.Length)
            {
                Array.Resize(ref buffer, read);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"无法读取文件：{normalized}");
        }

        if (buffer.AsSpan().IndexOf((byte)0) >= 0)
        {
            return Error($"疑似二进制文件，不予读取：{normalized}");
        }

        var text = Encoding.UTF8.GetString(buffer);
        return Json(new
        {
            path = normalized,
            truncated = size > buffer.Length,
            size,
            content = text,
        });
    }

    private WorkspaceToolResult GetFileInfo(string? relativePath)
    {
        if (!TryResolve(relativePath, out var fullPath, out var normalized, out var error))
        {
            return error!;
        }

        if (!File.Exists(fullPath))
        {
            return Error($"文件不存在：{normalized}");
        }

        long size = 0;
        DateTimeOffset? lastWrite = null;
        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
            lastWrite = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported as null/0 below.
        }

        var candidate = _candidates.FirstOrDefault(
            c => string.Equals(c.RelativePath, normalized, StringComparison.OrdinalIgnoreCase));

        return Json(new
        {
            path = normalized,
            size,
            lastWriteUtc = lastWrite,
            isCandidate = candidate is not null,
            ruleScore = candidate?.RuleScore,
            ruleNotes = candidate?.RuleNotes,
            productName = candidate?.VersionInfo.ProductName,
            fileDescription = candidate?.VersionInfo.FileDescription,
            companyName = candidate?.VersionInfo.CompanyName,
            fileVersion = candidate?.VersionInfo.FileVersion,
        });
    }

    private WorkspaceToolResult FindExecutables(string? nameContains, string? extension, int requestedLimit)
    {
        var limit = requestedLimit <= 0 ? _limits.MaxFindResults : Math.Min(requestedLimit, _limits.MaxFindResults);
        var query = _candidates.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            query = query.Where(c => c.FileName.Contains(nameContains.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(extension))
        {
            var normalizedExtension = extension.Trim();
            if (!normalizedExtension.StartsWith('.'))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            query = query.Where(c => c.FileName.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase));
        }

        var results = query
            .OrderByDescending(c => c.RuleScore)
            .Take(limit)
            .Select(c => new
            {
                path = c.RelativePath,
                size = c.SizeBytes,
                kind = c.Kind.ToString(),
                ruleScore = c.RuleScore,
                ruleNotes = c.RuleNotes,
                productName = c.VersionInfo.ProductName,
                fileDescription = c.VersionInfo.FileDescription,
                fileVersion = c.VersionInfo.FileVersion,
            })
            .ToList();

        return Json(new { count = results.Count, results });
    }

    private bool TryResolve(
        string? relativePath,
        out string fullPath,
        out string normalized,
        out WorkspaceToolResult? error)
    {
        fullPath = string.Empty;
        normalized = string.Empty;
        error = null;

        var candidate = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
        if (candidate.Contains('\0'))
        {
            error = Error("路径包含非法字符。");
            return false;
        }

        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/'))
        {
            error = Error("只允许使用解压目录内的相对路径。");
            return false;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                error = Error("路径不能跳出解压目录。");
                return false;
            }

            cleaned.Add(segment);
        }

        normalized = string.Join('/', cleaned);
        var combined = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!ArchivePathSafety.IsWithinRoot(_root, combined))
        {
            error = Error("路径不能跳出解压目录。");
            return false;
        }

        fullPath = combined;
        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static WorkspaceToolResult Json(object value) =>
        new(System.Text.Json.JsonSerializer.Serialize(value));

    private static WorkspaceToolResult Error(string message) =>
        new(System.Text.Json.JsonSerializer.Serialize(new { error = message }), IsError: true);
}
