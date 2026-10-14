# ui-ux-pro-max — Codex 接入记录

## 安装信息

- 核验日期：2026-10-14。
- 来源：https://github.com/nextlevelbuilder/ui-ux-pro-max-skill
- 固定 commit：`7246805992a42b7bba37d4cac83b01416f0c6c44`。
- 上游目录：`.claude/skills/ui-ux-pro-max`，包含 SKILL.md、references、scripts 与 data。
- 许可证：MIT；上游 LICENSE 已保存到安装目录。
- 安装目录：`C:/Users/王倚烽/.codex/skills/ui-ux-pro-max`，属于用户级技能，不提交第三方技能副本到项目。
- 安装方式：Codex 自带 skill-installer 的 install-skill-from-github.py，使用固定 ref、Git 下载及 Python UTF-8 模式。
- 运行环境：Python 3.12.5；通过 `py` 启动，无额外 Python 包依赖。

## Codex 配置

SKILL.md 保留原有 name 与 description；将上游 `${CLAUDE_PLUGIN_ROOT}/.claude/skills/ui-ux-pro-max/scripts/search.py` 替换为本机安装目录下脚本的绝对路径。除此之外，技能指令、数据与脚本保持上游版本。

项目 AGENTS.md 引用本记录与 Agent.md，UI/UX 任务开始时读取技能。技能安装在当前会话已列出的 Codex 用户技能根目录；下一轮对话可自动发现新技能。本轮已直接读取安装后的 SKILL.md，验证文件访问和辅助工具执行，不把本轮读取等同于已经观察到下一轮的自动发现结果。

## 验证记录

2026-10-14 已确认入口文件及 frontmatter 可读，脚本路径已完成 Codex 适配。执行以下只读命令成功（退出码 0）：

```powershell
py -X utf8 -B "C:/Users/王倚烽/.codex/skills/ui-ux-pro-max/scripts/search.py" "keyboard focus" --stack wpf -n 1
```

返回 `stacks/wpf.csv` 的一条匹配，主题为键盘导航，符合指定 WPF 技术栈。此次仅验证本地检索，不执行 --design-system、--persist 或 UI 生成操作，不保存检索中的代码示例。

## 使用边界与迁移

当前仅接入技能。后续以 docs/Design.md 为项目设计依据；技能提供建议，不覆盖 C#、.NET 9、WPF、MVVM、SQLite 的既定技术栈。WPF 查询显式传入 --stack wpf，设计参数变更经评审后更新项目文档。

在其他机器上，使用安装器按同一 commit 和上游目录安装到该机器 Codex 技能目录，重新适配 SKILL.md 中的绝对脚本路径，保留 LICENSE，并重复只读验证。更新技能时记录新 commit、适配差异与验证结果；不自动跟随 main。
