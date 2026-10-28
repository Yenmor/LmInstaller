using LmInstaller.Core.Archive;
using LmInstaller.Core.Configuration;
using LmInstaller.Core.Installation;
using LmInstaller.Core.Llm;
using LmInstaller.Core.Manifest;
using LmInstaller.Core.Models;
using LmInstaller.Core.Pipeline;
using LmInstaller.Core.Platform;

namespace LmInstaller.Cli;

/// <summary>Command implementations. The core engine does the work; this layer only reports and asks.</summary>
internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = Arguments.Parse(args.Skip(1));

        return command switch
        {
            "analyze" => await AnalyzeAsync(options, install: false).ConfigureAwait(false),
            "install" => await AnalyzeAsync(options, install: true).ConfigureAwait(false),
            "list" => List(options),
            "demo" => await DemoAsync(options).ConfigureAwait(false),
            "set-app-root" => SetAppRoot(options),
            _ => Unknown(command),
        };
    }

    private static async Task<int> AnalyzeAsync(Arguments options, bool install)
    {
        var zip = Path.GetFullPath(options.Require("zip"));
        var paths = new LmInstallerPaths();
        var settings = new ManagerSettingsStore(paths).Load();

        var appRoot = install ? ResolveAppRoot(options, settings) : options.Get("app-root");
        return await RunPipelineAsync(zip, appRoot, options, install, forceYes: false, demo: false).ConfigureAwait(false);
    }

    private static async Task<int> DemoAsync(Arguments options)
    {
        var demoRoot = options.Get("workspace") ?? Path.Combine(Path.GetTempPath(), "lminstaller-demo");
        var archive = DemoPackage.Create(Path.Combine(demoRoot, "input"));
        var appRoot = options.Get("app-root") ?? Path.Combine(demoRoot, "apps");
        Directory.CreateDirectory(appRoot);

        if (options.Has("unsafe"))
        {
            var unsafeZip = DemoPackage.CreateUnsafe(Path.Combine(demoRoot, "input"));
            Console.WriteLine("演示模式：构造一个含路径穿越条目的恶意压缩包，验证会被拦截。\n");
            try
            {
                return await RunPipelineAsync(
                    unsafeZip,
                    null,
                    options,
                    install: false,
                    forceYes: false,
                    demo: false,
                    defaultWorkspaceRoot: Path.Combine(demoRoot, "workspaces"))
                    .ConfigureAwait(false);
            }
            catch (UnsafeArchiveException ex)
            {
                Console.WriteLine("已拦截：" + ex.Message);
                Console.WriteLine();
                Console.WriteLine("压缩包没有被解压，也没有任何文件被写入应用根目录。");
                return 0;
            }
        }

        Console.WriteLine("演示模式：自动生成示例压缩包并走完整流程。");
        Console.WriteLine("压缩包里的 .exe 都是占位文本，不会被读取版本信息，也不会被执行。\n");

        return await RunPipelineAsync(
                archive,
                appRoot,
                options,
                install: true,
                forceYes: true,
                demo: true,
                defaultWorkspaceRoot: Path.Combine(demoRoot, "workspaces"))
            .ConfigureAwait(false);
    }

    private static async Task<int> RunPipelineAsync(
        string archivePath,
        string? appRoot,
        Arguments options,
        bool install,
        bool forceYes,
        bool demo,
        string? defaultWorkspaceRoot = null)
    {
        var llmClient = TryCreateLlmClient(options, out var llmNote);
        Console.WriteLine($"LLM：{(llmClient is null ? "未配置（" + llmNote + "）" : "已配置")}");
        Console.WriteLine();

        var selector = new MainExecutableSelector(
            llmClient is null ? null : new LlmMainExecutableSelector(llmClient, new ReadOnlyWorkspaceToolHostFactory()));

        var installer = new AppInstaller(new AppRootManifestStore());
        var workspaceFactory = new TempWorkspaceFactory(
            options.Get("workspace-root") ?? defaultWorkspaceRoot ?? new LmInstallerPaths().TempWorkspaceDirectory);

        var pipeline = new InstallerPipeline(
            selector: selector,
            workspaceFactory: workspaceFactory,
            installer: installer);

        ConsoleReport.AnalysisHeader(archivePath);
        var analysis = await pipeline.AnalyzeAsync(archivePath).ConfigureAwait(false);

        try
        {
            ConsoleReport.Inspection(analysis.Inspection);
            ConsoleReport.Candidates(analysis.Candidates);
            ConsoleReport.Selection(analysis.Selection);

            if (analysis.Candidates.Count == 0)
            {
                Console.WriteLine("没有可用候选，流程结束。");
                return 2;
            }

            if (!install)
            {
                Console.WriteLine("（仅分析，未安装；改用 install 命令即可完成安装。）");
                return 0;
            }

            if (appRoot is null)
            {
                throw new CliException("安装需要 --app-root <目录>（或先用 set-app-root 保存应用根目录）。");
            }

            var selection = ResolveSelection(analysis, options, forceYes);
            if (selection is null)
            {
                return 2;
            }

            if (!forceYes && !Confirm("确认按上面的选择安装到应用根目录？"))
            {
                Console.WriteLine("已取消，未写入任何文件。");
                return 3;
            }

            var installOptions = BuildInstallOptions(options, forceYes);

            try
            {
                var result = pipeline.Install(analysis, selection, appRoot, installOptions);
                Console.WriteLine();
                ConsoleReport.InstallResult(result, appRoot);
            }
            catch (InstallConflictException ex)
            {
                Console.WriteLine();
                Console.WriteLine($"安装冲突：{ex.TargetPath} 已存在。");
                Console.WriteLine("可改用 --conflict new（安装到新目录）或 --conflict overwrite --yes（覆盖）。");
                return 4;
            }

            Console.WriteLine();
            ConsoleReport.InstalledApps(appRoot, new AppRootManifestStore().Load(appRoot));

            if (demo)
            {
                Console.WriteLine();
                Console.WriteLine($"演示目录（可自行查看）：{appRoot}");
            }

            return 0;
        }
        finally
        {
            if (options.Has("keep-workspace"))
            {
                Console.WriteLine($"临时工作区已保留：{analysis.Workspace.RootPath}");
            }
            else
            {
                analysis.Dispose();
            }
        }
    }

    private static MainExecutableSelection? ResolveSelection(
        ArchiveAnalysis analysis,
        Arguments options,
        bool forceYes)
    {
        var pick = options.Get("pick");
        if (!string.IsNullOrWhiteSpace(pick))
        {
            if (int.TryParse(pick.Trim(), out var index) && index >= 1 && index <= analysis.Candidates.Count)
            {
                var chosen = analysis.Candidates[index - 1];
                return new MainExecutableSelection
                {
                    RelativePath = chosen.RelativePath,
                    Source = SelectionSource.User,
                    Confidence = 1.0,
                    Reason = "命令行 --pick 指定",
                };
            }

            var byPath = analysis.Candidates.FirstOrDefault(
                c => string.Equals(c.RelativePath, pick.Trim().Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return new MainExecutableSelection
                {
                    RelativePath = byPath.RelativePath,
                    Source = SelectionSource.User,
                    Confidence = 1.0,
                    Reason = "命令行 --pick 指定",
                };
            }

            throw new CliException($"--pick 指定的候选无效：{pick}");
        }

        if (analysis.Selection.Selection is not null)
        {
            return analysis.Selection.Selection;
        }

        if (!forceYes && !Console.IsInputRedirected)
        {
            var suggested = analysis.Selection.SuggestedSelection;
            var suggestedCandidate = analysis.Candidates.FirstOrDefault(
                c => suggested is not null &&
                     string.Equals(c.RelativePath, suggested.RelativePath, StringComparison.OrdinalIgnoreCase));

            var chosen = PromptForCandidate(analysis.Candidates, suggestedCandidate);
            if (chosen is not null)
            {
                return new MainExecutableSelection
                {
                    RelativePath = chosen.RelativePath,
                    Source = SelectionSource.User,
                    Confidence = 1.0,
                    Reason = "用户在命令行中选择",
                };
            }
        }

        if (forceYes && analysis.Selection.SuggestedSelection is not null)
        {
            return analysis.Selection.SuggestedSelection;
        }

        Console.WriteLine("需要人工选择，但没有交互输入。请用 --pick <编号> 指定，或加上 --yes 接受规则建议。");
        return null;
    }

    private static CandidateExecutable? PromptForCandidate(
        IReadOnlyList<CandidateExecutable> candidates,
        CandidateExecutable? suggested)
    {
        var defaultIndex = suggested is null ? 1 : candidates.ToList().IndexOf(suggested) + 1;
        if (defaultIndex < 1)
        {
            defaultIndex = 1;
        }

        Console.Write($"请选择主程序编号 1-{candidates.Count}（回车接受建议 {defaultIndex}）：");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return candidates[defaultIndex - 1];
        }

        if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= candidates.Count)
        {
            return candidates[index - 1];
        }

        Console.WriteLine("输入无效，使用建议项。");
        return candidates[defaultIndex - 1];
    }

    private static InstallOptions BuildInstallOptions(Arguments options, bool forceYes)
    {
        var policy = options.GetOrDefault("conflict", "fail").ToLowerInvariant() switch
        {
            "fail" => ConflictPolicy.Fail,
            "new" => ConflictPolicy.UseNewName,
            "overwrite" => ConflictPolicy.Overwrite,
            var other => throw new CliException($"未知的 --conflict 取值：{other}（可选 fail/new/overwrite）"),
        };

        var confirmOverwrite = false;
        if (policy == ConflictPolicy.Overwrite)
        {
            confirmOverwrite = forceYes || Confirm("覆盖策略会替换同名目录，确认继续？");
            if (!confirmOverwrite)
            {
                throw new CliException("未确认覆盖，已取消。");
            }
        }

        return new InstallOptions
        {
            ConflictPolicy = policy,
            ConfirmOverwrite = confirmOverwrite,
            DisplayNameOverride = options.Get("name"),
        };
    }

    private static int List(Arguments options)
    {
        var paths = new LmInstallerPaths();
        var settings = new ManagerSettingsStore(paths).Load();
        var appRoot = options.Get("app-root") ?? settings.AppRootPath;

        if (string.IsNullOrWhiteSpace(appRoot))
        {
            Console.WriteLine("没有指定应用根目录：请加 --app-root <目录>，或先运行 set-app-root。");
            return 2;
        }

        var fullPath = Path.GetFullPath(appRoot);
        if (!Directory.Exists(fullPath))
        {
            Console.WriteLine($"目录不存在：{fullPath}");
            return 2;
        }

        ConsoleReport.InstalledApps(fullPath, new AppRootManifestStore().Load(fullPath));
        return 0;
    }

    private static int SetAppRoot(Arguments options)
    {
        var paths = new LmInstallerPaths();
        var validator = new AppRootValidator(new[] { paths.BaseDirectory, paths.TempWorkspaceDirectory });
        var result = validator.Validate(options.Get("path") ?? options.Get("app-root"));

        Console.WriteLine(result.Message + (result.NormalizedPath is null ? string.Empty : $"（{result.NormalizedPath}）"));
        if (!result.IsValid || result.NormalizedPath is null)
        {
            return 2;
        }

        var store = new ManagerSettingsStore(paths);
        var settings = store.Load();
        store.Save(settings with { AppRootPath = result.NormalizedPath });
        Console.WriteLine($"已保存到管理器配置：{store.ConfigFilePath}");
        return 0;
    }

    private static string? ResolveAppRoot(Arguments options, ManagerSettings settings)
    {
        var appRoot = options.Get("app-root") ?? settings.AppRootPath;
        if (string.IsNullOrWhiteSpace(appRoot))
        {
            throw new CliException("安装需要 --app-root <目录>，或先用 set-app-root 保存应用根目录。");
        }

        var validator = new AppRootValidator();
        var result = validator.Validate(appRoot);
        if (!result.IsValid || result.NormalizedPath is null)
        {
            throw new CliException("应用根目录不可用：" + result.Message);
        }

        return result.NormalizedPath;
    }

    private static ILlmChatClient? TryCreateLlmClient(Arguments options, out string note)
    {
        var paths = new LmInstallerPaths();
        var settings = new ManagerSettingsStore(paths).Load();

        var baseUrl = options.Get("llm-base")
            ?? Environment.GetEnvironmentVariable("LMINSTALLER_LLM_BASE_URL")
            ?? settings.Llm.BaseUrl;
        var model = options.Get("llm-model")
            ?? Environment.GetEnvironmentVariable("LMINSTALLER_LLM_MODEL")
            ?? settings.Llm.Model;
        var apiKey = options.Get("llm-key")
            ?? Environment.GetEnvironmentVariable("LMINSTALLER_LLM_API_KEY")
            ?? settings.Llm.ApiKey;

        var llmOptions = new LlmOptions
        {
            BaseUrl = baseUrl,
            Model = model,
            ApiKey = apiKey,
            TimeoutSeconds = 120,
        };

        if (!llmOptions.IsConfigured)
        {
            note = "需要 --llm-base/--llm-model 或环境变量 LMINSTALLER_LLM_BASE_URL / LMINSTALLER_LLM_MODEL";
            return null;
        }

        note = string.Empty;
        return new OpenAiCompatibleChatClient(OpenAiCompatibleChatClient.CreateHttpClient(llmOptions), llmOptions);
    }

    private static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N] ");
        var input = Console.ReadLine();
        return input is not null && input.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?" or "-?";

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            lminstaller —— portable 应用安装器（核心引擎命令行）

            用法：
              lminstaller demo    [--workspace <目录>] [--app-root <目录>]
                  生成示例压缩包并完整走一遍"分析 → 选主程序 → 安装 → 写元数据"。
              lminstaller demo --unsafe
                  生成一个含路径穿越条目的恶意压缩包，验证会被安全拦截。

              lminstaller analyze --zip <压缩包> [--llm-base <URL> --llm-model <模型> [--llm-key <密钥>]]
                  只分析并列出候选，不写入应用根目录。

              lminstaller install --zip <压缩包> [--app-root <目录>] [--name <名称>]
                                  [--pick <编号|相对路径>] [--conflict fail|new|overwrite]
                                  [--yes] [--llm-base <URL> --llm-model <模型>]
                  分析并安装。未配置 LLM 时会列出候选让你手动选择。

              lminstaller list  [--app-root <目录>]      查看该目录已登记的应用
              lminstaller set-app-root --path <目录>     校验并保存应用根目录

            常用参数：
              --conflict new        同名目录时安装到 "名称 (2)"，不覆盖任何数据
              --conflict overwrite  覆盖同名目录（会再次确认，--yes 可跳过）
              --pick 2              直接指定第 2 个候选作为主程序
              --yes                 跳过确认，采用自动/建议结果
              --keep-workspace      保留临时解压区，便于自己检查
              --workspace-root <目录>  临时解压区位置（默认在管理器数据目录下）

            环境变量：LMINSTALLER_LLM_BASE_URL、LMINSTALLER_LLM_MODEL、LMINSTALLER_LLM_API_KEY
            """);
    }
}
