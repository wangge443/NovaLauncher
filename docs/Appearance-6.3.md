# 6.3 背景与外观交付记录

基于已验收 6.2（b176796e2b37400f20123ea3e3e72f053da14459）。本阶段等待验收，不自动 commit。

## 设置与架构

Settings → 外观 → 背景在原页面展开，保留单一滚动区。正式模式为 Default、DesktopWallpaper、CustomImage；Theme 保持 Light，DesktopBackdrop 原生代码保留为 Experimental / Internal，正式背景不启用它。Window.Opacity 始终为 1。

Background Source → Image Loader → Blur → White Overlay → ShellBackgroundView → Launcher UI。

AppearanceViewModel 通过 IAppearanceService 调用 AppearanceService，Windows API、文件读写与图片解码封装在服务层；AppSettings 原 Repository / Service 分层不变。

新增 Migration：`20261017084703_AddBackgroundAppearance`。旧 Migration 未修改。

| AppSettings 字段 | SQLite 类型 | 默认值 |
| --- | --- | --- |
| BackgroundImagePath | TEXT nullable | null |
| BackgroundOverlayOpacity | REAL | 0.72 |
| BackgroundBlurRadius | REAL | 12 |
| BackgroundStretch | TEXT | UniformToFill |

遮罩范围 20–95%，越高背景越淡；模糊范围 0–30；显示方式为填充 UniformToFill / 适应 Uniform。Default 隐藏图片参数，自定义模式显示选择图片，桌面模式显示刷新操作。恢复默认重置模式、路径及三个参数。

## 图片来源、保存与失败处理

桌面壁纸优先调用 Windows 官方 `SystemParametersInfoW(SPI_GETDESKWALLPAPER)`，读取本地静态图片，无网络请求。参考 [Microsoft API 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)。若 API 返回非空但失效路径，尝试 `%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper`；本机壁纸管理器遗留路径失效，实际截图来自该 Windows 静态缓存。缓存可能滞后，不捕获动态壁纸。API 失败或为空则直接安全回退。

第一版只读系统/主静态壁纸，不实现 IDesktopWallpaper 每显示器选择，不按窗口重叠面积选择显示器；切换模式、启动和手动刷新时重新读取，不安装系统 Hook。

自定义支持 PNG/JPG/JPEG/BMP，统一保存到 `%LOCALAPPDATA%\NovaLauncher\Background\background-{guid}.png`。后台解码与转换，先写 `.tmp`，Flush 后重命名为版本文件，再提交数据库指针；提交成功后清理旧图，失败清理候选并保留旧图。采用版本文件以避免固定文件覆盖成功但数据库提交失败时破坏旧背景。取消选择不改变当前图片。

BitmapCacheOption.OnLoad 读取后释放流、Freeze，避免持续锁定原图或自有文件。限制文件 80 MB、像素 4000 万，最长边缩至 2560。格式错误 Toast“不支持此图片格式”，损坏图片或选择器异常 Toast“无法读取背景图片”。

只在背景 Image 上复用 BlurEffect；白色 Border 单独控制 Overlay，文字、图标、Sidebar、卡片及 Settings 不模糊。低于 60% 遮罩时文字增加浅色底，Sidebar 保留额外半透明 Surface。高对比度状态隐藏图片层。

参数即时预览，400 ms debounce 合并 SQLite 写入；仅更新背景字段，保护同时写入的窗口/启动页设置。保存失败回滚并 Toast，退出等待在途命令与保存完成。Default、图片缺失/损坏或壁纸读取失败显示稳定浅白 Surface；失败仅当前会话回退，保留已存模式以供刷新恢复。恢复默认成功后清空路径并清理自有图片，清理失败不阻止设置恢复。

## 验证

Release Build：0 warnings / 0 errors。背景专项 39 项断言通过，覆盖 Migration/defaults、原生 PNG/JPG 文件选择与取消、源图删除、连续替换无锁、格式/损坏/保存失败、即时预览/debounce/回滚、Fill/Fit、背景层独立 Blur、恢复默认、错误 fallback、导航/Profile/Settings、键盘 Slider、小窗口/Sidebar 宽度与旧数据完整性。

真实 nova.db 使用三次独立生产 App 进程验证：Migration 后原 Applications / Categories / UserProfiles 全字段、Favorites、LaunchCount、LastLaunchTime、图标/头像哈希不变；原 AppSettings 11 个字段完整。删除源图后重启恢复 CustomImage + Overlay84 + Blur18 + Fit；下一次启动恢复 DesktopWallpaper + Overlay72 + Blur12。测试结束恢复原设置，原字段包含 UpdatedAt 逐值一致。

真实数据库备份保留：
`%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-appearance-20261017T085916Z-c05710fb8f964c42b19ac449c7c3f592.db`

原生选择器初始化曾出现时序波动，测试等待初始化后再输入路径。最终回归的 Hover 检查曾在 ScrollToTop 布局尚未完成时超时，补充等待滚动偏移归零再定位卡片后通过；未修改产品 Hover 行为。

性能检查启用自定义图及 Blur12，包含 82 卡片 Home 滚动、真实 Hover 和 30 次窗口缩放布局；具体测量见 Development.md。时间为布局/Render yield 的墙钟时间，不代表 GPU FPS。未实机测试混合 DPI、拔插多显示器或切换高对比度。ui-ux-pro-max 已读取并按内置规则检查；本机 Python 缺失，未执行其 Python 检索。

复现需要可交互 Windows 桌面，UI 测试串行执行：

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --appearance docs/screenshots/6.3
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --real-appearance
```

既有回归入口：--settings、--migrations、--profile、--home、--categories、--favorites、--recent、--launch、--toast、--polish；导入/OLE 不传模式参数。结果见 Development.md。

## 截图

| 场景 | 截图 |
| --- | --- |
| 默认 | [Default](screenshots/6.3/default.png) |
| Windows 静态壁纸 | [DesktopWallpaper](screenshots/6.3/desktop-wallpaper.png) |
| 自定义 | [CustomImage](screenshots/6.3/custom-image.png) |
| 遮罩20% / Blur0 / Fill | [低遮罩](screenshots/6.3/overlay20-blur0-fill.png) |
| 遮罩85% / Blur24 / Fit | [高遮罩](screenshots/6.3/overlay85-blur24-fit.png) |
| 卡片 Hover | [Hover](screenshots/6.3/custom-hover.png) |
| 外观设置 / 小窗口 | [设置](screenshots/6.3/appearance-settings.png)、[小窗口](screenshots/6.3/appearance-small.png) |
| 真实进程重启 | [自定义恢复](screenshots/6.3/real-custom-restarted.png)、[桌面恢复](screenshots/6.3/real-desktop-restarted.png) |
