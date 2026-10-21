@echo off
rem 编译原生 EXE（需要 .NET SDK 9.0）
chcp 65001 >nul
title 编译 应用使用时长
cd /d "%~dp0"

echo.
echo   正在编译 AppUsage.exe ...
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo   [!] 找不到 dotnet，请先安装 .NET 9 SDK：https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

dotnet publish "%~dp0native\AppUsage.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0dist"
if errorlevel 1 (
    echo.
    echo   [x] 编译失败。
    pause
    exit /b 1
)

echo.
echo   [+] 编译完成：%~dp0dist\AppUsage.exe
pause
