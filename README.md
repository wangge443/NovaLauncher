# Nova Launcher

## 8.4.3 Glass Integration Polish（已通过最终验收）

重建整窗、Workspace、面板、控件四层材质；统一Chrome与Sidebar，去掉标题底色，设置卡片使用轻高光/细边界，分类末行整合创建入口。Release 0 warnings / 0 errors；透明28项、相关回归231项、发布启动6项通过。实际截图、材质参数和验证边界见 [8.4.3交付记录](docs/GlassIntegration-8.4.3.md)。8.4.3透明底层、数据库、业务、搜索和性能架构不变；用户已确认作为Nova Preview Glass Mode基线并授权提交 style: refine glass mode visual hierarchy，不开始下一阶段。


Nova Launcher 是面向 Windows 11 的现代桌面应用启动器，采用 Windows 11 Minimal Glass UI，通过左侧导航和右侧大图标启动区组织应用。

## 当前状态

8.4.3 Glass Integration Polish已通过最终验收：用户可见名称为“玻璃模式（实验性）”，沿用8.4实时透明机制，采用Windows逐像素窗口合成，保留Default、DesktopWallpaper和CustomImage。实时后窗证据、路线比较、性能和待人工验证项见 [Transparency-8.4.md](docs/Transparency-8.4.md)。最新视觉与独立验收包见 [GlassIntegration-8.4.3.md](docs/GlassIntegration-8.4.3.md)。

8.3 主界面信息架构简化已通过验收并提交为 `3eaf6b8`：默认“全部应用”，移除聚合 Dashboard，保留分类、收藏、最近使用、Quick Launcher 和渐进渲染。实现与验证见 [Workspace-8.3.md](docs/Workspace-8.3.md)。

8.2 已通过验收并提交为 `b592964`：300项首屏布局中位数4497→1103ms；没有实现真正虚拟化，历史性能与限制见 [Performance-8.2.md](docs/Performance-8.2.md)。

第八阶段 8.1：发布安全与交付基线（已通过开发验收）。当前 Preview 版本为 `0.1.0-preview.1`；增加单实例、字段级设置保存、异步有界日志、统一 Nova 图标与可重复的 FDD/SCD/Single-file 发布脚本。交付目录、验证证据及未完成的真实干净机器验收见 [Release.md](docs/Release.md)。

## 构建与运行

需要 Windows 与 .NET SDK 9.0.318（global.json 固定版本）。在仓库根目录执行：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
dotnet run --project NovaLauncher.App/NovaLauncher.App.csproj -c Release --no-build
```

已验证 Release 构建无警告和错误，以及实际窗口导航、键盘选择、缩放和标题栏按钮。8.1 发布验证使用 `scripts/Test-Publish.ps1`（命令和实际范围见 [Release.md](docs/Release.md)），会操作隔离测试窗口。`Test-UiShell.ps1` 的旧导航假设仅作为历史记录。实际截图和验证边界见 [Design.md](docs/Design.md)。

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

4.1 已提交为 58bf6dc；4.2 已提交为 480f357；4.3 稳定基线为 5168131。5.0 建立迁移基线，后续 Schema 变化必须新增 Migration，禁止继续使用 EnsureCreated。Desktop Backdrop 后续可独立研究，不作为业务开发的前置条件。

ui-ux-pro-max 已安装到 Codex 用户技能目录，入口已读取，WPF 检索验证通过。初始化文档以 docs: initialize project documentation 作为首次提交；未关联远程仓库。


4.2 验证：先构建解决方案，再执行 `dotnet run --project NovaLauncher.Verification -c Release --no-build -- --launch`。该模式通过独立的 NovaLauncher.LaunchProbe 测试程序验证真实进程启动、快捷方式配置与卡片交互，不启动用户应用；测试输出写入临时目录并在退出时清理。

4.3 验证：`dotnet run --project NovaLauncher.Verification -c Release --no-build -- --persistence`。使用隔离临时数据库，包含独立 WPF 进程关闭/重启恢复与异常测试；不会写入真实用户数据库。
