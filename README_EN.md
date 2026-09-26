# Codex Pet Focus

### An offline Windows focus timer beside your Codex pet

**Turn today's plan into timed tasks and keep the active task and elapsed time beside the native Codex desktop pet. Manage tasks locally, with no network access or model calls during everyday use.**

English | [简体中文](README.md)

[Install and run](#install-and-run) · [Build from source](#build-from-source) · [Compatibility and limits](docs/compatibility.md)

## Features

- Add today's plan with one task per line.
- Start, pause, resume, and complete tasks; only one task runs at a time.
- Show the active task and elapsed time in a click-through banner above the native Codex pet.
- Track daily focus time, per-task time, and completed-task totals.
- Carry unfinished tasks from yesterday into today without copying historical duration.
- Pause safely on lock, suspend, and exit; restore crash checkpoints in a paused state.
- Store all task data locally in `%LOCALAPPDATA%\CodexPetFocus`.
- Run without network access, model calls, MCP services, or chat polling.

## Native pet compatibility

The banner locates the original pet through read-only Windows UI Automation. It does not draw a replacement pet, patch the Codex client, move the real pointer, or inject chat content.

Automatic movement, native jumping, and return-to-position are disabled in this release. Tests against Codex `26.908.4834.0` could identify the pet image, but window movement and reliable native jumping did not pass the compatibility gate. The application never simulates jumping by moving a window.

See [Compatibility](docs/compatibility.md) for verified behavior and current limits.

## Install and run

1. Download the Windows release ZIP and extract it to a permanent directory.
2. Run `codex-pet-focus/app/CodexPetFocus.App.exe`.
3. Enter one task per line and select **Add to today**.
4. Start a task. When the native pet can be located, its banner appears automatically.
5. Closing the panel keeps the helper in the system tray.

Requirements: Windows x64 and .NET 10 Desktop Runtime.

The release also includes a Codex plugin manifest, a small local skill, and PowerShell scripts for starting, diagnostics, and shortcut creation. Daily task use happens in the local application and consumes no model tokens.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
powershell -NoProfile -File tools/build.ps1
```

The project uses no third-party NuGet packages. The release archive is created under `artifacts/`.

## Privacy

The application reads only its local task data and the minimum Codex pet window state needed for positioning. It does not read authentication files or chats and contains no network client code.

## License

MIT. Design references are listed in [docs/references.md](docs/references.md). Codex client code and character artwork are not distributed.
