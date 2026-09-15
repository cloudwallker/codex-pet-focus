#!/usr/bin/env python3
"""只读定位 Codex 原桌宠；显式 --exercise 才执行可恢复兼容性实验。"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from dataclasses import asdict, dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time


VERIFIED_GEOMETRY = {"26.908.4834.0": {"width": 112, "height": 121, "dpi": 96}}
WS_EX_TOPMOST = 0x00000008
WS_EX_LAYERED = 0x00080000
GWL_EXSTYLE = -20
WM_MOUSEMOVE = 0x0200
WM_MOUSELEAVE = 0x02A3
SWP_NOACTIVATE = 0x0010
SWP_NOZORDER = 0x0004
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


class StateReadError(RuntimeError):
    pass


@dataclass(frozen=True)
class WindowInfo:
    hwnd: int
    process_id: int
    visible: bool
    class_name: str
    ex_style: int
    rect: tuple[int, int, int, int]


def is_codex_executable(path: str) -> bool:
    normalized = path.replace("/", "\\")
    return re.search(
        r"(?:^|\\)WindowsApps\\OpenAI\.Codex_[^\\]+\\app\\ChatGPT\.exe$",
        normalized,
        re.IGNORECASE,
    ) is not None


def extract_version(path: str) -> str | None:
    match = re.search(r"(?:^|\\)OpenAI\.Codex_([0-9]+(?:\.[0-9]+){3})_", path.replace("/", "\\"), re.I)
    return match.group(1) if match else None


def geometry_profile(version: str):
    profile = VERIFIED_GEOMETRY.get(version)
    return dict(profile) if profile else None


def read_overlay_state(path: Path) -> dict:
    try:
        with path.open("r", encoding="utf-8") as stream:
            document = json.load(stream)
        raw_bounds = document.get("electron-avatar-overlay-bounds")
        bounds = None
        if isinstance(raw_bounds, dict) and isinstance(raw_bounds.get("x"), (int, float)) and isinstance(raw_bounds.get("y"), (int, float)):
            bounds = {"x": int(raw_bounds["x"]), "y": int(raw_bounds["y"])}
            mascot = raw_bounds.get("mascot")
            if isinstance(mascot, dict) and all(isinstance(mascot.get(k), (int, float)) for k in ("left", "top", "width", "height")):
                bounds["mascot"] = {k: int(mascot[k]) for k in ("left", "top", "width", "height")}
        return {"open": document.get("electron-avatar-overlay-open") is True, "bounds": bounds}
    except (OSError, UnicodeError, json.JSONDecodeError, TypeError, ValueError) as error:
        raise StateReadError(f"无法读取桌宠状态：{type(error).__name__}") from None


def select_unique_overlay(windows: list[WindowInfo], process_ids: set[int], position: tuple[int, int]):
    x, y = position
    matches = []
    for window in windows:
        left, top, right, bottom = window.rect
        style_ok = window.ex_style & (WS_EX_TOPMOST | WS_EX_LAYERED) == (WS_EX_TOPMOST | WS_EX_LAYERED)
        contains_sprite = left <= x < right and top <= y < bottom
        if (window.process_id in process_ids and window.visible and
                window.class_name == "Chrome_WidgetWin_1" and style_ok and contains_sprite):
            matches.append(window)
    if len(matches) == 1:
        return matches[0], "unique-compatible-window"
    if not matches:
        return None, "no-compatible-window"
    return None, f"ambiguous-compatible-windows:{len(matches)}"


if os.name == "nt":
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    ENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    class RECT(ctypes.Structure):
        _fields_ = [("left", wintypes.LONG), ("top", wintypes.LONG),
                    ("right", wintypes.LONG), ("bottom", wintypes.LONG)]

    class POINT(ctypes.Structure):
        _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]

    class LASTINPUTINFO(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.UINT), ("dwTime", wintypes.DWORD)]

    user32.EnumWindows.argtypes = [ENUMPROC, wintypes.LPARAM]
    user32.EnumChildWindows.argtypes = [wintypes.HWND, ENUMPROC, wintypes.LPARAM]
    user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(RECT)]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowLongPtrW.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.GetWindowLongPtrW.restype = ctypes.c_ssize_t
    user32.GetDpiForWindow.argtypes = [wintypes.HWND]
    user32.GetDpiForWindow.restype = wintypes.UINT
    user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int,
                                   ctypes.c_int, ctypes.c_int, wintypes.UINT]
    user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                                   wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]


def _process_path(pid: int) -> str | None:
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return None
    try:
        size = wintypes.DWORD(32768)
        buffer = ctypes.create_unicode_buffer(size.value)
        return buffer.value if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)) else None
    finally:
        kernel32.CloseHandle(handle)


def enumerate_windows() -> tuple[list[WindowInfo], dict[int, str]]:
    windows: list[WindowInfo] = []
    paths: dict[int, str] = {}

    @ENUMPROC
    def callback(hwnd, _):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        path = paths.get(pid.value)
        if path is None:
            path = _process_path(pid.value) or ""
            paths[pid.value] = path
        if not is_codex_executable(path):
            return True
        rect = RECT()
        class_name = ctypes.create_unicode_buffer(256)
        if user32.GetWindowRect(hwnd, ctypes.byref(rect)):
            user32.GetClassNameW(hwnd, class_name, len(class_name))
            windows.append(WindowInfo(int(hwnd), pid.value, bool(user32.IsWindowVisible(hwnd)),
                                      class_name.value, int(user32.GetWindowLongPtrW(hwnd, GWL_EXSTYLE)),
                                      (rect.left, rect.top, rect.right, rect.bottom)))
        return True

    if not user32.EnumWindows(callback, 0):
        raise OSError(ctypes.get_last_error(), "EnumWindows 失败")
    return windows, {pid: path for pid, path in paths.items() if is_codex_executable(path)}


def _input_snapshot() -> dict:
    point = POINT()
    info = LASTINPUTINFO(ctypes.sizeof(LASTINPUTINFO), 0)
    user32.GetCursorPos(ctypes.byref(point))
    user32.GetLastInputInfo(ctypes.byref(info))
    return {"cursor": [point.x, point.y], "foregroundHwnd": int(user32.GetForegroundWindow()),
            "lastInputTick": int(info.dwTime)}


def _capture(path: Path, rect: tuple[int, int, int, int]) -> str:
    from PIL import ImageGrab
    image = ImageGrab.grab(bbox=rect, all_screens=True)
    image.save(path)
    return hashlib.sha256(image.tobytes()).hexdigest()


def _message_targets(top_level: int) -> list[tuple[str, int]]:
    targets = [("top-level", top_level)]

    @ENUMPROC
    def callback(hwnd, _):
        class_name = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, class_name, len(class_name))
        if class_name.value == "Chrome_RenderWidgetHostHWND" and user32.IsWindowVisible(hwnd):
            targets.append(("content", int(hwnd)))
        return True

    user32.EnumChildWindows(top_level, callback, 0)
    return targets


def exercise(target: WindowInfo, sprite: tuple[int, int, int, int], artifact_dir: Path) -> dict:
    artifact_dir.mkdir(parents=True, exist_ok=True)
    original = target.rect
    width, height = original[2] - original[0], original[3] - original[1]
    events = [{"event": "baseline", **_input_snapshot(), "windowRect": list(original),
               "spriteRect": list(sprite), "imageHash": _capture(artifact_dir / "00-baseline.png", sprite)}]
    try:
        if not user32.SetWindowPos(target.hwnd, 0, original[0] + 20, original[1], width, height,
                                   SWP_NOACTIVATE | SWP_NOZORDER):
            raise OSError(ctypes.get_last_error(), "SetWindowPos(+20) 失败")
        time.sleep(0.35)
        moved = RECT()
        user32.GetWindowRect(target.hwnd, ctypes.byref(moved))
        events.append({"event": "move+20", "windowRect": [moved.left, moved.top, moved.right, moved.bottom]})
    finally:
        user32.SetWindowPos(target.hwnd, 0, original[0], original[1], width, height,
                            SWP_NOACTIVATE | SWP_NOZORDER)
    time.sleep(0.8)
    restored = RECT()
    user32.GetWindowRect(target.hwnd, ctypes.byref(restored))
    events.append({"event": "restored", **_input_snapshot(),
                   "windowRect": [restored.left, restored.top, restored.right, restored.bottom]})

    center_x, center_y = (sprite[0] + sprite[2]) // 2, (sprite[1] + sprite[3]) // 2
    local_x, local_y = center_x - original[0], center_y - original[1]
    lparam = (local_y & 0xFFFF) << 16 | (local_x & 0xFFFF)
    for index, (kind, hwnd) in enumerate(_message_targets(target.hwnd), start=1):
        prefix = f"{index:02d}-{kind}"
        before_hash = _capture(artifact_dir / f"{prefix}-before.png", sprite)
        try:
            posted = bool(user32.PostMessageW(hwnd, WM_MOUSEMOVE, 0, lparam))
            time.sleep(0.35)
            early_hash = _capture(artifact_dir / f"{prefix}-after-350ms.png", sprite)
            time.sleep(2.35)
            late_hash = _capture(artifact_dir / f"{prefix}-after-2700ms.png", sprite)
            events.append({"event": f"{kind}-message", "hwnd": hwnd, "posted": posted,
                           "beforeHash": before_hash, "earlyHash": early_hash, "lateHash": late_hash,
                           "visualChangedEarly": before_hash != early_hash,
                           "visualChangedLate": early_hash != late_hash, **_input_snapshot()})
        finally:
            user32.PostMessageW(hwnd, WM_MOUSELEAVE, 0, 0)
        time.sleep(0.4)
    return {"events": events, "jumpingConfirmed": False,
            "conclusion": "消息投递与画面变化不足以证明 jumping；需人工观察且需内容子窗口独立验证。"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exercise", action="store_true", help="在唯一定位后执行可恢复实验")
    parser.add_argument("--artifact-dir", type=Path, default=Path("artifacts/compatibility"))
    args = parser.parse_args()
    if os.name != "nt":
        print(json.dumps({"status": "unsupported-platform"}, ensure_ascii=False)); return 2
    try:
        state_path = Path(os.environ["USERPROFILE"]) / ".codex" / ".codex-global-state.json"
        state = read_overlay_state(state_path)
        windows, paths = enumerate_windows()
        position = None if state["bounds"] is None else (state["bounds"]["x"], state["bounds"]["y"])
        selected, reason = (None, "state-bounds-missing") if position is None else select_unique_overlay(windows, set(paths), position)
        report = {"status": reason, "state": state,
                  "processes": [{"pid": pid, "version": extract_version(path)} for pid, path in paths.items()],
                  "candidateCount": sum(1 for w in windows if w.visible), "target": None}
        if selected and position:
            version = extract_version(paths[selected.process_id])
            profile = geometry_profile(version or "")
            dpi = int(user32.GetDpiForWindow(selected.hwnd))
            mascot = None if state["bounds"] is None else state["bounds"].get("mascot")
            sprite = None
            if profile and dpi == profile["dpi"] and mascot:
                sprite = (selected.rect[0] + mascot["left"], selected.rect[1] + mascot["top"],
                          selected.rect[0] + mascot["left"] + mascot["width"],
                          selected.rect[1] + mascot["top"] + mascot["height"])
            report["target"] = {**asdict(selected), "sprite_rect": sprite, "version": version, "dpi": dpi}
            if sprite is None:
                report["status"] = "window-found-sprite-bounds-missing-or-unverified"
            elif args.exercise:
                report["exercise"] = exercise(selected, sprite, args.artifact_dir)
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0 if selected and report.get("target", {}).get("sprite_rect") else 3
    except StateReadError as error:
        print(json.dumps({"status": "state-read-error", "reason": str(error)}, ensure_ascii=False)); return 2
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
