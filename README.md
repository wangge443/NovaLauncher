# Nova Launcher

Nova Launcher 是面向 Windows 11 的现代桌面应用启动器，采用 Windows 11 Minimal Glass UI，通过左侧导航和右侧大图标启动区组织应用。

## 当前状态

第四阶段 4.2：保留 .exe / .lnk 拖拽导入，新增单击、Enter/Space 启动；卡片 Hover/键盘聚焦显示上次启动时间和次数。统计仅在会话内保存，4.3 再接入 SQLite。LNK 通过原始快捷方式交给 Windows Shell，保留参数与工作目录。没有新增 NuGet 或其他业务功能。Desktop Backdrop 仍为 Experimental，未修改窗口与背景实现；本阶段已获用户授权完成最终 UI 收尾并提交。

## 构建与运行

需要 Windows 与 .NET SDK 9.0.318（global.json 固定版本）。在仓库根目录执行：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.App/NovaLauncher.App.csproj -c Release --no-build
```

已验证 Release 构建无警告和错误，以及实际窗口导航、键盘选择、缩放和标题栏按钮。运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UiShell.ps1` 可复现 UI 检查并生成截图；脚本会操作测试窗口。实际截图和验证边界见 [Design.md](docs/Design.md)。

4.1 导入与卡片验证：运行 `dotnet run --project NovaLauncher.Verification -c Release --no-build`，执行服务/状态断言与真实 OLE 拖放，生成 `docs/screenshots/4.1/` 运行截图。需要交互式桌面，运行期间会移动鼠标；结束后恢复位置并清理测试文件。详见 [Development.md](docs/Development.md)。

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

4.1 已提交为 58bf6dc；4.2 最终收尾验证后按用户授权提交；Desktop Backdrop 后续可独立研究，不作为业务开发的前置条件。

ui-ux-pro-max 已安装到 Codex 用户技能目录，入口已读取，WPF 检索验证通过。初始化文档以 docs: initialize project documentation 作为首次提交；未关联远程仓库。


4.2 验证：先构建解决方案，再执行 `dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch`。该模式通过独立的 NovaLauncher.LaunchProbe 测试程序验证真实进程启动、快捷方式配置与卡片交互，不启动用户应用；测试输出写入临时目录并在退出时清理。
