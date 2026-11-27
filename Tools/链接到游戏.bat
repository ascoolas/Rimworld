@echo off
chcp 65001 >nul
setlocal
rem ============================================================
rem  把仓库里的 Mod 文件夹“链接”到游戏的 Mods 文件夹（只需运行一次）。
rem  之后在 GitHub Desktop 里点 Pull，游戏里的导演 Mod 就自动是最新的，
rem  不用再下载 ZIP、复制、覆盖。
rem ============================================================

set "REPO_MOD=%~dp0..\Mod"
for %%I in ("%REPO_MOD%") do set "REPO_MOD=%%~fI"

if not exist "%REPO_MOD%\About\About.xml" (
  echo [错误] 找不到 "%REPO_MOD%\About\About.xml"。
  echo 请在 GitHub Desktop 克隆下来的仓库文件夹里运行本脚本。
  pause
  exit /b 1
)

set "GAME=D:\Steam\steamapps\common\RimWorld"
echo 默认游戏目录：%GAME%
set /p "INPUT=直接回车使用默认目录，或输入其它游戏目录："
if not "%INPUT%"=="" set "GAME=%INPUT%"

if not exist "%GAME%\Mods" (
  echo [错误] 找不到 "%GAME%\Mods"。请确认游戏目录。
  pause
  exit /b 1
)

set "LINK=%GAME%\Mods\MemorialDirector"
if exist "%LINK%" (
  echo [提示] "%LINK%" 已经存在。
  echo 请先把它改名为 MemorialDirector_旧版 备份（里面如果有你自己写的剧本或演员卡，
  echo 请把它们复制到仓库的 Mod\Scripts 或 Mod\Actors 里），然后再运行本脚本。
  pause
  exit /b 1
)

mklink /J "%LINK%" "%REPO_MOD%"
if errorlevel 1 (
  echo [错误] 创建链接失败。
  pause
  exit /b 1
)
echo.
echo 完成！游戏里的 Mods\MemorialDirector 现在直接指向仓库里的 Mod 文件夹。
echo 以后更新：在 GitHub Desktop 里点 Fetch origin / Pull origin，然后重启游戏即可。
pause
