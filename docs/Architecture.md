# Nova Launcher — 架构设计

## 当前边界

第二阶段已创建 NovaLauncher.sln 和单个 NovaLauncher.App WPF 项目，目标框架为 net9.0-windows。当前只包含可启动窗口和基础 MVVM 结构，没有业务功能、数据库或最终 UI。

## 已创建的项目结构

```text
NovaLauncher.sln
global.json                         # 固定 SDK 9.0.318
.gitignore                          # 排除构建产物及本地数据
NovaLauncher.App/
├── NovaLauncher.App.csproj          # WPF / nullable / implicit usings
├── packages.lock.json              # NuGet 依赖锁定
├── App.xaml
├── App.xaml.cs                     # 启动装配与窗口生命周期
├── AssemblyInfo.cs                 # WPF 默认主题资源查找配置
├── Views/
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── ViewModels/
│   ├── ViewModelBase.cs
│   └── MainWindowViewModel.cs
├── Models/                         # 后续业务模型
├── Services/                       # 后续应用服务
├── Repositories/                   # 后续持久化抽象及实现
├── Data/                           # 后续数据库初始化与迁移
├── Resources/                      # 后续图片等静态资源
├── Themes/                         # 后续样式与主题资源字典
└── Helpers/                        # 按需添加小型辅助逻辑
```

七个尚无实现的目录通过 .gitkeep 保留；不添加空服务类或占位业务接口。实际数据库仍应写入用户数据目录，不能写入源码 Data 目录。

## 当前 MVVM 与依赖

App.OnStartup 创建 MainWindowViewModel 并设置 MainWindow.DataContext，再显示主窗口。App.xaml 不使用 StartupUri，避免重复创建窗口；ShutdownMode 为 OnMainWindowClose。

ViewModelBase 继承 CommunityToolkit.Mvvm 的 ObservableObject，为后续属性变更通知提供基础。MainWindowViewModel 暴露只读 Title 和 StatusMessage，分别绑定窗口标题及两行文本。当前没有可执行动作，因此不添加示例命令。MainWindow 的 code-behind 只调用 InitializeComponent。

唯一直接 NuGet 依赖为 [CommunityToolkit.Mvvm 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2)，采用 MIT 许可，用于复用属性通知及后续命令能力，避免自行维护重复实现；当前目标框架下没有传递包依赖。未添加 DI 容器、SQLite 驱动、导航框架或第三方控件库。

当前使用标准 WPF 窗口和系统默认样式，只有 Nova Launcher 与 Initialization Complete 两行文字。Design.md 的玻璃材质和最终视觉方案尚未实施；产品目标仍为 Windows 11，最低系统版本和材质 API 在后续视觉阶段确定。net9.0-windows 不表示已验证旧版 Windows 的兼容性。

## 模块与依赖

| 计划模块 | 职责 | 允许依赖 |
| --- | --- | --- |
| NovaLauncher.Core | 应用模型、应用用例、服务及 Repository 接口 | .NET 基础类型 |
| NovaLauncher.Infrastructure | SQLite、进程启动、文件与图标访问 | Core |
| NovaLauncher.App | WPF Views、ViewModels、主题资源、窗口材质适配和启动装配 | Core；启动装配处引用 Infrastructure |
| NovaLauncher.Tests | 核心与 ViewModel 行为、SQLite 集成验证 | 被测模块 |

以上是后续扩展方案，Core、Infrastructure 和 Tests 项目当前均未创建。现阶段使用 App 内目录组织职责，后续随业务复杂度再拆分；届时 Core 不引用 WPF 或 SQLite，ViewModel 不引用基础设施实现，应用启动入口负责连接接口与实现。

## MVVM 与数据流

View 通过 Binding 显示 ViewModel 的状态，通过 ICommand 触发操作。ViewModel 调用应用用例和服务接口，Repository 负责持久化，结果返回 ViewModel 更新绑定属性。code-behind 仅处理与控件或窗口生命周期直接相关的视觉行为。

加载流程：ViewModel 请求应用列表 → Repository 查询 SQLite → 返回模型 → UI 线程更新可观察集合。大型列表后续按实测需要引入分页、虚拟化或图标缓存。

启动流程：用户激活卡片 → LaunchCommand 检查执行状态 → 启动服务校验目标并启动进程 → 返回成功或结构化错误 → ViewModel 更新反馈。首期范围以 Product.md 为准，仅支持本地 exe；快捷方式、URI 和打包应用留待后续扩展，不默认支持任意命令脚本。

## 接口职责建议

- IAppRepository：查询、保存、删除应用条目，隔离 SQLite。
- IAppLaunchService：封装进程启动、目标有效性检查与错误结果。
- IIconService：提供图标获取和缓存能力；WPF 图像转换保留在 UI 适配层。
- ISettingsRepository：持久化后续确认的用户设置。
- IWindowMaterialService：留在 WPF 平台层，管理材质能力检测、应用与实色回退，不放入业务模型。

## SQLite 数据规划

建议将数据库放在 %LocalAppData%\NovaLauncher\nova-launcher.db；仓库和安装目录不保存运行数据。

| 候选数据表 | 字段意图 |
| --- | --- |
| Applications | Id、DisplayName、ExecutablePath、Arguments、WorkingDirectory、SortOrder、CreatedAtUtc、UpdatedAtUtc |
| Settings | Key、Value；设置项确定后再定义类型与校验 |

收藏或分类经确认后再扩展字段或关系。图标优先缓存为用户目录下的文件，数据库只保存必要引用；具体缓存键和失效策略在实现时确定。

数据库访问采用参数化 SQL，连接按明确生命周期释放，写入按需串行化并使用事务。迁移显式记录版本，迁移前备份，失败时保留原始数据并给出错误，不通过删除用户数据库恢复启动。SQLite 驱动的异步能力需要验证，不能仅因方法名带 Async 就认为不会阻塞 UI。

## 线程与错误边界

文件访问、图标提取和数据库操作避免阻塞 UI；可取消操作传播取消信号。ViewModel 的绑定集合更新回到 UI 线程，启动操作执行期间限制重复触发。

服务区分路径失效、访问失败、数据库失败与操作取消。UI 显示可理解的反馈，诊断日志记录必要上下文，不记录敏感参数。关闭窗口时取消后台任务并释放资源。

## 后续验证与决策

业务开发前评审首期功能，视觉开发前确定最低 Windows 版本、材质实现路径和相关依赖版本。材质方案需要验证窗口拖动、缩放、最大化、DPI 变化及关闭透明效果时的行为。

后续业务阶段验证 ViewModel 命令状态和错误反馈、启动服务结果映射、SQLite CRUD 与迁移回滚，再在真实 WPF 窗口检查布局和交互。

第二阶段验证（2026-10-14）：SDK 9.0.318、WindowsDesktop Runtime 9.0.20；锁定还原通过，Release 构建 0 警告、0 错误。实际启动生成的 exe，通过 Windows UI Automation 确认窗口标题及两行绑定文本均正确；关闭主窗口后进程以退出码 0 结束。此次未验证最终 UI、业务逻辑或发布包。
