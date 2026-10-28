# LmInstaller

Windows 上的 portable 应用安装与管理器。本仓库当前包含**阶段一的核心引擎**（`LmInstaller.Core`）。

详细的产品目标、行为和安全边界见 [`PROJECT_GUIDE.md`](PROJECT_GUIDE.md)。本 README 只描述已经实现的代码。

## 已实现的核心流程

给定一个 ZIP 路径，核心引擎可以完成：

1. **安全检查**：在解压前遍历 ZIP 条目，拦截路径穿越、绝对路径/盘符、符号链接、重复条目、Windows 保留设备名、过深路径、条目数量/大小超限和解压炸弹。
2. **临时解压**：把内容解压到独立临时工作区（`ITempWorkspaceFactory`），解压过程中再次校验每个目标路径必须落在临时目录内，符号链接直接跳过，超限立即中止。
3. **扫描候选**：递归找出 `.exe`、`.com`、`.bat`、`.cmd`、`.ps1`、`.vbs`、`.jar`，读取 PE 版本信息（产品名/描述/版本，尽力而为），并用规则评分给候选排序。
4. **LLM 选主程序**：把候选列表交给 LLM，模型可通过**只读**工具（列目录、读文本片段、查文件信息、筛选可执行文件）浏览临时工作区，最后返回 `{"path","reason","confidence","name"}`。返回的路径必须通过程序校验（在候选列表中、位于工作区内、是 `.exe`/`.com`）。
5. **人工兜底**：没有配置 LLM、调用失败或置信度低于阈值时，返回全部候选与规则评分建议，由用户选择。
6. **安装**：把完整应用目录（含 DLL、资源）复制进用户指定的应用根目录，先复制到暂存目录再移动到位，失败可回滚；不复制单个 `.exe`，不执行任何压缩包内的程序。
7. **写入目录元数据**：在**应用根目录的根目录**写入 `LmInstaller.apps.json`，记录该目录下有哪些 portable 应用、每个应用的主程序和来源压缩包。

## 目录结构

```
LmInstaller.slnx
Directory.Build.props          # 公共编译属性（Core 开启 warnings-as-errors）
src/LmInstaller.Core/          # 不依赖 UI 与 Windows API 的核心类库（net10.0）
  Archive/                     # ZIP 安全检查与安全解压
  Scanning/                    # 候选扫描、规则评分、PE 版本信息读取
  Llm/                         # LLM 契约、OpenAI 兼容客户端、只读工具、主程序选择
  Installation/                # 目录名净化、冲突处理、复制安装
  Manifest/                    # 应用根目录元数据
  Configuration/               # 应用根目录校验、管理器设置（配置与根目录分离）
  Pipeline/                    # 把上述步骤串起来的入口
  Platform/                    # 管理器自有目录、原子写文件
tests/LmInstaller.Core.Tests/  # xUnit 测试（92 个用例）
```

## 代码入口

```csharp
var paths = new LmInstallerPaths();          // 管理器自身的数据目录（与应用根目录分离）
var store = new AppRootManifestStore();
var pipeline = new InstallerPipeline(
    selector: new MainExecutableSelector(
        new LlmMainExecutableSelector(llmClient, new ReadOnlyWorkspaceToolHostFactory())),
    installer: new AppInstaller(store));

using var analysis = await pipeline.AnalyzeAsync(@"D:\downloads\MyApp.zip");

if (analysis.RequiresUserDecision)
{
    // 展示 analysis.Candidates（含 ruleScore / ruleNotes / 版本信息），或使用
    // analysis.Selection.SuggestedSelection 作为默认选项，由用户确认。
}

var result = pipeline.Install(
    analysis,
    selection,                                // 用户确认或 LLM 高置信度的结果
    appRootPath,
    new InstallOptions { ConflictPolicy = ConflictPolicy.UseNewName });

Console.WriteLine($"已安装到 {result.InstallDirectory}");
```

`LlmMainExecutableSelector` 需要 `ILlmChatClient`。生产环境用
`new OpenAiCompatibleChatClient(OpenAiCompatibleChatClient.CreateHttpClient(options), options)`。

## 应用根目录元数据

安装后会在应用根目录生成 `LmInstaller.apps.json`（可直接放在便携目录里随目录一起移动）：

```json
{
  "schemaVersion": 1,
  "updatedAtUtc": "2026-09-29T12:00:00+00:00",
  "apps": [
    {
      "id": "…",
      "name": "示例应用",
      "directory": "MyApp",
      "mainExecutable": "MyApp.exe",
      "installedAtUtc": "2026-09-29T12:00:00+00:00",
      "version": "1.2.3.4",
      "productName": "示例应用",
      "sourceArchiveFileName": "MyApp.zip",
      "sourceArchiveSha256": "…",
      "status": "installed"
    }
  ]
}
```

`directory` 与 `mainExecutable` 都是相对路径，目录整体移动后依然有效。

## 安全约束的落地方式

- 应用根目录由用户选择，通过 `AppRootValidator` 校验（可创建、可写、不与管理器数据/临时目录重叠），**不会**回退到程序目录或当前工作目录。
- 管理器配置、日志、缓存、临时解压区来自 `LmInstallerPaths`，与用户的应用根目录分开。
- LLM 只能通过 `ReadOnlyWorkspaceToolHost` 访问临时工作区，路径参数经过规范化并确认未越界；工具集中没有执行、删除、移动能力。
- LLM 返回的路径只作为**建议**，必须经过程序校验；校验失败一律视为“没有结果”，退回人工选择。
- 覆盖、删除都需要显式策略：`ConflictPolicy.Overwrite` 必须同时设置 `ConfirmOverwrite`。
- 解压与复制都不会跟随符号链接/重解析点。

## 亲自测试效果

先确保 `dotnet` 可用：

```bash
export PATH="$HOME/.dotnet:$PATH"   # 如果 dotnet 已在 PATH 中可跳过
```

### 1. 零准备：一份示例压缩包跑完整流程

```bash
dotnet run --project src/LmInstaller.Cli -- demo
```

它会自动生成示例 ZIP，依次打印安全检查结果、候选可执行文件（含评分与理由）、主程序判断、安装结果，并写出目录元数据。演示目录默认在系统临时目录下的 `lminstaller-demo`，可以用 `--app-root` 指定别的位置。

### 2. 看安全检查：恶意压缩包会被拦截

```bash
dotnet run --project src/LmInstaller.Cli -- demo --unsafe
```

构造一个含 `../escaped.txt` 条目的压缩包，验证它在解压前就被拦下，并且没有文件被写到目标目录之外。

### 3. 用自己下载的真实压缩包

```bash
# 只分析，不写盘
dotnet run --project src/LmInstaller.Cli -- analyze --zip ~/Downloads/SomeApp.zip

# 分析并安装到 ~/PortableApps（同名目录存在时安装为 "名称 (2)"）
dotnet run --project src/LmInstaller.Cli -- install \
    --zip ~/Downloads/SomeApp.zip \
    --app-root ~/PortableApps \
    --conflict new
```

没有配置 LLM 时，它会列出全部候选并按编号让你手动选择（也可用 `--pick 2` 直接指定）。
加上 `--keep-workspace` 可以保留临时解压目录，方便自己检查解压结果。

### 4. 接上 LLM 再跑一次

任意 OpenAI 兼容端点都可以（OpenAI、网关、本地 Ollama / LM Studio 等）：

```bash
export LMINSTALLER_LLM_BASE_URL=https://api.openai.com/v1
export LMINSTALLER_LLM_MODEL=gpt-4o-mini
export LMINSTALLER_LLM_API_KEY=sk-...

dotnet run --project src/LmInstaller.Cli -- install \
    --zip ~/Downloads/SomeApp.zip --app-root ~/PortableApps --conflict new
```

这时主程序由 LLM 通过只读工具判断，输出会显示来源、路径、理由和置信度；置信度低或调用失败会自动退回人工选择。

### 5. 查看某个目录登记了哪些应用

```bash
dotnet run --project src/LmInstaller.Cli -- list --app-root ~/PortableApps
```

`set-app-root --path <目录>` 可以先把应用根目录保存进管理器配置，之后 `install`/`list` 就不必每次传 `--app-root`。

## 开发环境

- .NET SDK 10（本机位于 `~/.dotnet`，如果 `dotnet` 不在 PATH，先执行 `export PATH="$HOME/.dotnet:$PATH"`）。
- 核心类库只依赖基础类库，可在 macOS 上构建和测试。
- Windows 专用能力（快捷方式、图标提取、Rainmeter、系统拖放）不在本阶段范围内。

## 构建与测试

```bash
dotnet build LmInstaller.slnx
dotnet test LmInstaller.slnx
```

在 macOS 上验证过的内容：核心类库编译（0 警告）、92 个单元测试全部通过。

## 仍需在 Windows 上验证

- 真实 Windows ZIP（含 `Setup.exe`、`unins000.exe` 等）的 PE 版本信息读取。
- 主界面/桌面小组件集成、快捷方式创建、图标提取与系统拖放。
- 非 ASCII 路径、网络驱动器、外置磁盘、同步目录下的安装行为。
