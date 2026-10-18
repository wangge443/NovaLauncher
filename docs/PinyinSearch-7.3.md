# 7.3 中文拼音与首字母搜索交付记录

2026-10-18；基线 `70d7e6209863bcbbdcb79aff5637047517a3eeb3`。已通过用户验收，按授权提交 feat: add pinyin application search，不开始新功能开发。

## 依赖选择

新增 **ToolGood.Words.Pinyin 3.1.0.4**，固定版本并更新 App / Verification 锁文件。官方包包含 net9.0、net8.0、net10.0、netstandard2.1，无传递依赖；项目实际选择 net9.0。NuGet 下载包 6,708,195 bytes（约 6.40 MiB，含四个目标程序集），实际部署 DLL 1,753,600 bytes（约 1.67 MiB）。MIT 许可副本随 App 输出到 ThirdParty，原许可位于 `NovaLauncher.App/ThirdParty/ToolGood.Words.Pinyin.LICENSE.txt`。

选择原因：独立拼音模块，同时提供全拼/首字母所需音节与常见词组读音，可直接在 .NET 9 使用，2026-02-02 发布该版本；内嵌压缩词典离线读取，应用运行无需网络。未引入 ToolGood.Words 完整过滤组件、UI 或国际化框架。开发还原依赖需要 NuGet 网络或本地缓存。替代方案：只提供首字母的 FirstPinyin 无法满足全拼；旧 .NET Framework 专用库不适合当前目标；自维护字符表会增加字库维护成本。

核查来源：[NuGet 包页](https://www.nuget.org/packages/ToolGood.Words.Pinyin/3.1.0.4)、[官方源码与说明](https://github.com/toolgood/ToolGood.Words.Pinyin)、[转换实现](https://github.com/toolgood/ToolGood.Words.Pinyin/blob/master/csharp/ToolGood.Words.Pinyin/internals/PinyinDict.cs)。下载包 nuspec 对应源码提交 `de86cad8127ee9706b1d4c47d57d63ee4f65155c`。包说明保守标注 U+3400–U+4DB5 / U+4E00–U+9FA5；源码处理到 U+9FD5，部分生僻字无读音。未转换字符不影响 OriginalName 搜索。

## Key、生命周期与排序

`ApplicationSearchEntry`：ApplicationId、OriginalName、NormalizedName、FullPinyin、PinyinInitials。例：QQ音乐 → `qq音乐` / `qqyinyue` / `qqyy`；工作微信 → `工作微信` / `gongzuoweixin` / `gzwx`。只移除空白并转小写，不去掉 `+ . - _ ( )`；英文、数字保留。`Wei Xin` 可匹配 `weixin`，`gz wx` 可匹配 `gzwx`。中文 Query 不转拼音。

Application → DisplayName → Normalize / 一次 GetPinyinList → Search Entry → 内存 Index。加载和导入时生成；DisplayName 通知只替换对应记录；Remove/Replace/Reset 清理旧项并退订；Dispose 清空并退订所有监听。QuickLauncherViewModel 仅新增服务构造与释放，先注册 Index 后注册结果刷新监听，使重命名和导入同一事件内立即生效。Query 变化只做字符串匹配，缓存不保存历次 Query 或旧名称。

SearchQuery → Trim / Normalize → Index → Rank → 最近时间 / 名称 / Id → Top8。Rank 越小越优先：

| Rank | 匹配 |
| --- | --- |
| 0 / 1 / 2 | 原 DisplayName 完全 / 前缀 / 包含 |
| 3 / 4 / 5 | 忽略空白的名称完全 / 前缀 / 包含 |
| 6 / 7 | FullPinyin 完全 / 前缀 |
| 8 / 9 | PinyinInitials 完全 / 前缀 |
| 10 / 11 | FullPinyin 包含 / PinyinInitials 包含 |

同一应用取第一个命中档位，不合并多份候选；同档复用 RecentApplicationComparer（LastLaunchTime 降序、OrdinalIgnoreCase DisplayName、Id）。原名称三档整体优于其余档位，规范名称三档是为了支持 Visual Studio → visualstudio 的补充。Quick Launcher 空 Query 分支保持原 Recent Top6 / Favorites Top6，非空仍最多 Top8。

多音字使用库默认词组/单字读音，不保证应用自定义名称的语义消歧，不枚举全部读音，也没有在线词典、AI 服务或自建例外表。无法转换字符、emoji 保持原文可查。

## 验证与边界

专项使用隔离 SQLite、临时 EXE/LNK 探针和独立数据目录，截图里的微信、记事本、QQ音乐均为测试显示名称，未启动用户的真实应用。测试快捷方式导入走真实解析和 ContentViewModel.ImportAsync；原导入回归另覆盖 OLE 拖放。重命名与移除走原管理命令。不改正式数据库、Schema、Migration、持久化模型。

Release 构建 **0 warnings / 0 errors**。拼音专项 **123 项断言通过**：指定中文/英文/数字/符号/emoji 名称、全拼/首字母、混合名称、大小写/空白、ASCII v 音节、12 档排序及时间/名称/Id 次序、去重/Top8、打开和关闭时重命名、中文快捷方式导入、移除、集合替换/移动/Reset/Dispose、缓存复用、空 Query、IME 数字候选键和 300/1000 应用。

7.2 Quick Launcher 回归 **69 项通过**，包含真实 Ctrl+K、鼠标入口、自动焦点、选择/Enter/Esc/外部点击、背景页面保持、原启动链路、失败保留、原生中文 IME Space/Enter 确认和三种背景。UI、Overlay 代码与样式无修改。三张截图已在淡入完成后检查。

既有功能回归 **439 项全部通过**：Home29、Categories34、Favorites31、Recent22、Settings36、Profile44、Appearance39、Management46、Application Editing59、Launch32、Toast11、Polish19、Import/OLE37；另含既有独立进程检查。本阶段合计 **631 项主进程断言通过**。锁定还原成功，最终 Release Build **0 warnings / 0 errors**。

Favorites 前两次因指针目标 HWND 不等于测试窗口而安全中止，未发送右键；增加失败坐标/HWND/PID/截图诊断后，仅该失败套件继续执行并通过 31 项，未改产品代码或放宽点击保护。瞬时遮挡来源未捕获，不声称确定根因。首张 wx 截图曾处于淡入起点，已调整测试等待动画结束并重新检查三图。最终保留交付截图，清理本阶段临时下载包、日志和回归截图。

## 性能与内存

Windows 交互式桌面，Release，125% DPI；使用 300 个名称为“工作微信网易云音乐工具 0000”等的中文混合条目，七次 Query（wx、gzwx、gongzuoweixin、wangyiyun、yinyue、0009、不存在）。词典冷启动单独测量，不混入逐 Query 耗时。

| 测量 | 300 应用 | 1000 应用（补充） |
| --- | ---: | ---: |
| 已有卡片的独立索引构建（词典已热） | 2.22 ms | 5.06 ms |
| Content 加载与建立索引 | 62.40 ms | 1260.52 ms |
| 单 Query 缓存匹配/排序/Top8 | 0.133–2.119 ms | 0.164–3.385 ms |
| Query 更新、投影及同步绑定 | 1.09–10.52 ms | 2.21–14.90 ms |
| 后续布局/Render yield | 5.53–71.39 ms | 9.85–391.63 ms |
| 三个派生字符串 UTF-16 数据量（不含对象头/原名/字典） | 40,800 bytes | 136,000 bytes |
| 建索引线程分配（含临时对象，非长期占用） | 795,352 bytes | 2,678,456 bytes |

300 应用同次 Query 投影加布局最高 **73.73 ms**，首次结果布局后其余六次为 7.93–16.05 ms。7.2 原 300 应用样例本轮匹配/排序为 0.194–0.884 ms，投影 1.44–20.43 ms、布局 3.83–18.48 ms。查询不转换中文、不重载存储，最多八行；保留底层 Home 全部卡片，未优化其初次布局。1000 应用补充测量有一次 391.63 ms 布局延迟，不能据此宣称 1000 卡片 UI 全程流畅。

词典第一次中文转换 **185.02 ms**，累计分配约 **198.14 MiB**，强制 GC 后堆增量约 **41.17 MiB**（包含运行时/JIT 活动，不是精确独占库内存）；这是一次性词典初始化成本，不是每应用或每 Query 成本。纯英文/数字等非词典范围名称跳过拼音库调用。上述小型 Key 数据量不包含这份共享词典，不将累计分配等同于长期占用。

测试比较 Query 前后的 Entry 引用，确认查询没有重建 Key；重命名只替换目标条目；移除、Reset、Dispose 后 Count 和订阅生命周期检查通过。多音字、第三方 IME、生僻字全集、混合 DPI 不是本轮完整覆盖范围。

复现（交互式桌面，会操作测试窗口、鼠标与输入法）：

```powershell
dotnet restore NovaLauncher.sln --locked-mode
dotnet build NovaLauncher.sln -c Release --no-restore
& .\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --pinyin docs/screenshots/7.3
& .\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --quick-launcher artifacts/7.3/quick
```

## 截图

保持 7.2 的 UI/布局/主题，真实 WPF RenderTargetBitmap 输出。已读取 ui-ux-pro-max，沿用内置焦点与可读性规则检查，不新增设计系统或宣称执行 Python 检索。

- [wx → 微信](screenshots/7.3/pinyin-wx.png)
- [jsb → 记事本](screenshots/7.3/pinyin-jsb.png)
- [qqyy → QQ音乐](screenshots/7.3/pinyin-qqyy.png)

## 最终收尾（已验收）

再次执行 locked-mode 还原成功，Release Build **0 warnings / 0 errors**。核对微信/记事本/QQ音乐的全拼与首字母、中英文直接搜索和名称优先级、重命名/导入/移除索引同步、Quick Launcher 快捷键/IME/Top8/空 Query、缓存复用，均与已通过的验收及测试记录一致。收尾没有修改业务代码，沿用专项 123 项、Quick Launcher 69 项及既有回归 439 项，共 631 项通过记录，不重复执行已通过套件。

确认 ToolGood.Words.Pinyin 3.1.0.4 已锁定，UI、Schema、Migration 和持久化模型无修改；工作区仅含本阶段实现、验证、依赖许可、三张截图与文档。按用户授权创建指定提交，不推送、不开始新功能开发。
