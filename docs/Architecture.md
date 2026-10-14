# Nova Launcher — 架构设计

## 当前边界

技术栈固定为 C#、.NET 9、WPF、MVVM 和 SQLite。本文件描述计划架构，不代表已创建模块或数据库。当前不创建 .sln、.csproj、C# 或 XAML 文件。

## 模块与依赖

| 计划模块 | 职责 | 允许依赖 |
| --- | --- | --- |
| NovaLauncher.Core | 应用模型、应用用例、服务及 Repository 接口 | .NET 基础类型 |
| NovaLauncher.Infrastructure | SQLite、进程启动、文件与图标访问 | Core |
| NovaLauncher.App | WPF Views、ViewModels、主题资源、窗口材质适配和启动装配 | Core；启动装配处引用 Infrastructure |
| NovaLauncher.Tests | 核心与 ViewModel 行为、SQLite 集成验证 | 被测模块 |

以上是后续建议的工程划分，本次不创建目录骨架。Core 不引用 WPF 或 SQLite。ViewModel 不引用基础设施实现；应用启动入口负责连接接口与实现。

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

编码前确定首期功能、最低 Windows 版本、材质实现路径和依赖版本。材质方案需要验证窗口拖动、缩放、最大化、DPI 变化及关闭透明效果时的行为。

工程创建后，验证 ViewModel 命令状态和错误反馈、启动服务结果映射、SQLite CRUD 与迁移回滚；再在真实 WPF 窗口检查布局和交互。当前阶段仅验证文档链接、技术栈一致性、文件范围和 Git 初始化结果。
