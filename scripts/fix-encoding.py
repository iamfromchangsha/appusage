#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
给 scripts\\*.ps1 补上 UTF-8 BOM。

Windows PowerShell 5.1 读取无 BOM 的 .ps1 时会按 ANSI(GBK) 解码，
中文提示就会变成乱码。加 BOM 后它才会正确按 UTF-8 读取。

用法：  python scripts\\fix-encoding.py
"""

import glob
import os
import sys

BOM = b"\xef\xbb\xbf"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def main():
    # 整个项目里的 .ps1 都要 BOM（scripts\、native\tools\ 等）
    targets = sorted(glob.glob(os.path.join(ROOT, "**", "*.ps1"), recursive=True))
    changed = 0
    for path in targets:
        with open(path, "rb") as fh:
            raw = fh.read()
        rel = os.path.relpath(path, ROOT)
        if raw.startswith(BOM):
            print(f"  ok   {rel} (已有 BOM)")
            continue
        with open(path, "wb") as fh:
            fh.write(BOM + raw)
        print(f"  fix  {rel} (已补 BOM)")
        changed += 1
    print(f"完成，修改 {changed} 个文件")
    return 0


if __name__ == "__main__":
    sys.exit(main())
