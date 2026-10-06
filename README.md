# Codex Pet Focus

### An offline Windows focus timer beside your Codex pet

**Turn today's plan into timed tasks and keep the active task and elapsed time beside the native Codex desktop pet. Manage tasks locally, with no network access or model calls during everyday use.**

**把今日计划变成可计时的任务，让当前任务与用时显示在 Codex 原桌宠旁。日常通过 Windows 本地窗口管理任务，无需联网或调用模型。**

[English](README_EN.md) | 简体中文

[使用发布包](#使用发布包) · [从源码构建](#从源码构建) · [兼容性与限制](docs/compatibility.md)


![codex-pet-focus](docs/images/cartoon-infographic.png)

今日计划界面保留深色薄荷绿外观，提供明确的新增任务标签、按下反馈和可见键盘焦点。窗口缩小或显示缩放增大时可滚动访问任务与设置。

The daily planner keeps its dark mint identity with a visible task-input label, pressed feedback and keyboard focus. Scrollable content keeps tasks and settings reachable in smaller windows or at higher display scaling.

## 当前功能

- 今日计划逐行加入任务；启动、暂停、继续、完成。
- 单任务计时，切换任务自动暂停上一项。
- 今日完成数量、累计用时、昨日未完成任务一键转入。
- 运行任务的文字横幅跟随原桌宠；不绘制另一只宠物。
- 锁屏、睡眠、退出时暂停；重开后手动继续。
- 每 5 秒检查点、原子保存及上一份备份。

**自动移到屏幕中心、自动跳跃及归位暂不开放。** 当前版本窗口移动失败，原生跳跃实验也尚不足以证明可靠触发。详见 [兼容性报告](docs/compatibility.md)（发布包内为 COMPATIBILITY.md）。不会用浮窗上下跳动替代原生动画。

## 使用发布包

1. 将 ZIP 解压到长期保留的目录。
2. 双击 codex-pet-focus/app/CodexPetFocus.App.exe。
3. 输入今日计划，每行一个任务，点击“加入今日”。
4. 点击任务“启动”，桌宠可定位时显示任务横幅。
5. 关闭面板后可通过托盘再打开；托盘“退出”结束助手。

需要 Windows x64 和 **.NET 10 Desktop Runtime**，当前开发机已安装。首次准备运行时可能联网，助手正常运行不需要联网。

快捷方式脚本在源码仓库的 `plugins/codex-pet-focus/scripts/create-shortcut.ps1`，发布包中位于 `scripts/create-shortcut.ps1`。运行脚本可创建桌面快捷方式。开机启动默认关闭，可在助手设置中开启；设置指向当前程序路径，移动程序后需重新设置。

## Codex 插件

plugins/codex-pet-focus 包含 .codex-plugin/plugin.json、一个简短技能和本地脚本，用于安装、启动、诊断。每日任务操作在本地界面完成。

发布包包含完整插件目录，可通过 Codex 本地插件市场安装。仅双击助手也可使用离线功能，无需每次发消息调用技能。主动与 Codex 对话仍消耗 token。

## 数据与恢复

数据为 %LOCALAPPDATA%\CodexPetFocus\focus.json，上一份备份为 focus.json.bak。主文件损坏时尝试恢复备份，并保留损坏文件；两份都不可读时停止加载，不覆盖为空数据。

时长按实际运行片段累计，使用单调时钟。午夜按本地日期拆分统计。异常退出最多丢失最近 5 秒未保存时间，恢复后暂停。普通电脑空闲不暂停计时。昨日转入创建今日任务副本，历史时长保留在原任务，不复制到新任务。

退出助手后移除程序目录即可卸载；先关闭开机启动，再移除创建的快捷方式。更新与卸载保留数据目录。

## 从源码构建

安装 .NET 10 SDK，运行：

    powershell -NoProfile -File tools/build.ps1

如果存在 .tools/dotnet/dotnet.exe，脚本优先使用项目内 SDK。本项目无第三方 NuGet 包；NuGet.Config 清空包源。输出 ZIP 在 artifacts。

## 验证范围

当前版本的窗口定位与动作限制见 [兼容性报告](docs/compatibility.md)。本机已验证自定义宠物与副屏负坐标；其他缩放、拔屏、真实锁屏睡眠等场景仍需在对应环境补测。

## 许可

MIT。实现来源见 [references](docs/references.md)。不分发 Codex 客户端代码或角色素材。
