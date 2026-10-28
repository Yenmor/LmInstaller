# AGENTS.md

## 仓库现状
- `PROJECT_GUIDE.md` 是需求的唯一来源（产品目标、行为与安全约束）。第 14 节是分阶段计划（先完成阶段一），第 15 节是验收标准。动手前先读它。
- 已搭建阶段一的核心引擎：`LmInstaller.Core`（不依赖 UI 与 Windows API 的类库）、`LmInstaller.Core.Tests`（xUnit）与 `LmInstaller.Cli`（用真实压缩包验证流程的命令行工具）。解决方案文件是 `LmInstaller.slnx`；命令行用法见 `README.md` 的"亲自测试效果"。
- 尚未创建 UI 项目。UI 框架（WPF 或 WinUI）仍未确定，搭建 UI 项目前先和用户确认；核心类库不要引用任何 UI 框架。
- 还没有 CI 配置。

## 构建与测试（本机已验证）
```bash
# 若 dotnet 不在 PATH：SDK 位于 ~/.dotnet
export PATH="$HOME/.dotnet:$PATH"

dotnet build LmInstaller.slnx   # 0 警告 0 错误
dotnet test  LmInstaller.slnx   # 92 个用例通过
```
- 改动 Core 后请运行上面两条命令；`dotnet test` 需要本机允许回环套接字（VSTest 需要）。
- 在沙箱里运行 `dotnet restore`/`dotnet build` 可能卡住，必要时申请联网/非沙箱执行。

## 技术栈与环境
- 已定为 C# / .NET Windows 桌面应用；UI 框架（WPF 或 WinUI）尚未最终确定，搭建项目前先和用户确认。
- 目标平台是 Windows，开发机是 macOS。在非 Windows 上构建 Windows 桌面项目需要在 csproj 中设置 `<EnableWindowsTargeting>true</EnableWindowsTargeting>`，否则报 NETSDK1100。
- UI 运行、快捷方式、图标提取、系统拖放、Rainmeter 只能在 Windows 上验证。没验证的行为不要写成已完成，要留下在 Windows 上的验证步骤。
- 核心引擎（ZIP 分析、候选评分、清单、目录迁移）放在不依赖 UI 和 Windows API 的类库里，这样在 macOS 上也能 `dotnet test`；主界面和桌面小组件都只是调用它的入口。

## 参考项目
- https://github.com/Yenmor/PortableAppManager 只是功能参考，不是本项目的基础代码。它是 Java 实现，不要移植它的代码或结构；只有做旧数据/应用包导入（阶段二、四）时才参考它的格式。

## 容易违反的硬性约束（详见 `PROJECT_GUIDE.md`）
- 应用根目录由用户在 UI 中选择和修改，不得写死，也不得回退到程序目录或当前工作目录。配置、日志、缓存、临时解压区和应用根目录分开存放。
- 解压要防护路径穿越、绝对路径、符号链接、重复条目、极端文件数量和解压炸弹；规范化路径后确认目标仍在临时目录或应用根目录内。
- 不要自动运行发现的 `.exe`、安装器或脚本。安装只分析和复制，首次启动由用户触发。
- LLM 结果只是建议，不能直接变成文件操作、路径或命令。没有 LLM 或网络时，规则评分加人工选择必须可用。
- 覆盖、移动、删除必须先预览并经用户确认。“从管理器移除”和“从磁盘删除”是两个不同操作。
- 安装时复制完整应用目录（含 DLL、资源），不能只复制 `.exe`；清单记录实际安装位置。
- 不要把 `.zip` 注册为系统默认打开方式。桌面小组件（如 Rainmeter）不包含安装逻辑，也不能绕过主界面的确认。

## 语言
- 文档和 UI 文案用简体中文；代码注释中英文都可以。
