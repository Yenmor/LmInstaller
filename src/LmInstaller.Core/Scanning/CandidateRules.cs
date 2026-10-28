using LmInstaller.Core.Models;

namespace LmInstaller.Core.Scanning;

/// <summary>
/// Deterministic heuristics used to rank candidates. The LLM is the primary decision maker;
/// these rules provide the fallback ordering and hints when no LLM is available.
/// </summary>
internal static class CandidateRules
{
    private static readonly Dictionary<string, CandidateKind> ExtensionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = CandidateKind.NativeExecutable,
        [".com"] = CandidateKind.DosExecutable,
        [".bat"] = CandidateKind.CommandScript,
        [".cmd"] = CandidateKind.CommandScript,
        [".ps1"] = CandidateKind.PowerShellScript,
        [".vbs"] = CandidateKind.VisualBasicScript,
        [".jar"] = CandidateKind.JavaArchive,
    };

    private static readonly (string Token, int Delta, string Note)[] NamePenalties =
    {
        ("unins", -120, "疑似卸载程序"),
        ("uninstall", -120, "疑似卸载程序"),
        ("卸载", -120, "疑似卸载程序"),
        ("crashpad", -90, "疑似崩溃报告组件"),
        ("crashreport", -90, "疑似崩溃报告组件"),
        ("bugreport", -80, "疑似问题反馈组件"),
        ("sentry", -70, "疑似崩溃上报组件"),
        ("vcredist", -120, "疑似运行库安装包"),
        ("redist", -90, "疑似运行库安装包"),
        ("setup", -70, "疑似安装程序"),
        ("install", -55, "疑似安装程序"),
        ("updater", -60, "疑似更新程序"),
        ("update", -45, "疑似更新程序"),
        ("patch", -40, "疑似补丁工具"),
        ("crash", -60, "疑似崩溃处理程序"),
        ("report", -40, "疑似问题报告程序"),
        ("debug", -40, "疑似调试程序"),
        ("sample", -50, "疑似示例程序"),
        ("demo", -40, "疑似示例程序"),
        ("readme", -120, "疑似说明文档"),
        ("license", -120, "疑似许可文件"),
        ("eula", -110, "疑似许可文件"),
        ("changelog", -100, "疑似更新日志"),
        ("help", -70, "疑似帮助程序"),
        ("dotnet", -70, "疑似 .NET 运行时组件"),
        ("runtime", -50, "疑似运行时组件"),
        ("service", -50, "疑似后台服务"),
        ("daemon", -40, "疑似后台服务"),
        ("server", -25, "疑似服务端程序"),
        ("helper", -25, "疑似辅助程序"),
        ("console", -15, "疑似命令行程序"),
        ("test", -35, "疑似测试程序"),
        ("tool", -15, "疑似辅助工具"),
        ("cli", -10, "疑似命令行入口"),
    };

    private static readonly HashSet<string> LaunchLikeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "launcher", "start", "startup", "main", "app", "run", "application", "runapp",
    };

    private static readonly string[] SkippedDirectoryNames =
    {
        "__MACOSX", ".git", ".svn", ".hg",
    };

    public static bool IsCandidateExtension(string extension) => ExtensionKinds.ContainsKey(extension);

    public static CandidateKind ClassifyKind(string extension) =>
        ExtensionKinds.TryGetValue(extension, out var kind) ? kind : CandidateKind.Other;

    public static bool ShouldSkipDirectory(string directoryName) =>
        SkippedDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase);

    public static (int Score, List<string> Notes) Score(CandidateScoreInput input)
    {
        var notes = new List<string>();
        var score = 0;

        if (input.Kind is CandidateKind.CommandScript or CandidateKind.PowerShellScript or
            CandidateKind.VisualBasicScript or CandidateKind.JavaArchive)
        {
            score -= 30;
            notes.Add("脚本/非原生入口，优先级低于 .exe");
        }

        var lowerName = Path.GetFileNameWithoutExtension(input.FileName).ToLowerInvariant();
        foreach (var (token, delta, note) in NamePenalties)
        {
            if (lowerName.Contains(token, StringComparison.Ordinal))
            {
                score += delta;
                notes.Add(note);
            }
        }

        if (LaunchLikeNames.Contains(lowerName))
        {
            score += 25;
            notes.Add("名称类似启动器");
        }

        if (input.ParentDirectoryName.Length > 0 &&
            string.Equals(lowerName, input.ParentDirectoryName.ToLowerInvariant(), StringComparison.Ordinal))
        {
            score += 60;
            notes.Add("文件名与所在目录同名");
        }

        if (!string.IsNullOrWhiteSpace(input.NameHint))
        {
            var hint = input.NameHint.Trim();
            if (string.Equals(lowerName, hint.ToLowerInvariant(), StringComparison.Ordinal))
            {
                score += 40;
                notes.Add("文件名与压缩包/目录名称一致");
            }
        }

        if (input.Depth == 0)
        {
            score += 25;
            notes.Add("位于解压根目录");
        }
        else if (input.Depth == 1)
        {
            score += 10;
        }

        var displayName = input.VersionInfo.BestDisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            score += 15;
            notes.Add("包含产品信息：" + displayName);
        }

        if (!string.IsNullOrWhiteSpace(input.VersionInfo.CompanyName))
        {
            score += 5;
        }

        if (input.SizeBytes >= 2_000_000)
        {
            score += 15;
        }
        else if (input.SizeBytes >= 200_000)
        {
            score += 8;
        }

        return (score, notes);
    }
}

internal readonly record struct CandidateScoreInput(
    string FileName,
    string ParentDirectoryName,
    CandidateKind Kind,
    long SizeBytes,
    int Depth,
    string? NameHint,
    ExecutableVersionInfo VersionInfo);
