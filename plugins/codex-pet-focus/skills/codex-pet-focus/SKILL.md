---
name: codex-pet-focus
description: 当用户明确要安装、启动或诊断 Codex Pet Focus 本地任务助手时使用。
---

# Codex Pet Focus

运行阶段通过本地任务面板操作，不需要模型或网络。

- 启动：运行此插件的 scripts/start.ps1。
- 诊断：运行 scripts/diagnose.ps1，报告实际兼容状态。
- 快捷方式：用户要求时运行 scripts/create-shortcut.ps1。
- 新建计划、启动/停止任务：打开本地面板，让用户直接操作。不要聊天轮询、创建定时模型任务或持续读取用户任务文件。
- 自动动作以随包兼容性报告为准；投递消息成功不等于完整流程通过。不能生成替代宠物、移动真实鼠标、修改客户端或用浮窗弹跳替代原生动画。
- 更新和卸载保留 %LOCALAPPDATA%\CodexPetFocus 中的任务记录。

脚本路径相对插件根目录。缺少 app/CodexPetFocus.App.exe 时，使用已构建发布包，或在源码仓库运行 tools/build.ps1。
