# 6.5 应用管理完善（已验收）

基线为 `944776a7071a3c46e688c071ab373b278274ab64`。本阶段实现重命名、自定义图标、恢复默认图标与从启动器移除，已通过验收，按用户授权以 `feat: add application management actions` 收尾提交。保持 6.4 菜单视觉与背景系统，实时 DesktopBackdrop 继续 Experimental。

## 实现与数据

- 新增 Migration `20261017143447_AddCustomApplicationIcon`，仅增加 `Applications.CustomIconPath` nullable TEXT；旧 Migration 未改。名称直接复用 `DisplayName`，Trim 后限制 1–48 字符，按字段更新，不覆盖收藏/分类/统计；加载和重新提取图标不更新名称。
- `CustomIconPath` 为 Nova 自有 PNG 的绝对路径。保存位置为 `%LOCALAPPDATA%\NovaLauncher\CustomIcons\<ApplicationId:N>-<VersionGuid:N>.png`，独立于 `Cache\Icons`。版本文件先落盘、Flush、改名，再更新数据库；失败清理候选，成功清理旧版本。进程在文件落盘与数据库提交之间被中断时可能留下孤立文件，不损坏旧指针；本轮未引入全目录清扫。
- PNG/JPG/JPEG/BMP/ICO 转 PNG，长边最多 256 像素；文件限 50 MB，解码后像素数限 3200 万。ICO 选择最大帧。使用 `BitmapCacheOption.OnLoad`、释放流及 Freeze，不持有源文件或自有图标的文件锁。
- 图标优先级：`CustomIconPath` → `IconCachePath` → EXE 重新提取 → Nova 默认矢量图标。缺失/损坏自定义图标仅回退显示，保留路径让用户可明确恢复默认。恢复默认先解析自动图标，再清空数据库指针并更新共享 ViewModel，清理失败只记日志。

## 交互与安全

Application Context Menu → Rename / Custom Icon / Restore Icon / Remove from Launcher。新增项直接分组排列；恢复默认仅在存在自定义图标时显示；移除始终位于最后。菜单透明度未调整。

重命名使用卡片内部 Popup，默认选中已有名称，输入框 Enter 保存、Esc 取消，非法名称禁止提交。失败保留旧名称并显示“名称保存失败”。图标选择取消不变；非法格式/损坏图片/保存失败通过现有 Toast 告知。

移除使用内部确认 Popup：“从 Nova Launcher 移除此应用？”、“不会删除电脑中的程序或文件。”，默认焦点在取消。确认后等待正在执行的启动/收藏/分类写入，并禁用新写入；SQLite 删除成功才移除内存条目。失败保留 UI 和图标并提示“无法移除该应用”。

**Remove from Launcher ≠ Delete Application。** 删除服务仅接收应用 ID 对应的确定性自动缓存，以及严格校验目录、应用 ID、版本 GUID 的自定义资源路径；不会删除 EXE、LNK、安装目录或调用卸载程序。文件清理失败不撤销数据库删除。Category 本身及其他应用保持不变。

Home、Category、Favorites、Recent 共用 ApplicationCardViewModel，属性通知和集合事件立即同步名称、图标和移除结果。移除同时释放内存目标路径去重键；数据库条目已删除，因此再次导入同一 EXE/LNK 会生成全新 ID，采用默认分类/收藏/统计。

## 验证

Release Build：**0 warnings / 0 errors**。既有回归 **435 项主进程断言通过**：管理46、分类34、收藏31、首页29、设置36、背景39、Profile44、最近22、启动32、Toast11、Polish19、Migration30、Persistence25、Import/OLE37。

6.5 专项 **59 项主进程断言通过**，另有独立进程恢复检查；结合既有回归及真实迁移，共 **498 项主进程断言通过**。专项覆盖重命名校验与键盘、跨页面同步、原生 PNG/ICO 选择器、JPG/JPEG/BMP 连续替换、取消及失败保留、缺失/损坏回退、恢复默认、确认/取消/失败移除、SQLite 行删除、分类保留、图标清理及重导入。额外验证文件被锁导致清理失败时仍可恢复默认，以及移除等待在途启动统计、阻止新写入且不会复活记录。独立进程验证删除源图后名称和自定义图标仍能恢复。真实测试 EXE 和 LNK 移除后仍存在且 SHA256 一致，LNK 目标程序也保留。

真实数据库迁移 **4 项断言通过**：旧 Applications 全字段、Categories/UserProfiles/AppSettings 全字段，以及图标/头像/背景/自定义资源哈希一致；Migration 已应用且模型快照匹配。迁移前备份：

`%LOCALAPPDATA%\NovaLauncher\Data\Backups\nova-before-app-edit-20261018T025507Z-fe20d09c1839423b913b80d1a611d81b.db`

专项使用隔离库和测试应用副本，真实库仅迁移。真实 UI 自动化覆盖鼠标/键盘菜单、原生文件选择器和内部 Popup，不代表已覆盖混合 DPI、多屏拔插或系统高对比度切换。技能已读取；Python 查询不可用，采用本地规则审核。测试曾修正无内嵌图标的 LaunchProbe 缓存前提，以及备份目标须预先创建的问题，未改变背景或旧数据。

复现：

```powershell
dotnet build NovaLauncher.sln -c Release --no-restore
.\NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe --app-edit docs/screenshots/6.5
```

`--real-app-edit-migration` 会先备份再迁移真实数据库，已执行成功，无需重复。常规回归使用对应 `--management`、`--settings`、`--appearance` 等模式和独立截图目录。

## 实际窗口截图

- [右键菜单](screenshots/6.5/context-menu.png)
- [重命名](screenshots/6.5/rename.png)
- [自定义图标与首页同步](screenshots/6.5/custom-icon.png)
- [移除确认](screenshots/6.5/remove-confirm.png)
