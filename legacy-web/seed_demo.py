#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成一份「演示用」的历史数据到 data/demo.db。

用途：在真实数据积累之前，先预览热力图长什么样。
注意：demo.db 与真实数据库 usage.db 完全分开，互不污染。

运行：  python seed_demo.py [--days 120] [--db data/demo.db]
"""

from __future__ import annotations

import argparse
import json
import os
import random
import sqlite3
import sys
from datetime import date, datetime, time as dtime, timedelta

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tracker import SCHEMA, display_name, DEFAULT_CONFIG  # noqa: E402

# (exe, 显示名, 权重, 常见窗口标题)
APPS = [
    ("chrome", "Chrome", 26, ["知乎 - 浏览器", "哔哩哔哩", "GitHub", "文档 - Google 搜索", "ChatGPT"]),
    ("code", "VS Code", 20, ["appusage\\app.js", "config.json - appusage", "终端"]),
    ("wechat", "微信", 16, ["群聊", "文件传输助手", "张三"]),
    ("bilibili", "哔哩哔哩", 11, ["【4K】纪录片", "弹幕", "稍后再看"]),
    ("neteasemusic", "网易云音乐", 8, ["私人FM", "歌单 · 深夜代码", "每日推荐"]),
    ("explorer", "文件资源管理器", 6, ["下载", "项目", "此电脑"]),
    ("windows terminal", "Windows 终端", 6, ["PS D:\\minimax\\coder>", "cmd"]),
    ("steam", "Steam", 5, ["库", "新闻"]),
    ("photoshop", "Photoshop", 4, ["untitled.psd"]),
    ("zhihu", "知乎", 4, ["首页", "热门话题"]),
    ("dingtalk", "钉钉", 4, ["消息", "待办"]),
    ("notion", "Notion", 3, ["周报", "项目管理"]),
    ("figma", "Figma", 3, ["设计稿 - 仪表盘"]),
    ("discord", "Discord", 3, ["#general", "语音频道"]),
    ("qqmusic", "QQ音乐", 2, ["我喜欢", "歌单"]),
    ("obs64", "OBS Studio", 1, ["场景 1"]),
    ("claude", "Claude", 2, ["对话"]),
]


def main(argv=None):
    ap = argparse.ArgumentParser()
    here = os.path.dirname(os.path.abspath(__file__))
    ap.add_argument("--db", default=os.path.join(here, "data", "demo.db"))
    ap.add_argument("--days", type=int, default=126)
    ap.add_argument("--seed", type=int, default=20261021)
    ap.add_argument("--empty-days", type=int, default=8, help="随机留空的天数")
    args = ap.parse_args(argv)

    rnd = random.Random(args.seed)
    cfg = json.loads(json.dumps(DEFAULT_CONFIG))
    cfg["app_names"].update(json.load(open(os.path.join(here, "config.json"), encoding="utf-8"))["app_names"])

    os.makedirs(os.path.dirname(os.path.abspath(args.db)), exist_ok=True)
    if os.path.exists(args.db):
        os.remove(args.db)
    conn = sqlite3.connect(args.db)
    conn.executescript(SCHEMA)

    weights = [a[2] for a in APPS]
    today = date.today()
    empty = set(rnd.sample(range(args.days), min(args.empty_days, args.days)))
    rows = []
    app_rows = {}
    days_used = set()

    for back in range(args.days - 1, -1, -1):
        d = today - timedelta(days=back)
        if back in empty:
            continue
        weekend = d.weekday() >= 5
        base = rnd.uniform(3.2, 7.6) if not weekend else rnd.uniform(2.6, 6.4)
        if rnd.random() < 0.12:
            base *= rnd.uniform(1.3, 1.7)  # 偶尔爆肝
        total_min = base * 60

        # 把一天切成 5~11 段
        n_seg = rnd.randint(5, 11)
        segs = []
        weights_left = weights[:]
        for _ in range(n_seg):
            segs.append(rnd.uniform(0.7, 2.6))
        norm = sum(segs)
        clock = rnd.choice([8.5, 9.0, 9.5, 10.0, 13.5, 14.0, 19.5, 20.0, 21.0])
        for i, seg in enumerate(segs):
            dur = total_min * seg / norm
            pick = rnd.choices(range(len(APPS)), weights=weights_left)[0]
            weights_left[pick] = max(0.4, weights_left[pick] * 0.82)
            exe, name, _, titles = APPS[pick]
            start_dt = datetime.combine(d, dtime(0, 0)) + timedelta(hours=clock)
            end_dt = start_dt + timedelta(minutes=dur)
            if end_dt.hour >= 24:
                continue
            title = rnd.choice(titles)
            start_ts = start_dt.timestamp()
            end_ts = end_dt.timestamp()
            rows.append((exe, f"C:\\Program Files\\{exe}\\{exe}.exe", title, start_ts, end_ts, end_ts - start_ts))
            days_used.add(d.isoformat())
            a = app_rows.setdefault(exe, {"name": name, "total": 0.0, "first": start_ts, "last": end_ts, "n": 0})
            a["total"] += end_ts - start_ts
            a["first"] = min(a["first"], start_ts)
            a["last"] = max(a["last"], end_ts)
            a["n"] += 1
            clock += dur / 60 + rnd.uniform(0.2, 1.6)
            if clock > 23.0:
                break

    conn.executemany(
        "INSERT INTO sessions (app_key, exe_path, title, start_ts, end_ts, duration) VALUES (?,?,?,?,?,?)",
        rows,
    )
    for key, a in app_rows.items():
        conn.execute(
            "INSERT INTO apps (app_key, display_name, exe_path, total_seconds, first_seen, last_seen)"
            " VALUES (?,?,?,?,?,?)",
            (key, a["name"], f"C:\\Program Files\\{key}\\{key}.exe", a["total"], a["first"], a["last"]),
        )
    conn.execute("INSERT INTO meta (k,v) VALUES ('seed','demo')")
    conn.commit()
    n_days = len(days_used)
    conn.close()
    print(f"[demo] 已生成 {len(rows)} 段 / {n_days} 天记录 → {os.path.abspath(args.db)}")
    print("[demo] 用 start-dashboard.ps1 -Demo 可以预览这个库")
    return 0


if __name__ == "__main__":
    sys.exit(main())
