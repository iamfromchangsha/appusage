#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把「键盘鼠标点击次数统计工具V1.0」的 data.db 迁移进 AppUsage 的 data/input/ 目录。

老工具的数据结构：
    keyboard_stats(date, key_name, combo_flag, count)   -- 每个键每天的次数
    mouse_stats(date, distance_cm, left_cnt, middle_cnt, right_cnt, side_cnt)

AppUsage 的格式（每个键鼠统计文件）：
    data/input/YYYY-MM-DD.json
    {"Keys":{"A":120,...},"DistanceCm":1234.5,"Left":10,"Middle":0,"Right":2,"Side":0}

用法：
    python scripts/migrate_mousekey.py [旧工具目录] [新目录] [--replace] [--days 2026-10-25]

    --replace   不和 AppUsage 已有的当天数据合并，直接覆盖（重做某天时用）
    --days      只处理指定日期，多个用逗号分隔（不写就全部）

**迁移完就可以把旧程序停用了**（取消开机自启 + 退出进程）。
"""
import argparse
import json
import os
import sqlite3
import sys
from collections import defaultdict

DEFAULT_OLD_DIR = r"E:\桌面\工具\键盘鼠标点击次数统计工具V1.0"
DEFAULT_NEW_DIR = r"D:\minimax\coder\appusage\dist\data\input"


def migrate(old_dir=DEFAULT_OLD_DIR, new_dir=DEFAULT_NEW_DIR, merge=True, only_days=None):
    db = os.path.join(old_dir, "data.db")
    if not os.path.exists(db):
        print(f"找不到 {db}")
        return 1

    conn = sqlite3.connect("file:" + db.replace("\\", "/") + "?mode=ro", uri=True)
    os.makedirs(new_dir, exist_ok=True)

    # 1) 键盘：按天聚合所有键
    keys_by_day = defaultdict(lambda: defaultdict(int))
    for date, key_name, count in conn.execute(
            "select date, key_name, sum(count) from keyboard_stats group by date, key_name"):
        if key_name:
            keys_by_day[date][key_name] += int(count or 0)

    # 2) 鼠标：每天一行
    mouse_by_day = {}
    for row in conn.execute(
            "select date, distance_cm, left_cnt, middle_cnt, right_cnt, side_cnt from mouse_stats"):
        mouse_by_day[row[0]] = {
            "DistanceCm": float(row[1] or 0),
            "Left": int(row[2] or 0),
            "Middle": int(row[3] or 0),
            "Right": int(row[4] or 0),
            "Side": int(row[5] or 0),
        }
    conn.close()

    days = sorted(set(keys_by_day) | set(mouse_by_day))
    if only_days:
        want = set(only_days)
        days = [d for d in days if d in want]
    total_keys = 0
    written = 0
    for date in days:
        path = os.path.join(new_dir, date + ".json")
        stat = {
            "Keys": dict(sorted(keys_by_day.get(date, {}).items(), key=lambda kv: -kv[1])),
            "DistanceCm": 0.0,
            "Left": 0, "Middle": 0, "Right": 0, "Side": 0,
        }
        stat.update(mouse_by_day.get(date, {}))

        # 和 AppUsage 自己已经记的当天数据合并（迁移当天两者都可能有）
        if merge and os.path.exists(path):
            try:
                with open(path, encoding="utf-8") as f:
                    cur = json.load(f)
                merged = dict(cur.get("Keys") or {})
                for k, v in (stat["Keys"] or {}).items():
                    merged[k] = merged.get(k, 0) + v
                stat["Keys"] = merged
                for fld in ("DistanceCm", "Left", "Middle", "Right", "Side"):
                    stat[fld] = (cur.get(fld) or 0) + (stat.get(fld) or 0)
            except Exception as e:
                print(f"  {date} 合并失败，保留旧数据: {e}")

        total_keys += sum(stat["Keys"].values())
        with open(path, "w", encoding="utf-8") as f:
            json.dump(stat, f, ensure_ascii=False, separators=(",", ":"))
        written += 1

    print(f"迁移完成：{written} 天，共 {total_keys:,} 次按键 → {new_dir}")
    return 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="迁移键盘鼠标点击次数统计工具V1.0 的历史数据")
    ap.add_argument("old_dir", nargs="?", default=DEFAULT_OLD_DIR, help="旧工具目录")
    ap.add_argument("new_dir", nargs="?", default=DEFAULT_NEW_DIR, help="AppUsage 的 data/input 目录")
    ap.add_argument("--replace", action="store_true", help="覆盖已有文件，不与 AppUsage 的数据合并")
    ap.add_argument("--days", default="", help="只处理这些日期，逗号分隔（如 2026-10-25）")
    a = ap.parse_args()

    only = [d.strip() for d in a.days.split(",") if d.strip()] or None
    sys.exit(migrate(a.old_dir, a.new_dir, merge=not a.replace, only_days=only))
