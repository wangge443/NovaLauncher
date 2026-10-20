# Nova Launcher 8.2 — 大量应用性能专项

## 范围与 Baseline

稳定源码基线：`2272435bff1ede0f486bd1e54ea1b19e5a9fd6a1`。本阶段仅修改 Home 的视图创建调度，新增显式诊断与隔离验证；无新增业务功能、依赖、Schema 或 Migration。8.2 已通过用户验收，授权以 `perf: improve large application home rendering` 收尾提交；未进入 8.3。

本机 Windows、.NET SDK 9.0.318、Release，固定 10 / 100 / 300 个应用。记录使用确定的 Id、排序、中文英文名称、每应用独立缓存 PNG（取自本机 notepad.exe）、6 个收藏和 6 个最近使用。目标为不存在的隔离路径，不能启动真实用户程序。数据库与缓存位于 `artifacts/verification/8.2/fixture-*`，使用既有 `.nova-verification` 边界，不修改真实 Nova 数据。

先仅加入计时探针，保存未优化的运行文件到 `artifacts/verification/8.2/baseline-bin`，然后修改渲染。每场景独立进程预热 1 次（run 0），正式 5 次（run 1–5）。正式运行恢复相同 1200×720 窗口；没有清空 OS 文件缓存，故 run 0 仅称 cold-ish，正式数据称 warm OS-cache / fresh process，不能称严格冷启动。历史审计使用不同夹具与测量边界，其 1.87–3.31s 不替代本轮 A/B。

复现：

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
./scripts/Test-Performance.ps1 -Label after
./scripts/publish.ps1 -Mode SelfContained
./scripts/Test-Performance.ps1 -Label self-contained -Counts 300 -Executable ./artifacts/publish/self-contained/NovaLauncher.exe
```

### 测量定义

`--performance-test` 必须同时指定有效的隔离数据根才启用；普通 Release 不采集细分样本、不执行截图/滚动/自动退出。`PerformanceTrace` 汇总后复用 NovaLog 的 `PERF` 分类，JSON 保留结构化结果。

| 节点/指标 | 边界与解释 |
| --- | --- |
| Process Start | OS Process.StartTime 为相对时间零点；ManagedStartup 为进入 App.OnStartup 后 |
| DatabaseInitialized | ApplicationRepository 初始化/迁移检查结束；DatabaseInitialize 为该段持续时间 |
| DatabaseQueryAndEFMapping | EF AsNoTracking 查询与实体映射，包含 SQLite 查询，未伪称二者被单独隔离 |
| ApplicationsLoaded | 记录验证、缓存读取、可用性检查和模型组装完成 |
| ApplicationMaterializationWithIconIO | 上述循环的合计，包含后台 PNG 校验及 File.Exists，不能当作纯磁盘 IO |
| ViewModelsCreated | 卡片 VM、同步图片解码、完整搜索索引与 Home 投影建成 |
| IconDecode | VM 内 BitmapImage.OnLoad + Freeze 累计；是 VM 建立的子区间 |
| HomeProjection | 源集合变化的 Recent/Favorite 更新累计；也是 VM 建立的子区间 |
| HomeDataReady | 数据、Profile、Settings、背景与启动页就绪，马上调用 Show |
| CardXamlConstruction | 每张卡 InitializeComponent 累计；不包含后续模板布局，优化后包含所有批次 |
| MainWindowShown / FirstLayoutCompleted | Show 返回，随后 UpdateLayout 返回；容器已生成并完成初次 Measure/Arrange |
| HomeFirstLayout | Show 前至 UpdateLayout 返回，包含窗口与卡片首次布局，非独立纯 Panel 时间 |
| StartupReady | 首次 ApplicationIdle，先于 SystemIdle 后续批次；代表首屏可响应，不代表所有卡片完成 |
| AllCardsReady | 渐进完成后再执行布局至 ApplicationIdle；含首屏证据截图开销，不是纯渲染时间 |

这些计时是应用级 Stopwatch 探针，**不是 ETW/WPA 的逐调用 CPU profiler**。包含关系不能相加；容器生成与 Measure/Arrange 尚未独立拆成纯耗时。JSON 的时间线与构造耗时一起用于定位，避免仅用进程到截图总时间。

## Profiler Findings

300 项基线是 **312 张卡片**（6 Recent + 6 Favorite + 300 All），没有发现额外重复卡片。单卡已连接视觉后代为 19（不含根和未打开 Popup 的独立树）；每卡仍有一个 ContextMenu、3 个 Popup，菜单含 8 个顶层动作与 3 个分隔项，分类项由 ItemsSource 生成。Popup 虽然关闭，其 XAML 内容对象仍提前创建。不能把“19 个视觉后代”解释为单卡全部对象数量。

300 项全量卡片 XAML 构造中位数 1990.8ms，首次布局 4497.1ms；VM/索引 534.0ms，图片解码 294.5ms，Home 投影 26.2ms。主要可改善的首屏阻塞是卡片创建与布局。菜单/Popup 是构造成本的一部分，但没有独立消融数据证明 ContextMenu 单独占多少，故不宣称它是已定量确认的唯一瓶颈。

图标已有缓存直接读取 PNG，不重新提取 EXE；缺失/损坏时才恢复提取。缓存 IO 与校验在 Task.Run，VM 内从 bytes 同步 OnLoad 解码并 Freeze，Custom Icon 替换创建新实例。当前未设置 DecodePixelWidth：本轮正常缓存图标不是最大瓶颈，保留现有高 DPI 质量；超大自定义图标的解码优化没有纳入本轮已证明收益。

Home 投影和 Quick Launcher 使用原 VM 引用；收藏/统计更新不重建 300 个 VM。完整搜索索引在首屏前已建立，不依赖渲染集合。没有重写拼音算法或增加延迟索引状态机。卡片详情采用现有绑定属性，时间由交互计时刷新，未永久缓存相对时间。未发现需要本轮重写的昂贵布局 Converter，也未批量修改 Binding 模式。

## Chosen Strategy / Rejected Alternatives

| 方案 | 决定 |
| --- | --- |
| A 小幅视觉树优化 | 审计到提前创建菜单/Popup；暂不改其名称作用域、焦点和管理交互，缺少独立消融证据 |
| B Progressive Rendering | 采用：Home All 超过 100 项时首批 40，随后 SystemIdle 每批 20 |
| C 真正 VirtualizingWrapPanel | 未采用：外层单 ScrollViewer + 三个 Section，需要重做滚动协议、回收、Wrap 测量和键盘定位，风险超出首屏问题的最小改动 |
| D 原生 VirtualizingStackPanel | 未采用：直接替换会失去现有 Wrap 布局；按行分组增加 Resize/焦点/实时变化复杂度 |

不使用第三方 Panel，无新增包与许可证。Recent/Favorite 继续原有简单实现。**这不是真正虚拟化**，最终视觉数量仍为 312；不承诺降低全量布局总成本。

## Implementation

`HomeView` 持有仅用于视图呈现的 ObservableCollection，不移动业务数据或 VM 职责。DataContext 接入时选首批，Loaded 后排队；Unloaded/DataContext 切换取消 DispatcherOperation 并解除源订阅。返回 Home 重新建立稳定前缀。源集合变更按对象引用增删/移动，维持顺序与存活容器；重命名/图标/收藏/统计继续直接通知同一 VM。

<=100 项同步完整呈现，不引入批次等待。300 项首屏为 52 卡（40+6+6），最终 312。批次低于输入、Render、ApplicationIdle 优先级；每次仍会占用 UI 线程，并不保证单帧 16ms 或滚动始终 60fps。

## Before / After

以下为五次正式运行 **Median，ms**；内存为稳定强制 GC 后 MiB。改进率为 `(Before-After)/Before`，负数代表回退，未选择最好单次。

| 指标 | 10 Before → After | 100 Before → After | 300 Before → After |
| --- | --- | --- | --- |
| Database Init | 757.2 → 738.7 | 742.1 → 758.8 | 744.5 → 722.9 |
| Application query / EF mapping | 293.5 → 281.1 | 293.4 → 295.8 | 304.5 → 304.5 |
| Materialization + Icon IO | 46.3 → 53.6 | 188.7 → 205.2 | 599.5 → 530.4 |
| ViewModel + index build | 221.4 → 219.8 | 300.1 → 297.0 | 534.0 → 550.8 |
| Icon decode（上项子区间） | 11.2 → 13.0 | 98.8 → 95.9 | 294.5 → 286.1 |
| Home First Layout | 566.7 → 575.0 (**-1.5%**) | 1760.7 → 1625.8 (**+7.7%**) | 4497.1 → 1103.4 (**+75.5%**) |
| Startup Ready | 2587.0 → 2637.6 (**-2.0%**) | 3957.3 → 3928.1 (**+0.7%**) | 7494.2 → 4012.5 (**+46.5%**) |
| Managed memory MiB | 51.34 → 51.33 | 75.15 → 75.16 | 128.07 → 128.01 |

10 项有约 8ms 布局与 51ms Ready 中位数增加，区间重叠，没有额外批次等待；用户验收确认约1.5%的布局差异属于测量波动，不视为性能回退。100 项首屏更快。300 项首屏达成约 40% 改善与 <=1.5s 目标，但**全量完成变慢**。

### 300 项 Min / Median / Max（ms）

| 指标 | Before | After |
| --- | --- | --- |
| Home First Layout | 4428.0 / 4497.1 / 5088.1 | 975.8 / 1103.4 / 1380.4 |
| Startup Ready | 7318.6 / 7494.2 / 8025.3 | 3787.3 / 4012.5 / 4379.8 |
| All Cards Ready | 初始 Ready 时已全量完成 | 7670.2 / 8250.3 / 9047.5 |
| Scroll 30 steps | 215.9 / 264.2 / 324.4 | 274.9 / 322.0 / 350.0 |
| Resize 20 steps | 1115.0 / 1231.6 / 1377.3 | 1260.7 / 1344.9 / 1533.8 |

滚动采用 30 次 offset + Dispatcher 布局，Resize 为 20 次窗口宽度变更，非人手连续拖动的帧率测试。全量加载后 Scroll 中位数 +21.9%、Resize +9.2%，不能宣称滚动/Resize 提速。Scroll CPU 中位数 375 → 500ms；两边 Scroll LayoutUpdated 为 30，Resize 为 60。WrapPanel 仍保留所有子项，Resize 仍须重新安排全量；LayoutUpdated 数不是逐个 Measure 调用数。

## Memory

300 项稳定托管内存 134,290,568 → 134,229,576 bytes，约 -0.05%；100 项与 10 项亦无明显增长。300 个不同 BitmapSource 被 312 张卡复用，不为重复 Section 解码新图片。该数字统计存活 VM 引用的 BitmapSource，不是底层 WIC/GPU 分配数量。

原始 JSON 保留 Gen0/1/2、WorkingSet、交互前后 GC 差值。强制 GC 用于稳定内存读数，计数含此步骤，不用这些数据宣称生产运行的 GC pause 分布。渐进方案主要改变时间分布，不解决全部卡片常驻内存。

300 项累计 Gen0/1/2 中位数分别为 60/46/33 → 61/47/34；WorkingSet 中位数 628,604,928 → 617,160,704 bytes。独立导航压力测试先在渐进期间 Move/Remove 一项，再循环 Home → Settings → Category → Favorites → Recent → Home 五次：最终卡片恒为311（299+6+6），5 个旧 Home WeakReference 全部可回收。五轮强制 GC 后内存为116.07/116.81/116.64/116.64/116.64 MB（十进制），未线性增长。每轮20步侧栏宽度调整为205.1/269.9/347.9ms（Min/Median/Max）；这是无缓存图标的独立交互夹具，不混入前述启动 A/B。

## Background 与 Self-contained

300 项同一夹具设置 Default、CustomImage Blur0、CustomImage Blur24；自定义图为固定1920×1080合成 PNG，遮罩保持用户值0.72，没有降低Blur。每组预热1次、正式5次。下面为Median（ms）：

| 场景 | First Layout Before → After | Ready Before → After | Scroll Before → After | Resize Before → After |
| --- | --- | --- | --- | --- |
| Default | 4497.1 → 1103.4 | 7494.2 → 4012.5 | 264.2 → 322.0 | 1231.6 → 1344.9 |
| CustomImage Blur0 | 3629.7 → 923.5 | 6731.3 → 4049.7 | 235.9 → 197.1 | 1213.6 → 1354.5 |
| CustomImage Blur24 | 3656.5 → 941.2 | 6732.6 → 3881.1 | 289.8 → 183.1 | 1248.9 → 1227.5 |

背景加载中位数约9–12ms；Blur24相对Blur0未显著增加本轮首次布局，不是主要CPU启动瓶颈，故不改背景。场景顺序未随机化、系统缓存未控制；不能从CustomImage比Default快推断Blur能加速。没有GPU帧时间，不以此证明所有设备上Blur没有滚动代价。

重新生成 `artifacts/publish/self-contained`，不是 `dotnet run`。300 项5次 **Min/Median/Max**：

| 指标 | Self-contained |
| --- | --- |
| Home First Layout | 897.4 / 924.2 / 991.1ms |
| Startup Ready | 3924.8 / 4107.4 / 4161.7ms |
| All Cards Ready（含首屏截图） | 6910.4 / 7067.6 / 7297.7ms |

未另建优化前SCD性能组，不将FDD Before到SCD After作为严格同运行时改善百分比。严格改善百分比取上面的Release A/B；新SCD确认收益存在。

## 验证与证据

用户已确认本阶段验收通过。收尾再次执行 Release Build：0 warnings / 0 errors（`artifacts/verification/8.2/build-closeout.log`）；核对以下726项自动断言与12项SCD Smoke通过记录，收尾未修改已验收实现，不重复运行完整测试。已知全量约8.25秒、Scroll/Resize未明显改善及未完成Clean Machine Test继续保留。

现有 19 个自动测试入口全部通过，含新增 3 项渐进断言，共 **713 项主进程断言**。新增检查证明首批40、Quick Launcher能搜索第300项、切页后不继续追加；原有 Home 检查仍确认最终全量、源删除/Move/Reset、单 scroller 和无横向溢出。

补充 `--performance-behavior` **13 项通过**，总计 **726 项主进程断言**，另有SCD完整发布Smoke **12项通过**。Smoke验证实际发布进程、第二实例恢复/激活、强制结束后重启、真实OLE导入与隔离LaunchProbe、分类/收藏/最近使用、右键重命名、Ctrl+K全拼、自定义图标/Profile/背景及重启恢复；真实用户数据目录文件元数据保持不变。发布包内容检查通过，未混入数据库、日志或真实用户资源。

最终 `dotnet build NovaLauncher.sln -c Release --no-restore`：**0 warnings / 0 errors**，证据 `artifacts/verification/8.2/build-final.log`。SCD发布脚本 warnings-as-errors 成功，记录 `publish.log`。Migration目录、DbContext、Entity及版本文件均无变更。

覆盖 Home/Category/Favorites/Recent、真实 OLE 拖放、Launch、Hover、ContextMenu/分类子菜单、Rename、Custom Icon/Restore/Remove、Profile、Settings/Window Restore、Default/DesktopWallpaper/CustomImage、Quick Launcher/Pinyin、迁移、写入安全、日志。测试为交互桌面自动化与隔离服务检查，不冒充用户手工验收。

原始结果位于 `artifacts/verification/8.2/{before,after,regression}`。Import 测试参数曾被 PowerShell 展开为单字符目录 `D`，测试断言成功，生成截图已移回 `regression/import`，临时 `D` 目录已清理。

已检查真实 WPF 截图（1200×720，96 DPI RenderTargetBitmap；不用于证明DWM效果）。首屏与全量截图视口保持顶部，区别主要为滚动条长度；卡片完成数量由JSON/自动断言证明：

- [300项首屏，52张视图](screenshots/8.2/home-first.png)
- [300项加载完毕，312张视图](screenshots/8.2/home-full.png)
- [300项CustomImage Blur24](screenshots/8.2/home-blur24.png)

## Known Limitations

- 不是真正虚拟化；首次40项之后的批次仍可能产生可感知的 UI 占用，全部加载较基线晚。
- 滚动与 Resize 未明显改善；用户已确认此限制，本阶段验收通过。
- 没有 ETW/GPU 帧时间，也未单独分离 ContextMenu、EF 映射、容器生成与 Measure/Arrange 的纯成本。
- 未验证所有高 DPI、多显示器、超大自定义图标与500项压力场景。
- 真实 Clean Machine Test **未完成**，仍为正式对外发布前 Release Gate；主要交付为 Self-contained win-x64 Portable Preview，Single-file 仍仅可选/部分验证。
