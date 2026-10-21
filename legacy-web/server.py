#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AppUsage Dashboard —— 本地仪表盘服务

只监听 127.0.0.1，从本地 SQLite 读数据，渲染 GitHub 风格热力图。
无网络请求、无第三方依赖（Python 标准库即可）。

运行：  python server.py --open
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
import threading
import webbrowser
from datetime import date, datetime, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

HERE = os.path.dirname(os.path.abspath(__file__))
WEB_DIR = os.path.join(HERE, "web")


# --------------------------------------------------------------------------
# 数据访问
# --------------------------------------------------------------------------

def db_connect(db_path: str) -> sqlite3.Connection:
    conn = sqlite3.connect(db_path, timeout=15.0)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    return conn


def iso_today() -> str:
    return date.today().isoformat()


def _q(conn, sql, params=()):
    return conn.execute(sql, params).fetchall()


def day_total(conn, day: str) -> float:
    row = conn.execute(
        "SELECT COALESCE(SUM(duration),0) FROM sessions"
        " WHERE date(start_ts,'unixepoch','localtime') = ?",
        (day,),
    ).fetchone()
    return float(row[0] or 0.0)


def day_apps(conn, day: str) -> list:
    rows = _q(
        conn,
        "SELECT s.app_key,"
        "       COALESCE(a.display_name, s.app_key) AS name,"
        "       SUM(s.duration) AS seconds,"
        "       COUNT(*) AS n"
        " FROM sessions s LEFT JOIN apps a ON a.app_key = s.app_key"
        " WHERE date(s.start_ts,'unixepoch','localtime') = ?"
        " GROUP BY s.app_key ORDER BY seconds DESC",
        (day,),
    )
    return [
        {"key": r["app_key"], "name": r["name"], "seconds": round(r["seconds"] or 0, 1), "sessions": r["n"]}
        for r in rows
    ]


def range_total(conn, start: str, end: str) -> float:
    row = conn.execute(
        "SELECT COALESCE(SUM(duration),0) FROM sessions"
        " WHERE date(start_ts,'unixepoch','localtime') BETWEEN ? AND ?",
        (start, end),
    ).fetchone()
    return float(row[0] or 0.0)


def range_apps(conn, start: str, end: str) -> list:
    rows = _q(
        conn,
        "SELECT s.app_key,"
        "       COALESCE(a.display_name, s.app_key) AS name,"
        "       SUM(s.duration) AS seconds,"
        "       COUNT(*) AS n"
        " FROM sessions s LEFT JOIN apps a ON a.app_key = s.app_key"
        " WHERE date(s.start_ts,'unixepoch','localtime') BETWEEN ? AND ?"
        " GROUP BY s.app_key ORDER BY seconds DESC",
        (start, end),
    )
    return [
        {"key": r["app_key"], "name": r["name"], "seconds": round(r["seconds"] or 0, 1), "sessions": r["n"]}
        for r in rows
    ]


def app_table(conn) -> list:
    """每个应用：今日 / 7天 / 30天 / 累计 / 最近使用。"""
    today = iso_today()
    d7 = (date.today() - timedelta(days=6)).isoformat()
    d30 = (date.today() - timedelta(days=29)).isoformat()
    rows = _q(
        conn,
        """
        SELECT COALESCE(a.display_name, s.app_key) AS name,
               s.app_key AS key,
               MAX(a.exe_path) AS exe_path,
               SUM(CASE WHEN date(s.start_ts,'unixepoch','localtime') = ?    THEN s.duration ELSE 0 END) AS today_s,
               SUM(CASE WHEN date(s.start_ts,'unixepoch','localtime') >= ?   THEN s.duration ELSE 0 END) AS d7_s,
               SUM(CASE WHEN date(s.start_ts,'unixepoch','localtime') >= ?  THEN s.duration ELSE 0 END) AS d30_s,
               SUM(s.duration) AS total_s,
               COUNT(*) AS sessions,
               MAX(s.end_ts) AS last_seen
        FROM sessions s
        LEFT JOIN apps a ON a.app_key = s.app_key
        GROUP BY s.app_key
        ORDER BY total_s DESC
        """,
        (today, d7, d30),
    )
    out = []
    for r in rows:
        out.append(
            {
                "key": r["key"],
                "name": r["name"],
                "exe_path": r["exe_path"] or "",
                "today": round(r["today_s"] or 0, 1),
                "d7": round(r["d7_s"] or 0, 1),
                "d30": round(r["d30_s"] or 0, 1),
                "total": round(r["total_s"] or 0, 1),
                "sessions": r["sessions"],
                "last_seen": r["last_seen"],
            }
        )
    return out


def heatmap(conn, start: str, end: str) -> list:
    rows = _q(
        conn,
        """
        SELECT date(s.start_ts,'unixepoch','localtime') AS d,
               SUM(s.duration) AS seconds,
               COUNT(DISTINCT s.app_key) AS apps
        FROM sessions s
        WHERE date(s.start_ts,'unixepoch','localtime') BETWEEN ? AND ?
        GROUP BY d ORDER BY d
        """,
        (start, end),
    )
    data = {r["d"]: {"seconds": round(r["seconds"] or 0, 1), "apps": r["apps"]} for r in rows}

    # 补齐空白日，保证热力图格子连续
    out = []
    cur = date.fromisoformat(start)
    end_d = date.fromisoformat(end)
    while cur <= end_d:
        k = cur.isoformat()
        item = data.get(k, {"seconds": 0.0, "apps": 0})
        out.append({"date": k, "seconds": item["seconds"], "apps": item["apps"]})
        cur += timedelta(days=1)
    return out


def tracker_status(conn) -> dict:
    row = conn.execute("SELECT v FROM meta WHERE k='heartbeat'").fetchone()
    beat = float(row[0]) if row and row[0] else 0.0
    online = bool(beat) and (time_now() - beat) < 90
    last_stop = None
    row = conn.execute("SELECT v FROM meta WHERE k='last_stop'").fetchone()
    if row and row[0]:
        last_stop = float(row[0])
    pid_file = os.path.join(HERE, "data", "tracker.pid")
    pid = None
    if os.path.exists(pid_file):
        try:
            with open(pid_file, "r", encoding="utf-8") as fh:
                pid = int(fh.read().strip())
        except Exception:
            pid = None
    return {
        "online": online,
        "heartbeat": beat,
        "pid": pid,
        "last_stop": last_stop,
    }


def time_now() -> float:
    import time as _t
    return _t.time()


def build_summary(conn) -> dict:
    today = date.today()
    t = today.isoformat()
    y = (today - timedelta(days=1)).isoformat()
    w_start = (today - timedelta(days=6)).isoformat()
    m_start = (today - timedelta(days=29)).isoformat()

    total = range_total(conn, "1970-01-01", t)
    first = conn.execute("SELECT MIN(start_ts) FROM sessions").fetchone()[0]
    active_days = conn.execute(
        "SELECT COUNT(DISTINCT date(start_ts,'unixepoch','localtime')) FROM sessions"
    ).fetchone()[0] or 0
    app_count = conn.execute("SELECT COUNT(DISTINCT app_key) FROM sessions").fetchone()[0] or 0
    total_seconds_all = conn.execute("SELECT COALESCE(SUM(duration),0) FROM sessions").fetchone()[0] or 0

    return {
        "generated_at": datetime.now().isoformat(timespec="seconds"),
        "today": {"date": t, "seconds": round(day_total(conn, t), 1), "apps": day_apps(conn, t)},
        "yesterday": {"date": y, "seconds": round(day_total(conn, y), 1)},
        "week": {
            "from": w_start,
            "to": t,
            "seconds": round(range_total(conn, w_start, t), 1),
            "avg": round(range_total(conn, w_start, t) / 7.0, 1),
        },
        "month": {
            "from": m_start,
            "to": t,
            "seconds": round(range_total(conn, m_start, t), 1),
            "avg": round(range_total(conn, m_start, t) / 30.0, 1),
        },
        "total": {
            "seconds": round(total_seconds_all, 1),
            "since": first,
            "days": active_days,
            "apps": app_count,
        },
        "top_today": day_apps(conn, t)[:8],
        "top_total": range_apps(conn, "1970-01-01", t)[:8],
        "status": tracker_status(conn),
    }


# --------------------------------------------------------------------------
# HTTP
# --------------------------------------------------------------------------

class Handler(BaseHTTPRequestHandler):
    server_version = "AppUsage/1.0"
    db_path = ""
    quiet = False

    def log_message(self, fmt, *args):  # 安静一点
        if not self.quiet:
            sys.stderr.write("[server] %s\n" % (fmt % args))

    # -- helpers -------------------------------------------------------
    def _send_json(self, obj, code=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_file(self, path):
        ctype = {
            ".html": "text/html; charset=utf-8",
            ".css": "text/css; charset=utf-8",
            ".js": "application/javascript; charset=utf-8",
            ".json": "application/json; charset=utf-8",
            ".svg": "image/svg+xml",
            ".ico": "image/x-icon",
        }.get(os.path.splitext(path)[1].lower(), "application/octet-stream")
        with open(path, "rb") as fh:
            body = fh.read()
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(body)

    # -- routes --------------------------------------------------------
    def do_GET(self):
        parsed = urlparse(self.path)
        route = parsed.path
        query = parse_qs(parsed.query)

        try:
            if route in ("/", "/index.html"):
                return self._send_file(os.path.join(WEB_DIR, "index.html"))
            if route in ("/app.js", "/styles.css"):
                return self._send_file(os.path.join(WEB_DIR, route.lstrip("/")))

            if not route.startswith("/api/"):
                return self._send_json({"error": "not found"}, 404)

            conn = db_connect(self.db_path)
            try:
                if route == "/api/summary":
                    return self._send_json(build_summary(conn))

                if route == "/api/apps":
                    return self._send_json({"apps": app_table(conn)})

                if route == "/api/heatmap":
                    end = (query.get("end") or [iso_today()])[0]
                    weeks = query.get("weeks")
                    if weeks:
                        days = int(weeks[0]) * 7
                    else:
                        days = int((query.get("days") or [371])[0])
                    start = (
                        query.get("start")
                        or [(date.fromisoformat(end) - timedelta(days=days - 1)).isoformat()]
                    )[0]
                    return self._send_json(
                        {"start": start, "end": end, "days": heatmap(conn, start, end)}
                    )

                if route == "/api/day":
                    day = (query.get("date") or [iso_today()])[0]
                    rows = _q(
                        conn,
                        "SELECT strftime('%H', s.start_ts,'unixepoch','localtime') AS h,"
                        "       COALESCE(a.display_name, s.app_key) AS name,"
                        "       SUM(s.duration) AS seconds"
                        " FROM sessions s LEFT JOIN apps a ON a.app_key = s.app_key"
                        " WHERE date(s.start_ts,'unixepoch','localtime') = ?"
                        " GROUP BY h, s.app_key ORDER BY h",
                        (day,),
                    )
                    hourly = {}
                    for r in rows:
                        hourly.setdefault(r["h"], []).append(
                            {"name": r["name"], "seconds": round(r["seconds"] or 0, 1)}
                        )
                    return self._send_json(
                        {
                            "date": day,
                            "seconds": round(day_total(conn, day), 1),
                            "apps": day_apps(conn, day),
                            "hours": hourly,
                        }
                    )

                if route == "/api/status":
                    return self._send_json(tracker_status(conn))

                return self._send_json({"error": "not found"}, 404)
            finally:
                conn.close()
        except FileNotFoundError:
            return self._send_json({"error": "database not found"}, 500)
        except Exception as exc:
            return self._send_json({"error": str(exc)}, 500)

    def do_HEAD(self):
        self.do_GET()


def main(argv=None):
    ap = argparse.ArgumentParser(description="AppUsage 本地仪表盘")
    ap.add_argument("--db", default=None)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8787)
    ap.add_argument("--open", action="store_true", help="启动后自动打开浏览器")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    db_path = args.db or os.path.join(HERE, "data", "usage.db")
    db_path = os.path.abspath(db_path)
    if not os.path.exists(db_path):
        print(f"[server] 提示：数据库还不存在 ({db_path})，先运行 tracker.py 或 seed_demo.py")

    Handler.db_path = db_path
    Handler.quiet = args.quiet

    httpd = ThreadingHTTPServer((args.host, args.port), Handler)
    url = f"http://{args.host}:{args.port}/"
    print(f"[server] 仪表盘已启动 → {url}")
    print("[server] 按 Ctrl+C 退出")
    if args.open:
        threading.Timer(0.8, lambda: webbrowser.open(url)).start()
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[server] 已停止")
    finally:
        httpd.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
