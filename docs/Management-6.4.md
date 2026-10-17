# 6.4 分类管理入口与 ApplicationCard 菜单

基线：已验收 6.3，`911ee647133cbd6eaefbf6387d3c13573ca430f4`。6.4 已通过最终验收，按用户授权以 `feat: add application context menu and category management` 收尾提交；Context Menu 透明度保持不变。

## 实现

Sidebar 删除“新建分类”按钮和内联编辑器，保留所有动态分类导航。Settings 新增“分类管理”，通过 CategoryManagementView 显示图标/名称列表与内联创建区域。行模板留有未来扩展空间，本轮不增加重命名、删除、排序或图标编辑。

Settings 与 Sidebar 绑定同一个 Content.CategoryManager，继续调用 CategoryManagerViewModel → CategoryService → CategoryRepository。Trim、1–24 字符、空名/重复检查与 Toast 均复用原实现，Enter 创建、Esc 取消；成功后集合变化同步 Sidebar，原 SQLite 数据持久化不变。

Home、Category、Favorites、Recent 全部使用同一 ApplicationCardView；从该共享 View 移除 FavoriteButton 和 CategoryButton/旧分类 Popup，因此所有页面均不再显示卡片星标或 Hover 分类操作。默认显示图标/名称，Hover 显示名称、上次启动与启动次数。FavoriteGlyph 保留用于菜单图标。

每个卡片 View 持有独立 WPF ContextMenu，四个操作为收藏/取消收藏、移动到分类、打开文件位置、应用信息。通过 PlacementTarget.DataContext 绑定卡片，子菜单使用共享 CategoryManager.Choices，包含未分类、默认及自定义分类。直接绑定原 ToggleFavoriteCommand 和 ChangeCategoryCommand；保存成功才发布模型变化，失败仍显示“收藏状态保存失败”或“分类修改失败”，原类别/收藏状态不变。

ContextMenu 与 LaunchButton 的命令独立。真实右键和菜单操作不启动应用；左键、Enter/Space 保持启动行为。Shift+F10 使用卡片锚点打开；Context Menu 键保留 WPF KeyUp 处理，避免 KeyDown 与默认处理重复开关。方向键、Enter、Esc 沿用原生 MenuItem 导航。菜单关闭/卡片卸载同步清理浮层，Home 多个 Section 共享同一 ViewModel 时只打开被操作 View 的菜单。

新增 ManagementStyles.xaml，使用原语义色、字体和图标；菜单 10 DIP 圆角、轻 Surface、阴影、36 DIP 行高与轻分隔线。子菜单限制高度并滚动，WPF Popup 自动根据显示器工作区调整位置。ShellTheme.xaml 未修改。

文件位置通过 IApplicationLocationService 注入：App → ContentViewModel → ApplicationCardViewModel。服务检查目标文件存在，使用 Windows Explorer `/select,"目标路径"` 打开目录并选中文件。Shortcut 使用已解析的 ExecutablePath，而非 SourcePath。不存在时 Toast“应用文件不存在”，其他 Shell 失败 Toast“无法打开文件位置”。

应用信息为只读 Popup，显示名称、EXE/Shortcut、目标路径、分类、收藏状态、启动次数和上次启动时间。路径自动换行、可选中复制；支持关闭按钮、Esc 与点击外部关闭，不创建复杂详情页面、不写数据库。

Favorites 页面和 Home 收藏 Section 保持原投影更新逻辑。Favorites 空状态为“还没有收藏应用 / 右键应用卡片即可收藏”；分类空状态也改为右键操作提示。

## 数据与范围保护

无新增 Migration，无 Schema、Repository、CategoryService、依赖或模型变更。6.3 的 Background、DesktopWallpaper、CustomImage、Blur、Overlay、Theme、ShellBackgroundView 与 DesktopBackdrop 文件保持不变；App 仅增加文件位置服务注入及菜单资源字典引用。桌面壁纸仍是 Nova 内部静态图片，实时透明 DesktopBackdrop 继续 Experimental。

专项测试开始和结束读取真实 nova.db，比较 Applications / Categories / UserProfiles 全字段、收藏、LaunchCount / LastLaunchTime、AppSettings 全字段、数据库表定义以及图标/头像/Background 文件哈希，完全一致。没有对真实数据库执行分类、收藏或启动修改；交互修改在隔离临时库完成。

## 验证与复现

最终构建、各组断言及性能结果见 [Development.md](Development.md)。专项覆盖 Settings 原生创建、名称校验、Sidebar 同步、真实鼠标右键与菜单点击、Shift+F10/Context Menu 键、分类子菜单、只读信息、收藏投影、启动回归、缩放/最大化/Sidebar 调整、右下角菜单与子菜单重定位、缺失文件 Toast、Shortcut 目标语义、真实 Explorer 选中文件和真实数据保护。

分类、收藏、Home 旧测试已适配新入口，保留原业务/数据库/失败回滚/独立重启断言。文件位置测试仅关闭本次新建且匹配测试目录的 Explorer 窗口，避免干扰后续鼠标/OLE 回归。测试不绕过 HWND 防误点击保护。

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --management docs/screenshots/6.4
```

UI 验证需交互式 Windows 桌面并串行执行。真实数据保护检查需要已初始化的本机 nova.db。背景专项回归使用隔离库，未再次写入真实背景配置。ui-ux-pro-max 已读取；本机 Python 不可用，使用内置键盘、焦点、清晰反馈和一致性规则，未声称成功检索。未实机验证混合 DPI、多显示器拔插或高对比度切换。

## 截图

| 场景 | 截图 |
| --- | --- |
| Sidebar | [新 Sidebar](screenshots/6.4/sidebar.png) |
| Settings 分类管理 | [分类管理](screenshots/6.4/settings-categories.png) |
| 卡片默认 / Hover | [默认](screenshots/6.4/card-normal.png)、[Hover](screenshots/6.4/card-hover.png) |
| 右键菜单 | [菜单](screenshots/6.4/context-menu.png) |
| 分类子菜单 | [移动到分类](screenshots/6.4/category-submenu.png) |
| Favorites | [收藏页](screenshots/6.4/favorites.png)、[空状态](screenshots/6.4/favorites-empty.png) |
| 应用信息 | [只读信息](screenshots/6.4/application-info.png) |
