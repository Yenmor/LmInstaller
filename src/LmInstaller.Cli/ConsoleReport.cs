using LmInstaller.Core.Archive;
using LmInstaller.Core.Installation;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Manifest;
using LmInstaller.Core.Models;

namespace LmInstaller.Cli;

/// <summary>Console formatting kept in one place so the commands stay readable.</summary>
internal static class ConsoleReport
{
    public static void AnalysisHeader(string archivePath)
    {
        Console.WriteLine("=== 分析压缩包 ===");
        Console.WriteLine($"文件：{archivePath}");
    }

    public static void Inspection(ArchiveInspectionReport report)
    {
        Console.WriteLine($"条目数：{report.Entries.Count}    解压后大小：{FormatSize(report.TotalUncompressedBytes)}    名称提示：{report.NameHint ?? "(无)"}");

        var fatal = report.Violations.Where(v => v.IsFatal).ToList();
        var warnings = report.Violations.Where(v => !v.IsFatal).ToList();

        foreach (var violation in fatal)
        {
            Console.WriteLine($"  [拦截] {violation.Message}");
        }

        foreach (var violation in warnings)
        {
            Console.WriteLine($"  [提示] {violation.Message}");
        }

        Console.WriteLine(fatal.Count == 0 ? "安全检查：通过" : $"安全检查：发现 {fatal.Count} 个致命问题");
        Console.WriteLine();
    }

    public static void Candidates(IReadOnlyList<CandidateExecutable> candidates)
    {
        Console.WriteLine($"=== 候选可执行文件（{candidates.Count} 个，按规则评分排序）===");
        if (candidates.Count == 0)
        {
            Console.WriteLine("（没有找到候选可执行文件）");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"{"#",3}  {"评分",6}  {"大小",10}  {"路径",-42} 说明");
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var note = candidate.RuleNotes.Count > 0
                ? string.Join("；", candidate.RuleNotes.Take(2))
                : string.Empty;
            if (!candidate.VersionInfo.IsEmpty)
            {
                var product = candidate.VersionInfo.BestDisplayName;
                if (!string.IsNullOrWhiteSpace(product))
                {
                    note = string.IsNullOrEmpty(note) ? product : note + " | " + product;
                }
            }

            Console.WriteLine(
                $"{i + 1,3}  {candidate.RuleScore,6}  {FormatSize(candidate.SizeBytes),10}  {Truncate(candidate.RelativePath, 42),-42} {Truncate(note, 40)}");
        }

        Console.WriteLine();
    }

    public static void Selection(SelectionOutcome outcome)
    {
        Console.WriteLine("=== 主程序判断 ===");

        if (outcome.Selection is not null)
        {
            Console.WriteLine($"来源：{DescribeSource(outcome.Selection.Source)}    置信度：{outcome.Selection.Confidence:0.00}");
            Console.WriteLine($"选择：{outcome.Selection.RelativePath}");
            if (!string.IsNullOrWhiteSpace(outcome.Selection.Reason))
            {
                Console.WriteLine($"理由：{outcome.Selection.Reason}");
            }
        }
        else
        {
            Console.WriteLine($"未能自动确认：{outcome.Diagnosis}");
            if (outcome.SuggestedSelection is not null)
            {
                Console.WriteLine($"规则建议：{outcome.SuggestedSelection.RelativePath}");
                if (!string.IsNullOrWhiteSpace(outcome.SuggestedSelection.Reason))
                {
                    Console.WriteLine($"建议理由：{outcome.SuggestedSelection.Reason}");
                }
            }
        }

        Console.WriteLine();
    }

    public static void InstallResult(InstallResult result, string appRoot)
    {
        Console.WriteLine("=== 安装完成 ===");
        Console.WriteLine($"应用名称：{result.Record.Name}");
        Console.WriteLine($"安装目录：{result.InstallDirectory}");
        Console.WriteLine($"主程序：  {result.MainExecutablePath}");
        if (!string.IsNullOrWhiteSpace(result.Record.Version))
        {
            Console.WriteLine($"版本：    {result.Record.Version}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"  [提示] {warning}");
        }

        Console.WriteLine();
        Console.WriteLine($"目录元数据：{Path.Combine(appRoot, AppRootManifestStore.ManifestFileName)}");
    }

    public static void InstalledApps(string appRoot, ManifestLoadResult load)
    {
        Console.WriteLine($"=== {appRoot} 中的应用 ===");
        if (load.Warning is not null)
        {
            Console.WriteLine($"  [提示] {load.Warning}");
        }

        if (load.Manifest.Apps.Count == 0)
        {
            Console.WriteLine("（该目录还没有登记任何应用）");
            return;
        }

        Console.WriteLine($"{"名称",-24} {"目录",-24} {"主程序",-28} 来源压缩包");
        foreach (var app in load.Manifest.Apps)
        {
            Console.WriteLine(
                $"{Truncate(app.Name, 24),-24} {Truncate(app.Directory, 24),-24} {Truncate(app.MainExecutable, 28),-28} {app.SourceArchiveFileName ?? "-"}");
        }
    }

    public static string DescribeSource(SelectionSource source) => source switch
    {
        SelectionSource.Llm => "LLM",
        SelectionSource.Rules => "规则评分",
        SelectionSource.User => "人工选择",
        _ => source.ToString(),
    };

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
