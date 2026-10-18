# Nova Launcher — UI 设计

## 6.5 应用编辑交互（当前规范）

保持 6.4 ContextMenu 透明度、布局和样式，直接加入重命名/更换图标/恢复默认图标组。末尾移除使用 ManagementDangerBrush。恢复默认仅在 CustomIconPath 非空时显示。

重命名使用卡片独立 Popup，现有名称全选，实时校验，输入框 Enter 保存、Esc 取消；失败保留旧值并显示 Toast。移除确认使用内部 Popup，明确“从 Nova Launcher 移除此应用？”及“不会删除电脑中的程序或文件。”，默认焦点在取消。只读应用信息增加自定义图标状态。

已使用 ui-ux-pro-max 的本地规则检查焦点、键盘和视觉一致性；本机 Python 查询不可用，未宣称运行技能搜索。四张真实窗口截图见 [6.5 交付记录](ApplicationEditing-6.5.md)。以下为历史规范。

## 6.4 分类管理与上下文菜单（当前规范）

Settings 分类管理位于外观与启动设置之间，沿用 SettingsRow / SettingsButton、单页滚动和语义颜色。列表每行为图标 + 名称，保留布局扩展位置；新建采用带明确标签与错误提示的内联编辑器。Sidebar 不再出现新建分类入口。

卡片默认只显示图标与名称，Hover 仅展示图标、名称、上次启动及次数；收藏星标与分类按钮全部移除。统一右键菜单采用浅色 Surface、10 DIP 圆角、轻阴影、36 DIP 菜单行、图标与文字、轻分隔线、克制的 Highlight。分类子菜单高度受限并可滚动。只读信息浮层含名称、类型、目标路径、分类、收藏状态、次数、上次启动，长路径换行可选择复制，Esc/关闭按钮/点击外部关闭。

WPF ContextMenu / MenuItem 保留原生键盘导航与屏幕边缘重定位；Shift+F10 使用卡片锚点，Context Menu 键保留 WPF KeyUp 行为，鼠标右键靠近指针。每个卡片 View 独立控制浮层。ui-ux-pro-max 已读取，Python 不可用，按内置键盘、焦点、明确反馈与视觉一致性规则审核实际截图。不更改任何 6.3 背景或 Theme 设计。

详见 [6.4 交付记录](Management-6.4.md)。以下为历史规范。

## 6.3 背景与可读性（当前规范）

复用 Settings 单一 ScrollViewer，背景区域使用 Expander，内含模式选择、自定义图片按钮、桌面刷新、两个 Slider、显示方式和恢复默认。Default 隐藏图片参数；只有 CustomImage 显示图片按钮，DesktopWallpaper 显示静态壁纸限制和刷新。沿用 SettingsRow/Selector/QuietButton 与语义颜色，Slider 保留原生键盘和自动化语义。

ShellBackgroundView 中图片独立应用可复用 BlurEffect，白色 Overlay 在其上，Launcher UI 在两者上方；没有 Window.Opacity 或 UI 根层 Blur。Default 不加载图片，背景图片层隐藏。高对比度时隐藏图片层，沿用系统色。Sidebar 保留原半透明 Surface；标题栏增加轻 Surface，低于 60% 遮罩时对应用名与 Section 标题增加浅色文字底，图片背景下 HoverSurface 提高白色覆盖保证统计可读，卡片尺寸/布局/业务不变。

ui-ux-pro-max 已读取；本机 py 未找到 Python，依据内置对比度、语义控件、焦点、单层滚动和明确错误反馈规则审核实际截图。DesktopBackdrop 旧代码保留为 Experimental / Internal，默认不再激活。

详见 [6.3 实现与验证记录](Appearance-6.3.md)。以下为历史阶段。

## 6.2 设置中心（当前规范）

Settings 为独立 Workspace，单一纵向 ScrollViewer；依次为外观、启动与窗口、数据与存储、关于，不增加常驻 Sidebar。复用 WorkspacePadding=24；统一 SettingsRow 最小高度 64、Padding 16/12、圆角 8，描述与路径换行。两个轻量开关、三选项圆角 ComboBox 与 QuietButton 保持原生键盘语义和可见焦点。设置页不选中普通 Sidebar 项。

背景/主题显示默认/浅色与即将支持；点击仅 Toast，不弹 MessageBox。样式集中 ShellTheme，保留现有窗口背景和个人资料。使用 ui-ux-pro-max 内置规则检查；本轮 py 未找到 Python，未声称数据库检索成功。详情及边界见 [6.2 交付记录](Settings-6.2.md)。

截图：[实际设置](screenshots/6.2/real-settings.png)、[数据与关于](screenshots/6.2/real-settings-storage.png)、[最小窗口](screenshots/6.2/settings-small.png)、[小窗口底部](screenshots/6.2/settings-storage-about.png)。以下为历史设计。

## 6.1 Sidebar Avatar / Profile Flyout

继续使用 5.5 Design Tokens 与 Windows 11 Minimal Light UI。ui-ux-pro-max 已读取，采用可见焦点、至少 48 DIP 点击区、反馈一致性与紧凑层级规则；本机 Python 不可用，本轮未执行技能检索脚本。

Sidebar 底部固定 34 DIP 圆形头像，点击区 48 × 48 DIP，和导航图标轴线协调；中间导航可滚动。无常驻名称、Chevron 或 Accent 选中条。Hover/Pressed 与展开状态提供轻微 Surface 反馈，Tooltip 绑定 DisplayName。

Flyout 使用 WPF Popup，宽 264 DIP，大头像 72 DIP，圆角与轻边框。高度按内容自适应；Padding 为左右 16、上下 12 DIP，头像到名称、名称到操作区均为 8 DIP，设置项不增加额外空隙。恢复默认隐藏菜单行后即时缩短 36 DIP。最终紧凑版截图位于 screenshots/6.1/compact。自定义头像用圆形 ImageBrush / UniformToFill；默认头像为本地 Nova N 矢量。再次点击头像、外部点击、窗口失活或 Esc 关闭；名称编辑时 Esc 先取消编辑。窗口移动、缩放、最大化及 Sidebar 调宽同步更新锚点。

Tab 可聚焦头像与操作，Enter/Space 打开，名称 Enter 保存；关闭后恢复头像焦点。名称编辑、更换头像、恢复默认和设置均有 AutomationProperties.Name。设置入口关闭 Flyout 并进入占位，不实现设置内容。

截图位于 screenshots/6.1：sidebar-default、profile-default、profile-custom、profile-small、settings-placeholder；real-sidebar / real-profile / real-restarted / real-profile-restarted 来自真实数据库启动与重启。自定义示例图用于验证同步与持久化，测试后真实资料恢复原值。

DesktopBackdrop 仍为 Experimental；300 卡片首次布局约 1.9 秒仍保留在 Performance Backlog。

## 5.5 UI / UX Polish（当前规范）

保留 Windows 11 Minimal Light UI、语义画刷和原导航结构。本节覆盖下文历史阶段的尺寸；未调整 DesktopBackdrop，仍为 Experimental。使用 ui-ux-pro-max 的语义层级、稳定布局、可见焦点、交互目标和减少动画规则；本机 python/python3 不可用，py -3 报告未安装 Python，使用已读取内置规则，未声称检索成功。

| 规范 | 最终值（DIP） |
| --- | --- |
| Workspace | 四边 24；所有页面共用 ContentView 外壳 |
| Section | Header 32 高，标题至内容 Margin 12，Section 底部 28；卡片另有 4 外边距 |
| SectionTitle | 16 / SemiBold / TextBrush，三个标题同一水平起点、同一 Header 高度 |
| ActionText | 12；查看全部至少 88 × 32，默认 MutedTextBrush，Hover/Focus 为 AccentBrush |
| ApplicationName | 13，行高 19，固定两行 38，省略并保留完整 Tooltip |
| SecondaryText | 12 / MutedTextBrush；Hover 统计保留 10.5、每行 15 |
| NavigationText | 14；44 行高、18 图标、30 图标槽、左右 18/12 内边距 |
| ApplicationCard | 所有页面 136 × 136，外边距 4、圆角 12；普通图标 64 |
| Empty State | 图标 32、标题 20、说明 12；图标后 20、说明前 8 |
| Toast | Workspace 右下角，圆角 12、内边距 16/10、字号 13、最大宽度 320 |

首页通过卡片高度 148 → 136、图标 72 → 64、图标与名称间距 12 → 8 收紧整体节奏，保留长名称两行空间。三个区域共享相同标题与卡片资源，不加分割线或独立面板。首页标题在 Workspace 左边距内增加 4，与卡片左边缘对齐；其他页面第一张卡片同样有 4 外边距，首页纵向多出的空间仅来自 Section Header。

普通卡片仅显示图标、名称和星标；Hover/键盘焦点用原 160ms 淡入覆盖层呈现 32 图标、名称、时间及次数，浅 HoverBrush 与 1 DIP 上浮，不改变测量尺寸；关闭动画时沿原资源机制停用上浮。分类入口改为左上 12 DIP Fluent 文件夹图标、28 × 28 点击区域，保留 Tooltip 和“选择应用分类”无障碍名称，默认隐藏、Hover/Focus 出现；原 Popup 与分类命令不变。右上收藏星标保留空心/实心与原交互。

Sidebar 只将重复尺寸替换为语义资源，布局、图标、字号、选中 Accent 条与行为不变。默认/动态分类仍共用 NavigationTemplate；新建分类继续较弱操作态、无选中条，分类组后留 12，设置上方留 16。

纵向 ScrollBar 保留原生 ScrollBar、PART_Track、Thumb、PageUp/PageDown 和 UI Automation。占用宽度 12，默认 Grip 4 / 0.4 不透明度，Hover 6 / 0.75，拖动 1；轨道透明且命中宽度不随 Hover 变化。不修改 ScrollViewer 行为，不引入第三方 UI 库。横向滚动仍禁用。

空状态文案语义不变，所有尺寸来自 ShellTheme。Toast 继续单实例、约 2 秒、160ms 淡入淡出、不获取焦点或拦截输入，不改服务逻辑。高频字号、间距、卡片尺寸、导航槽位和空状态已集中为 Design Tokens；不为一次性标识字号重写全局 UI。

实际 WPF 截图使用隔离演示数据及读取的系统程序图标，不代表真实用户收藏或启动历史：
[首页/普通卡片](screenshots/5.5/home.png) · [Hover](screenshots/5.5/card-hover.png) · [分类/Active](screenshots/5.5/category.png) · [收藏](screenshots/5.5/favorites.png) · [最近使用](screenshots/5.5/recent.png) · [Sidebar Hover](screenshots/5.5/sidebar-hover.png) · [Toast](screenshots/5.5/toast.png) · [空首页](screenshots/5.5/empty-home.png) · [空收藏](screenshots/5.5/empty-favorites.png) · [空最近使用](screenshots/5.5/empty-recent.png) · [最大化](screenshots/5.5/maximized.png) · [最小窗口/窄 Sidebar](screenshots/5.5/sidebar-narrow.png) · [最小窗口/宽 Sidebar](screenshots/5.5/sidebar-wide.png)。

## 5.4 首页信息层级

首页用一个主纵向 ScrollViewer 连续呈现 Recent、Favorite、All，三个区域均复用 ApplicationCardView 与 WrapPanel，自然换行且禁用横向滚动。前两段为空整段折叠；全空沿用现有拖入空状态。Sidebar 设计、宽度与背景系统不变。

标题使用 16 DIP SemiBold、TextBrush；段间距 28 DIP，标题下间距 10 DIP。“查看全部 ›”使用 12 DIP 和 MutedTextBrush，Hover/键盘焦点增强为 TextBrush，沿用 QuietButton 的反馈与焦点样式。颜色、间距和样式集中于 ShellTheme。未添加 Banner、分割线或独立背景面板。

已使用 ui-ux-pro-max 内置一致性、层级、键盘可达和响应布局规则检查。本机 Python 不可用，未声称完成技能数据库检索。实际 WPF 截图：[真实首页](screenshots/5.4/real-home-preview.png)、[空状态](screenshots/5.4/home-empty.png)、[仅全部应用](screenshots/5.4/home-all-only.png)、[多区域](screenshots/5.4/home-sections.png)、[窄窗口滚动](screenshots/5.4/home-small.png)。截图不用于证明 Experimental DWM 合成效果。

## 5.3 最近使用交互

Recent 复用原 ApplicationCardView，大图标、名称、收藏星标、分类弱入口、Hover 统计与 LaunchTimeFormatter 均不改；Sidebar 继续原 E81C 图标、Active 背景与 Accent 条。仅新增专属空状态：32 DIP 同款时钟图标，20 DIP 主文案“最近还没有启动过应用”，12 DIP 辅助文字“启动应用后，它会出现在这里”，沿用 EmptyIconBrush/EmptyTextBrush/MutedTextBrush 和现有居中留白。

按已读取 ui-ux-pro-max 的一致图标、语义颜色、稳定卡片尺寸与明确空状态规则检查，没有创建第二套卡片或改动 Sidebar/背景设计。本机 Python 仍不可用，本轮依据技能内置规则，不声称完成检索。真实 WPF 截图：[空状态](screenshots/5.3/recent-empty.png)、[真实 C/B/A](screenshots/5.3/real-recent-cba.png)、[真实 A/C/B](screenshots/5.3/real-recent-acb.png)、[重启后的顺序](screenshots/5.3/real-recent-restarted.png)。

## 5.2 收藏交互

沿用 136×148 DIP 卡片与现有大图标、名称、Hover 统计布局。右上独立 FavoriteButton：28×28 DIP 桌面鼠标点击区、16 DIP Segoe Fluent E734/E735 空心/实心星标、6 DIP 圆角。未收藏默认 MutedTextBrush 与 0.45 图标透明度，Hover/键盘焦点完整显示；已收藏 AccentBrush 实心星标常显。Pressed 使用 SelectedBrush，聚焦保留 FocusRing，原生 Button 支持 Tab/Enter/Space。没有额外弹跳、旋转或成功 Toast，已有卡片 Hover 动画保持减少动画规则。

分类弱入口移至左上，与星标和中心图标错开；分类浮层及业务保持原样。收藏专属空状态采用 32 DIP 空心星形、“还没有收藏应用”及弱辅助文字“点击应用卡片上的星标即可收藏”。Sidebar 结构、宽度、背景系统不变。

使用 ui-ux-pro-max 的原生独立操作区、可见焦点、动态图标标签、语义画刷和稳定布局规则；本机 Python 不可用，未运行成功技能检索。已检查实际 WPF 截图：[Hover](screenshots/5.2/favorites-hover.png)、[收藏页](screenshots/5.2/favorites-page.png)、[空收藏](screenshots/5.2/favorites-empty.png)、[真实 QQ 重启](screenshots/5.2/real-favorite-restarted.png)。

## 5.1 分类交互

沿用 Windows 11 Minimal Light UI、44 DIP 导航项、小图标、左侧选中指示及既有语义画刷。首页后动态显示数据库分类，默认游戏/应用沿用原图标，自定义项使用 Segoe Fluent 文件夹图标；名称省略并提供完整 Tooltip。“新建分类”采用独立 SidebarActionButtonStyle：44 DIP 行高、0/2 DIP 外边距、18/12 DIP 左右内边距、30 DIP 图标列、18 DIP Fluent Plus 图标和 14 DIP 文字，内容居中，整行可点击。默认沿用 MutedTextBrush 并以 0.97 内容透明度稍降权重；Hover 使用同款 8 DIP 圆角浅色背景、TextBrush 和完整内容不透明度，Pressed 叠加 SelectedBrush 反馈。按钮没有 Accent 选中条，不参与导航选择，始终位于所有分类之后，与下方收藏/最近使用保留 12 DIP 分组间距。点击继续展开已有内联编辑区，显式名称标签、24 字符输入、创建/取消；Enter 提交，Esc 取消。重复错误就近保留，同时复用已有 Toast。

Sidebar 微调截图：[默认](screenshots/5.1/sidebar-refinement/sidebar-action-default.png)、[Hover](screenshots/5.1/sidebar-refinement/sidebar-action-hover.png)。本次仅更改 Sidebar XAML 与新增操作项样式，未更改分类业务、筛选、数据库、Migration、卡片浮层、侧栏宽度或背景系统；继续采用已读取技能中的一致图标、稳定点击区域、语义颜色和键盘焦点规则。

卡片仍为 136×148 DIP，不改变图标、名称或统计区布局。右上“分类”为独立 ToggleButton，Hover、键盘聚焦或浮层打开时显示；Tab 可到达，不依赖鼠标悬停。按钮与主启动按钮为兄弟控件，选择分类不会启动应用。Popup 宽 224 DIP、圆角 10、浅色实底、34 DIP 列表项、高度上限 240 后滚动，包含“未分类”及共享分类列表，左侧指示当前选择。Esc 关闭并返回按钮焦点，点击外部关闭，保存期间禁用列表。动画沿用 HoverDuration 和减少动画规则。

本轮读取 `ui-ux-pro-max/SKILL.md`、quick-reference 的键盘/交互/布局规则与 pro-rules；本机 Python/python3/py 无可用解释器，采用技能内置规则，没有声称完成数据库检索。遵循可见焦点、原生控件、显式标签、错误就近显示、稳定尺寸和统一图标；未更改 MainWindow、背景或侧栏宽度系统。

实际运行截图：[新建分类](screenshots/5.1/new-category.png)、[重复分类](screenshots/5.1/duplicate-category.png)、[游戏筛选](screenshots/5.1/games-filter.png)、[分类浮层（实际屏幕）](screenshots/5.1/category-picker-desktop.png)、[浮层细节](screenshots/5.1/category-picker-detail.png)、[最小窗口](screenshots/5.1/categories-small.png)、[真实 QQ/微信迁移后](screenshots/5.1/real-upgraded.png)。窗口截图来自运行中的 WPF，浮层整体图使用屏幕捕获，不是设计稿；其余 RenderTargetBitmap 图不用于证明 Experimental DWM 效果。

## 4.3 重复导入 Toast

重复导入显示 Workspace 右下角“该应用已添加”。ToastView 位于 Canvas 覆盖层，外边距沿用 WorkspacePadding，不参与卡片区域测量、不拦截鼠标、不获取焦点。使用 WindowBrush、TextBrush、BorderBrush、12 DIP 圆角和 16×10 DIP 内边距，最大宽度 320 DIP。约 2 秒后消失，淡入淡出沿用 HoverDuration（160ms，系统减少动画/高对比度时为 0）。连续重复导入刷新同一条 Toast 的计时，不叠加窗口或队列。

遵循 ui-ux-pro-max 内置的就近反馈、稳定布局、语义颜色和减少动画准则；本机 Python 不可用，使用已读取规则，不声称完成检索。当前仅重复导入接入 Toast，其他状态提示保留原行为。

## 4.2 应用启动与状态

卡片外框保持 136 × 148 DIP、原网格间距和两行名称规则。普通状态仍为 72 DIP 图标与名称。Hover 或键盘聚焦时，固定卡片内部用 160ms 淡入的 DetailsContent 替换 NormalContent：32 DIP 图标、名称、10.5 DIP 次要文字“上次启动”和“启动次数”。两层布局都在原固定 Grid 中，不挤动相邻卡片。完整 DisplayName 的 ToolTip 保留。最终收尾将统计区上间距从 6 DIP 收紧为 4 DIP、统计文本高度从 17 DIP 收紧为 15 DIP，沿用 MutedTextBrush，低于 13 DIP 应用名称的视觉层级。Workspace 不再显示“已添加 X 个应用”，保留导入中、重复项及失败提示。

卡片内部使用原生 Button 和 LaunchCommand，单击、Enter/Space 都触发同一命令，沿用 FocusRing；点击反馈为 SelectedBrush 的 130ms 淡出，不显示成功弹窗。系统关闭动画或高对比度时淡入/点击动画立即完成，并保持语义资源。状态时间在进入和停留时更新；未启动显示“从未 / 0”。

本轮沿用已读取的 ui-ux-pro-max 内置检查：稳定布局、原生交互控件、清晰键盘焦点、次级信息层级、减少动画、错误就近反馈。没有重新设计 Sidebar、TitleBar、Window 或背景系统。4.2 运行截图与测试边界见 Development.md。

## 4.1 应用卡片与拖放（历史基线）

保留窗口、Sidebar、TitleBar 和 Background 系统。Workspace 使用 ItemsControl + WrapPanel，滚动区禁用横向滚动，按可用宽度自动换行。卡片 136 × 148 DIP、外边距 4 DIP、圆角 12 DIP，图标 72 DIP；名称采用固定 19 DIP 行高和 38 DIP 文本区域，Wrap + CharacterEllipsis 最多两行，超出省略；短名称居中，不改变卡片高度。Hover 名称或卡片时 ToolTip 显示完整 DisplayName。卡片为展示项，没有点击启动或按钮语义。

Hover 使用既有 HoverBrush、160ms EaseOut 和 1 DIP 上浮；系统关闭动画或高对比度时不产生上浮。拖入合法类型时 Workspace 轻微高亮边框与背景；取消或移出时复原。不支持文件、失效目标和重复应用显示底部轻提示，4 秒后消失；混合批次仍导入合法项并汇总错误。导入期间显示“正在导入…”，卡片逐个加入；空集合显示原空状态，非空隐藏。

技能检查：读取 ui-ux-pro-max/SKILL.md、quick-reference.md 优先级 1–3 与 pro-rules.md；Python / python3 不可用，py 报告没有安装的 Python，因此采用技能内置规则而非数据库匹配。应用了语义颜色、稳定布局、轻反馈、减少动画和长名称提示。当前按用户限定范围仅开放文件拖放，键盘文件选择入口留待后续授权；未宣称完整无障碍验收。

实际运行截图和复现见 Development.md。以下 3.2 内容保留为历史材质决策记录，其“本轮”指 3.2。

## 3.2 收尾：稳定 UI Shell，Desktop Backdrop 为 Experimental（历史）

当前采用 Windows 11 Minimal Light Glass UI：保留左侧 Logo、六个导航入口、小图标与文字、选中标记和 Hover 动画；右侧改为占满可用空间的 Application Launcher Workspace。本轮只修改 UI Shell，不实现文件拖入、应用启动、文件解析、图标提取、SQLite 或用户系统。

本轮按用户收尾授权提交第三阶段全部 UI Shell 修改；不继续尝试透明 API，不开发业务功能。

## 窗口与侧栏

- 默认窗口 1120 × 720 DIP，最小 800 × 560 DIP，继续使用 WindowChrome；标题栏高 48 DIP。
- 标题栏保留最小化、最大化/还原、关闭按钮，以及原生拖动和双击最大化/还原行为。
- 内容布局初始使用 1*:3* 星号列宽，侧栏默认约 25%。窄窗口下优先满足 210 DIP 最小宽度，因此比例可能略高于 25%。DIP 是 WPF 逻辑像素，125% 缩放下 210 DIP 对应约 263 个物理像素。
- 侧栏右边缘增加 8 DIP 宽的 GridSplitter 命中区域，默认显示轻描边，Hover/聚焦时强调分隔线；支持鼠标拖动和键盘左右键微调。
- SidebarColumn.MinWidth 为 210，MaxWidth 随主布局宽度更新为 35%。拖动后继续使用星号列宽，窗口缩放时重新约束，不把侧栏改为完全固定宽度；本轮不持久化用户调整。
- 侧栏布局未重构，六个导航项与品牌区保留；3.2 删除底部宣传文字，保留空白供未来用户区使用。导航仍只修改 MVVM 选中状态，右侧不再显示导航标题。

列调整方式参考 [WPF GridSplitter 文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-resize-columns-with-a-gridsplitter)。列宽限制属于视图布局逻辑，不进入 ViewModel。

## Application Launcher Workspace

右侧取消原 Dashboard 的顶部介绍、页面标题、底部标语、品牌文字和中央矩形卡片。整个右侧区域作为未来应用图标容器，四周只保留 24 DIP 留白。

空状态在右侧中央显示极简加号与两行文字：

- 主文字：请将文件拖拽至此
- 辅助文字：支持 .exe 和 .lnk

这两行仅用于视觉预览，没有启用 AllowDrop 或任何拖放事件，不表示已经实现格式支持。

## 原生材质与实际结果

采用官方 DwmSetWindowAttribute 的 DWMWA_SYSTEMBACKDROP_TYPE（38），请求 DWMSBT_TRANSIENTWINDOW（3）。Windows 11 将其定义为 Desktop Acrylic，最低支持 build 22621；选择它是为了采样窗口后面的内容，Mica 的壁纸色调融合不能代替桌面透视。

配合 DwmExtendFrameIntoClientArea 的完整玻璃区域、WindowChrome.GlassFrameThickness=-1 和透明 WPF CompositionTarget。不降低 Window.Opacity，不启用 AllowsTransparency，不增加 NuGet。使用 Windows 10/11 兼容性应用清单，保留现有自定义标题栏。

**2026-10-15 实际截图未通过视觉验收：Windows 11 build 26200 上 DWM 返回成功，查询材质值为 3，但 Workspace 仍显示均匀灰色，无法看到壁纸。不能将 API 状态 DesktopAcrylic 解释为视觉生效。** 红蓝背后窗口探针也未透出，不能把原因直接归咎于动态壁纸。尝试的未公开 AccentPolicy 路径同样未通过，已移除，不作为交付实现。根因仍待定位。

背景分层保持：原生 DWM → ShellBackgroundView（fallback 或原生白色覆盖层）→ 较不透明的 Sidebar → 清晰文字/图标。MainWindow 不包含材质分支，布局未重设计。

## 回退与扩展入口

DesktopBackdropController 在不支持的 Windows、关闭 UI 视觉效果/透明效果、高对比度、远程会话、节能、窗口失活、DWM 调用失败或读取偏好失败时，先恢复不透明浅色背景，再禁用原生材质。监听窗口激活、状态变化、系统设置、主题、显示和电源通知，条件恢复后重新请求材质。系统自身也可能覆盖材质效果；成功的 HRESULT 不能检测所有实际渲染问题。

| BackgroundMode | 当前行为 / 入口 |
| --- | --- |
| Desktop（默认） | 请求原生 Desktop Acrylic；不满足条件时使用 BackgroundFallbackBrush |
| CustomImage | 仅保留模式与输入契约，暂不实现图片呈现，使用 SolidColor fallback |
| SolidColor | 保留 SolidBackground（Brush）输入，无输入则 fallback |

仅 Desktop 接入平台实现。没有图片选择、设置保存或业务功能。NOVA_DISABLE_SYSTEM_BACKDROP=1 为进程级诊断开关，可验证安全回退而不更改用户系统设置。

官方依据：[DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)、[DwmExtendFrameIntoClientArea](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmextendframeintoclientarea)、[Acrylic 材质行为](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)。

## 视觉参数

资源集中于 Themes/ShellTheme.xaml。

| 语义 | 值 |
| --- | --- |
| WindowBrush / BackgroundFallbackBrush | #F9FAFC |
| fallback 白色遮罩 GlassTintBrush | #A6FFFFFF |
| 原生材质覆盖层 NativeGlassTintBrush | #D9FFFFFF |
| 侧栏 SidebarBrush | #A8FFFFFF |
| 边框 BorderBrush | #18344058 |
| 主文字 TextBrush | #283446 |
| 次文字 MutedTextBrush | #657083 |
| 空状态文字 / 加号 | #586579 / #8A96A7 |
| 强调色 AccentBrush | #526BB5 |
| Active / Hover | #14617FC2 / #0F324F88 |
| 窗口圆角 / 导航圆角 | 12 / 8 DIP |
| Workspace 留白 | 24 DIP |
| 导航行高 / 字号 | 44 / 14 DIP |
| Hover 时长 | 160 ms，EaseOut；系统关闭动画时为 0 |

继续保留清晰键盘焦点、按钮可访问名称、系统高对比度资源适配。浅色主题是当前默认方向，深色主题不再是本阶段约束。

## 技能使用与验证

已阅读 AGENTS.md、项目工作规范和已安装的 ui-ux-pro-max SKILL.md，执行 light glass 的设计检索和 glassmorphism / WPF resizable 定向检索。通用设计结果中的 Hero / CTA 网页结构不适合启动器，未采用；采用玻璃表面分层、文字对比、轻边框，以及 Star + MinWidth/MaxWidth 的布局建议。没有生成新的网页设计系统或新增依赖。

2026-10-14，Windows 11、125% DPI、SDK 9.0.318：

- Release build：0 warnings、0 errors。
- 实际启动窗口，确认 Workspace 仅有指定两行文字；六项导航及键盘选择正常。
- 实际鼠标拖动 GridSplitter，确认宽度发生变化，并验证 210 DIP 下限、35% 上限和方向键微调。
- 检查标题栏原生拖动命中、双击最大化/还原、自定义最小化/最大化/还原/关闭；最大化后关闭按钮位于显示器工作区内，关闭进程退出码为 0。
- 检查默认、Hover、侧栏拉宽和最小窗口截图；最小窗口六个入口完整可见，空状态保持居中。
- 未测试真实原生背景、用户背景图片、混合 DPI、多显示器或实际切换系统高对比度；这些不作为本轮完成声明。

复现：先 Release build，再执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UiShell.ps1`。脚本会操作本次测试窗口、保存截图、关闭进程并恢复鼠标位置。

![3.1 默认窗口：浅色实色回退](screenshots/launcher-shell-3.1.png)

[Hover](screenshots/launcher-shell-3.1-hover.png) · [侧栏拉宽](screenshots/launcher-shell-3.1-wide.png) · [最小窗口](screenshots/launcher-shell-3.1-small.png)

## 3.2 验证记录

Windows 11 build 26200、125% DPI、.NET SDK 9.0.318：Release build 0 warnings / 0 errors。原生请求与强制 fallback 两条运行路径均通过导航、最小化、最大化/还原、标题栏双击、窗口调整大小、实际鼠标拖动侧栏（210 DIP 至 35%）以及关闭退出检查。没有新增依赖；该轮验证时未提交，后续收尾授权见下文。

强制 fallback 的 DWM 值为 1，界面保持浅色且可读；旧版 Windows、真实切换系统透明效果/高对比度、DWM HRESULT 失败、多显示器未实机测试，不宣称覆盖。桌面截图使用 CopyFromScreen，PrintWindow 不能证明 DWM 合成效果。

复现原生请求：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UiShell.ps1 -DesktopCapture`。

复现回退：上述命令追加 `-ForceFallback`。脚本临时最小化其他窗口，结束后恢复。自动检查只验证 API 状态和交互；截图仍须检查，当时透视目标未通过；本轮按下述 Experimental 决策收尾，不再以壁纸透视作为 UI Shell 完成条件。

![3.2 桌面实拍：原生请求成功但壁纸透视未生效](screenshots/launcher-desktop-3.2.png)

[强制 fallback 实拍](screenshots/launcher-desktop-3.2-fallback.png)
## 3.2 最终收尾决策

Desktop Backdrop 状态正式定义为 **Experimental**。Windows DWM API 已经接入，但在当前 WPF 自定义窗口环境下尚未可靠呈现桌面壁纸。保留 DesktopBackdropController、DwmSetWindowAttribute、DwmExtendFrameIntoClientArea、版本检测与 fallback。未来允许继续研究，但不得阻塞核心功能开发；本轮不尝试新透明 API。

BackgroundMode 固定为 Desktop / CustomImage / SolidColor。Desktop 默认尝试 Windows System Backdrop，调用失败或条件不支持时使用 SolidColorBackground；CustomImageBackground 暂不实现，仅预留扩展契约；SolidColorBackground 是稳定的浅白色 fallback。

fallback 基底统一为 #F9FAFC，继续叠加原白色表面。实验性 DWM 路径使用独立 #D9FFFFFF 白色覆盖层，避免 API 成功但系统呈灰色时出现大面积中灰。此覆盖层是可读性与稳定视觉处理，不意味着桌面透视已生效。Sidebar 保留 #A8FFFFFF 的额外白色表面，文字与图标保持清晰。未改变导航、Logo、图标、Workspace 布局、提示文案、窗口按钮或侧栏拖动行为。

收尾截图另存于 screenshots/final-3.2/，保留此前实验截图作为历史证据。
收尾验证：Release build 0 warnings / 0 errors；默认 Desktop 与强制 fallback 两条路径均通过现有 UI 回归，实际检查截图确认浅白 Surface 与原布局。技能入口已读取；本轮 Python 不可用，未新增技能数据库检索，颜色处理依据技能内置的浅色表面可读性、语义资源和对比建议。

[最终 Desktop 表面](screenshots/final-3.2/launcher-desktop-3.2.png) · [最终 SolidColor fallback](screenshots/final-3.2/launcher-desktop-3.2-fallback.png)
