#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AppUsage Tracker —— 本机前台应用使用时长采集器

纯本地实现：
  * 只用 Windows Win32 API（ctypes）读取当前前台窗口
  * 数据只写进本地 SQLite，不做任何网络请求
  * 空闲（无键鼠输入）超过阈值的时间不计入任何应用

运行：  python tracker.py            # 前台运行，Ctrl+C 退出
        python tracker.py --db x.db  # 指定数据库
"""

from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import signal
import sqlite3
import sys
import time
from datetime import datetime, timedelta

# --------------------------------------------------------------------------
# Win32 API
# --------------------------------------------------------------------------

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

GetForegroundWindow = user32.GetForegroundWindow
GetForegroundWindow.restype = wt.HWND
GetForegroundWindow.argtypes = []

IsWindowVisible = user32.IsWindowVisible
IsWindowVisible.restype = wt.BOOL
IsWindowVisible.argtypes = [wt.HWND]

GetWindowTextLengthW = user32.GetWindowTextLengthW
GetWindowTextLengthW.restype = ctypes.c_int
GetWindowTextLengthW.argtypes = [wt.HWND]

GetWindowTextW = user32.GetWindowTextW
GetWindowTextW.restype = ctypes.c_int
GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]

GetWindowThreadProcessId = user32.GetWindowThreadProcessId
GetWindowThreadProcessId.restype = wt.DWORD
GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]

OpenProcess = kernel32.OpenProcess
OpenProcess.restype = wt.HANDLE
OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]

QueryFullProcessImageNameW = kernel32.QueryFullProcessImageNameW
QueryFullProcessImageNameW.restype = wt.BOOL
QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]

CloseHandle = kernel32.CloseHandle
CloseHandle.restype = wt.BOOL
CloseHandle.argtypes = [wt.HANDLE]

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


class LASTINPUTINFO(ctypes.Structure):
    _fields_ = [("cbSize", wt.UINT), ("dwTime", wt.DWORD)]


GetLastInputInfo = user32.GetLastInputInfo
GetLastInputInfo.restype = wt.BOOL
GetLastInputInfo.argtypes = [ctypes.POINTER(LASTINPUTINFO)]

try:  # GetTickCount64 不会像 GetTickCount 那样 49 天回绕
    GetTickCount64 = kernel32.GetTickCount64
    GetTickCount64.restype = ctypes.c_ulonglong
    GetTickCount64.argtypes = []
except AttributeError:  # pragma: no cover
    GetTickCount64 = None


def idle_seconds() -> float:
    """距离用户最后一次键鼠输入过去了多少秒。"""
    info = LASTINPUTINFO()
    info.cbSize = ctypes.sizeof(LASTINPUTINFO)
    if not GetLastInputInfo(ctypes.byref(info)):
        return 0.0
    if GetTickCount64 is not None:
        now = GetTickCount64()
    else:  # pragma: no cover
        now = user32.GetTickCount()
    return float(now - info.dwTime) / 1000.0


def foreground_window():
    """返回 (hwnd, pid, title)，没有可用前台窗口时返回 (None, None, "")。"""
    hwnd = GetForegroundWindow()
    if not hwnd or not IsWindowVisible(hwnd):
        return None, None, ""
    pid = wt.DWORD()
    GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    length = GetWindowTextLengthW(hwnd)
    title = ""
    if length > 0:
        buf = ctypes.create_unicode_buffer(length + 1)
        GetWindowTextW(hwnd, buf, length + 1)
        title = buf.value
    return hwnd, pid.value, title


_exe_cache: dict[int, str] = {}


def process_exe(pid: int) -> str:
    """取进程可执行文件完整路径，失败返回空串。"""
    if pid in _exe_cache:
        return _exe_cache[pid]
    path = ""
    handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if handle:
        try:
            size = wt.DWORD(1024)
            buf = ctypes.create_unicode_buffer(size.value)
            if QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)):
                path = buf.value
        finally:
            CloseHandle(handle)
    if path:
        _exe_cache[pid] = path
        if len(_exe_cache) > 512:  # 简单防爆
            for k in list(_exe_cache)[:256]:
                _exe_cache.pop(k, None)
    return path


# --------------------------------------------------------------------------
# 配置 / 应用名归一化
# --------------------------------------------------------------------------

DEFAULT_CONFIG = {
    "poll_interval": 1.0,        # 采样间隔（秒）
    "idle_seconds": 90,          # 超过这么久没有键鼠输入就认为用户不在，不计时长
    "min_span_seconds": 2.0,     # 小于这个长度的片段直接丢弃（切窗口噪声）
    "flush_interval": 20.0,      # 当前片段最长多久落一次库（防崩溃丢数据）
    "db_path": "data/usage.db",
    "excluded_exes": [],         # 不统计的可执行文件名（不含扩展名，小写）
    "app_names": {},             # exe名 -> 友好显示名
}

FRIENDLY_DEFAULTS = {
    "chrome": "Chrome",
    "msedge": "Microsoft Edge",
    "firefox": "Firefox",
    "code": "VS Code",
    "explorer": "文件资源管理器",
    "wechat": "微信",
    "weixin": "微信",
    "qq": "QQ",
    "qqmusic": "QQ音乐",
    "neteasemusic": "网易云音乐",
    "steam": "Steam",
    "discord": "Discord",
    "terminal": "终端",
    "windowsterminal": "Windows 终端",
    "powershell": "PowerShell",
    "cmd": "命令提示符",
    "obs64": "OBS Studio",
    "douyin": "抖音",
    "tiktok": "TikTok",
    "notion": "Notion",
    "slack": "Slack",
    "discord.exe": "Discord",
    "photoshop": "Photoshop",
    "premiere": "Premiere Pro",
    "blender": "Blender",
    "unity": "Unity",
    "unrealeditor": "Unreal Engine",
    "zhipu": "智谱清言",
    "cherrystudio": "Cherry Studio",
    "claude": "Claude",
    "chatgpt": "ChatGPT",
}


def load_config(path: str) -> dict:
    cfg = json.loads(json.dumps(DEFAULT_CONFIG))
    if path and os.path.exists(path):
        try:
            with open(path, "r", encoding="utf-8") as fh:
                user = json.load(fh)
            if isinstance(user, dict):
                cfg.update(user)
        except Exception as exc:  # 配置坏了不应该让追踪器挂掉
            print(f"[warn] 读取配置失败，使用默认配置: {exc}", file=sys.stderr)
    return cfg


def app_key_from(exe_path: str, title: str) -> str:
    """把 (exe 路径, 窗口标题) 归一化成稳定的 app_key。"""
    base = os.path.basename(exe_path or "").lower()
    if base.endswith(".exe"):
        base = base[:-4]
    if not base:
        # 拿不到 exe 时退化用窗口标题
        head = (title or "").split(" - ")[0].strip().lower()
        return head[:40] or "unknown"
    if base in ("applicationframehost", "runtimebroker"):
        # UWP / 商店应用的宿主，真实应用名藏在标题里
        head = (title or "").strip()
        if head and len(head) <= 40 and " - " not in head:
            return head.lower()[:40]
        return "uwp"
    return base


def display_name(app_key: str, cfg: dict) -> str:
    names = dict(FRIENDLY_DEFAULTS)
    names.update(cfg.get("app_names") or {})
    if app_key in names:
        return names[app_key]
    if app_key == "uwp":
        return "UWP 应用"
    return app_key


# --------------------------------------------------------------------------
# 数据库
# --------------------------------------------------------------------------

SCHEMA = """
CREATE TABLE IF NOT EXISTS sessions (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    app_key   TEXT NOT NULL,
    exe_path  TEXT,
    title     TEXT,
    start_ts  REAL NOT NULL,
    end_ts    REAL NOT NULL,
    duration  REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_sessions_start ON sessions(start_ts);
CREATE INDEX IF NOT EXISTS idx_sessions_app   ON sessions(app_key);

CREATE TABLE IF NOT EXISTS apps (
    app_key       TEXT PRIMARY KEY,
    display_name  TEXT,
    exe_path      TEXT,
    total_seconds REAL NOT NULL DEFAULT 0,
    first_seen    REAL,
    last_seen     REAL
);

CREATE TABLE IF NOT EXISTS meta (
    k TEXT PRIMARY KEY,
    v TEXT
);
"""


def connect(db_path: str) -> sqlite3.Connection:
    os.makedirs(os.path.dirname(os.path.abspath(db_path)), exist_ok=True)
    conn = sqlite3.connect(db_path, timeout=30.0)
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    conn.executescript(SCHEMA)
    conn.commit()
    return conn


def local_midnight(ts: float) -> float:
    dt = datetime.fromtimestamp(ts)
    return dt.replace(hour=0, minute=0, second=0, microsecond=0).timestamp()


def split_by_day(start: float, end: float):
    """把跨零点的片段拆成若干「同一自然日」的片段。"""
    parts = []
    cur = start
    while cur < end:
        day_end = local_midnight(cur) + 86400.0
        nxt = min(end, day_end)
        if nxt - cur > 0.01:
            parts.append((cur, nxt))
        cur = nxt
    return parts


def write_sessions(conn, app_key, exe_path, title, start, end, cfg):
    """把一段前台时间写进库（按自然日拆分）。"""
    if end - start < 0.01:
        return 0.0
    rows = []
    for s, e in split_by_day(start, end):
        rows.append((app_key, exe_path, (title or "")[:240], s, e, e - s))
    if not rows:
        return 0.0
    total = sum(r[5] for r in rows)
    conn.executemany(
        "INSERT INTO sessions (app_key, exe_path, title, start_ts, end_ts, duration)"
        " VALUES (?,?,?,?,?,?)",
        rows,
    )
    name = display_name(app_key, cfg)
    conn.execute(
        "INSERT INTO apps (app_key, display_name, exe_path, total_seconds, first_seen, last_seen)"
        " VALUES (?,?,?,?,?,?)"
        " ON CONFLICT(app_key) DO UPDATE SET"
        "   total_seconds = total_seconds + excluded.total_seconds,"
        "   last_seen     = excluded.last_seen,"
        "   display_name  = CASE WHEN excluded.display_name != '' THEN excluded.display_name ELSE apps.display_name END,"
        "   exe_path      = CASE WHEN excluded.exe_path != '' THEN excluded.exe_path ELSE apps.exe_path END",
        (app_key, name, exe_path or "", total, start, end),
    )
    conn.commit()
    return total


# --------------------------------------------------------------------------
# 主循环
# --------------------------------------------------------------------------

_running = True


def _stop(signum, frame):
    global _running
    _running = False


def main(argv=None):
    ap = argparse.ArgumentParser(description="本机应用使用时长采集器")
    here = os.path.dirname(os.path.abspath(__file__))
    ap.add_argument("--config", default=os.path.join(here, "config.json"))
    ap.add_argument("--db", default=None, help="覆盖配置里的数据库路径")
    ap.add_argument("--poll", type=float, default=None, help="覆盖采样间隔（秒）")
    ap.add_argument("--idle", type=float, default=None, help="覆盖空闲阈值（秒）")
    ap.add_argument("--once", action="store_true", help="只采一次就退出（调试用）")
    ap.add_argument("--print-db", action="store_true", help="打印数据库绝对路径后退出")
    args = ap.parse_args(argv)

    cfg = load_config(args.config)
    db_path = args.db or os.path.join(here, cfg["db_path"])
    if args.poll:
        cfg["poll_interval"] = args.poll
    if args.idle is not None:
        cfg["idle_seconds"] = args.idle
    if args.print_db:
        print(os.path.abspath(db_path))
        return 0

    os.makedirs(os.path.dirname(os.path.abspath(db_path)), exist_ok=True)
    conn = connect(db_path)

    signal.signal(signal.SIGINT, _stop)
    try:
        signal.signal(signal.SIGTERM, _stop)
    except Exception:
        pass

    pid_file = os.path.join(here, "data", "tracker.pid")
    os.makedirs(os.path.dirname(pid_file), exist_ok=True)
    with open(pid_file, "w", encoding="utf-8") as fh:
        fh.write(str(os.getpid()))

    excluded = {x.lower() for x in (cfg.get("excluded_exes") or [])}
    poll = max(0.2, float(cfg["poll_interval"]))
    idle_limit = float(cfg["idle_seconds"])
    min_span = float(cfg["min_span_seconds"])
    flush_interval = float(cfg["flush_interval"])

    cur_key = None
    cur_exe = ""
    cur_title = ""
    cur_start = time.time()
    last_flush = cur_start
    last_beat = 0.0
    total_tracked = 0.0

    print(f"[tracker] 数据库 : {os.path.abspath(db_path)}")
    print(f"[tracker] 采样间隔 {poll}s / 空闲阈值 {idle_limit}s")
    print("[tracker] 运行中，Ctrl+C 退出", flush=True)

    try:
        while _running:
            now = time.time()
            hwnd, pid, title = foreground_window()
            key = exe = ""
            if hwnd and pid and pid != os.getpid():
                exe = process_exe(pid)
                key = app_key_from(exe, title)
                if key in excluded:
                    key = ""

            idle = idle_seconds()
            if idle >= idle_limit:
                key = ""  # 用户不在，不计

            if key != cur_key:
                # 结束上一段
                if cur_key and now - cur_start >= min_span:
                    total_tracked += write_sessions(
                        conn, cur_key, cur_exe, cur_title, cur_start, now, cfg
                    )
                cur_key, cur_exe = key, exe
                cur_title = title or ""
                cur_start = now
                last_flush = now
            elif key:
                if title:
                    cur_title = title
                if now - last_flush >= flush_interval:
                    total_tracked += write_sessions(
                        conn, cur_key, cur_exe, cur_title, cur_start, now, cfg
                    )
                    cur_start = now
                    last_flush = now

            if now - last_beat > 10:
                conn.execute(
                    "INSERT INTO meta (k,v) VALUES ('heartbeat',?)"
                    " ON CONFLICT(k) DO UPDATE SET v=excluded.v",
                    (str(now),),
                )
                conn.execute(
                    "INSERT INTO meta (k,v) VALUES ('tracked_seconds',?)"
                    " ON CONFLICT(k) DO UPDATE SET v=excluded.v",
                    (str(total_tracked),),
                )
                conn.commit()
                last_beat = now

            if args.once:
                break

            # 分片 sleep，保证 Ctrl+C / 信号能及时响应
            slept = 0.0
            while slept < poll and _running:
                step = min(0.25, poll - slept)
                time.sleep(step)
                slept += step
    finally:
        now = time.time()
        if cur_key and now - cur_start >= min_span:
            write_sessions(conn, cur_key, cur_exe, cur_title, cur_start, now, cfg)
        conn.execute(
            "INSERT INTO meta (k,v) VALUES ('last_stop',?)"
            " ON CONFLICT(k) DO UPDATE SET v=excluded.v",
            (str(now),),
        )
        conn.commit()
        conn.close()
        try:
            os.remove(pid_file)
        except OSError:
            pass
        print(f"\n[tracker] 已停止，本次共记录 {total_tracked/60:.1f} 分钟")
    return 0


if __name__ == "__main__":
    sys.exit(main())
