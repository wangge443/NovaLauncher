# Nova Launcher

Nova Launcher 是面向 Windows 11 的现代桌面应用启动器，采用 Windows 11 Minimal Glass UI，通过左侧导航和右侧大图标启动区组织应用。

## 当前状态

第三阶段 3.2 收尾：保留 .NET 9 / WPF / MVVM 和已确认布局，新增官方 DWM Desktop Acrylic 请求与浅色 fallback，删除侧栏底部宣传文字。没有业务功能或新增 NuGet。Release 构建与窗口操作通过，Desktop Backdrop 标记为 Experimental，当前尚未可靠呈现壁纸；统一使用浅白表面与 SolidColor fallback，CustomImage 暂不实现。实验材质不阻塞核心功能开发；详见 Design.md。

## 构建与运行

需要 Windows 与 .NET SDK 9.0.318（global.json 固定版本）。在仓库根目录执行：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.App/NovaLauncher.App.csproj -c Release --no-build
```

已验证 Release 构建无警告和错误，以及实际窗口导航、键盘选择、缩放和标题栏按钮。运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UiShell.ps1` 可复现 UI 检查并生成截图；脚本会操作测试窗口。实际截图和验证边界见 [Design.md](docs/Design.md)。

## 技术方向

| 项目 | 选型 |
| --- | --- |
| 语言与运行时 | C# / .NET 9 |
| 桌面界面 | WPF |
| 架构 | MVVM |
| 本地持久化 | SQLite |
| 视觉风格 | Windows 11 Minimal Light Glass UI，浅色主题 |

界面目标包括半透明背景、Acrylic/Mica 风格、左侧导航栏、右侧大图标启动区及 Hover 交互动画。首期规划为手动添加、编辑、移除、搜索与启动本地 exe，并使用 SQLite 持久化，具体验收条件见 Product.md。

## 文档入口

- [Agent.md](Agent.md)：技术栈、MVVM、UI、编码、Git 与 AI Agent 工作规范；开始工作前主动阅读。
- [docs/Design.md](docs/Design.md)：布局、视觉参数、交互状态、可访问性与 ui-ux-pro-max 接入准备。
- [docs/Architecture.md](docs/Architecture.md)：模块职责、依赖方向、数据流和 SQLite 持久化设计。

- [docs/Product.md](docs/Product.md)：项目定位、用户场景、首期功能与扩展方向。
- [docs/Development.md](docs/Development.md)：开发流程、Git 规范与阶段验收。
- [docs/SkillIntegration.md](docs/SkillIntegration.md)：技能来源、版本、安装配置与可读性验证。

## 后续步骤

UI Shell 收尾后可按用户授权开展核心功能。Desktop Backdrop 后续可独立研究，不作为业务开发的前置条件。

ui-ux-pro-max 已安装到 Codex 用户技能目录，入口已读取，WPF 检索验证通过。初始化文档以 docs: initialize project documentation 作为首次提交；未关联远程仓库。
