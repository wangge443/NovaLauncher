# Nova Launcher — Agent 工作规范

## 范围与当前阶段

项目目标是 Windows 11 风格的现代桌面应用启动器。当前为第六阶段 6.1：本地个人资料、Sidebar 底部头像与 Profile Flyout；稳定基线 08fe8615b206550cb77dc03fae416549541186cc。允许新增 UserProfiles Migration 与本地头像存储，设置仅提供占位入口。保护第五阶段所有功能，不开发登录、网络账户、多用户或真正设置。DesktopBackdrop 仍为 Experimental。6.1 已通过最终验收，按用户授权完成收尾提交，不开始下一阶段。

本文件是用户指定的项目工作规范。项目级 AGENTS.md 提供自动发现入口并引用本文件，不复制正文。开始任务时阅读本文件、README.md、docs/Product.md、docs/Design.md、docs/Architecture.md 和 docs/Development.md。全局规则仍以当前生效的 canonical AGENTS.md 为准。

## 技术栈规范

- 固定使用 C#、.NET 9、WPF、MVVM 和 SQLite；未经用户明确调整，不替换技术栈。
- 应用项目使用 net9.0-windows 并启用 WPF，SDK 固定为 9.0.318；原生 DWM 背景要求 Windows 11 build 22621+，不支持时回退；未增加 Windows SDK NuGet。
- MVVM 已使用 CommunityToolkit.Mvvm 8.4.2，并提交依赖锁文件；持久化使用 Microsoft.EntityFrameworkCore.Sqlite 9.0.16（传递依赖 Microsoft.Data.Sqlite），均锁定版本。
- 新增依赖说明用途、许可证和替代方案；采用明确版本，禁止浮动版本。SDK 版本在创建工程时固定。
- 应用数据保存到用户本地应用数据目录，不写入安装目录或仓库。

## MVVM 架构规范

- View 负责布局、绑定、视觉状态和动画。code-behind 仅允许窗口生命周期、焦点和纯视图适配逻辑。
- ViewModel 暴露可观察状态和 ICommand，通过注入的接口调用应用服务；不得直接操作 Window、控件、SQLite 或 Process。
- Model 表达应用条目及业务数据，不依赖 WPF。
- 数据访问集中于 Repository；进程启动、图标提取及窗口材质等平台能力通过独立服务封装。
- 依赖从 UI 指向核心接口，基础设施实现核心接口；在应用启动入口完成依赖装配，禁止 Service Locator。
- I/O 不阻塞 UI；可取消操作传递 CancellationToken，UI 集合在 UI 线程更新，避免重复执行启动命令。

## UI 设计规范

- 遵循 docs/Design.md 中的 Windows 11 Minimal Light Glass UI：浅色玻璃分层、左侧导航、右侧完整启动工作区和 Hover 动画；原生材质暂未接入时使用明确的实色 fallback。
- 颜色、间距、圆角和动画参数集中到 ResourceDictionary；使用语义化资源键，避免页面内重复硬编码。
- 区分系统背景材质与普通透明度；不通过降低整个窗口 Opacity 实现玻璃效果，以免文字和图标一起变淡。
- 支持键盘焦点、DPI 缩放、高对比度、关闭透明效果和减少动画；不可只用颜色表达状态。
- ui-ux-pro-max 用于设计建议，接入步骤见 Design.md；其网页框架建议不能覆盖 WPF 技术栈。

## 编码规范

- 使用 UTF-8、4 空格缩进、nullable reference types；类型、方法、属性使用 PascalCase，局部变量与参数使用 camelCase，私有字段使用 _camelCase。
- 接口使用 I 前缀，异步方法使用 Async 后缀；避免 async void（必要的 UI 事件处理除外）及同步等待异步任务。
- 单一职责、明确错误边界；不吞异常，不以空 catch 隐藏失败。错误应有用户可理解的反馈与必要诊断信息。
- SQL 使用参数化查询，写入和迁移按需使用事务。日志不得记录凭据或完整敏感启动参数。
- 注释解释原因与限制；面向用户的文案与内部技术标识分离，为后续本地化保留入口。
- 验证与改动相称：核心行为做单元测试，SQLite 做临时库集成测试，WPF 做实际窗口检查；纯文档阶段只检查文档与 Git 状态。

## Git 提交规范

- 默认分支 main；后续功能分支使用 feat/<name>、修复分支使用 fix/<name>。
- 提交格式为 type(scope): summary，scope 可省略，type 使用 feat、fix、docs、refactor、test、chore 等；示例：docs: initialize project documentation。
- 每次提交围绕一个明确变更，提交前检查 diff 和相关验证结果；不得夹带无关用户改动。
- 不提交 bin、obj、IDE 缓存、运行数据库、日志、凭据和本地配置；通过 .gitignore 排除。
- 不擅自推送、强制覆盖历史或执行破坏性清理。3.2 提交授权只适用于历史 UI Shell 收尾；4.1 已提交为 58bf6dc；4.2 已提交为 480f357；4.3 已授权最终收尾提交，不创建远程仓库或推送。

## AI Agent 工作流程规范

1. 阅读项目规范，检查目录与 Git 状态，确认当前阶段及用户已授权范围。
2. 先说明预期结果，发现缺失信息时先推进不依赖该信息的工作；不重复请求已有授权。
3. UI 工作先检查 ui-ux-pro-max 的安装与指令，形成设计参数和状态方案，再映射为 WPF 资源与控件。
4. 按小范围改动实施，不覆盖用户工作；只有明确独立且有收益的工作流才考虑委派。
5. 执行必要验证，记录未验证项与实际限制，清理本次无用临时文件，并同步相关文档。
6. 用简体中文交付结果、验证情况与必要的下一步；代码、命令和技术标识保持英文。当前只完成 6.1 本地个人资料，提供 Build、测试结果和实际 UI 截图；按用户授权完成 6.1 收尾提交，不开发下一阶段。
