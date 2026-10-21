@echo off
rem 用于计划任务 / 开机自启：前台运行追踪器
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-tracker.ps1" -Foreground
