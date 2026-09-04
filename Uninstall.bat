@echo off
setlocal

REM ---------------------------------------------------------------------------
REM Bad Dream: Coma - Accessibility uninstaller
REM
REM Restores the original data.win from the backup and removes the speech
REM bridge, leaving the game exactly as it was before installing.
REM
REM Usage:  Uninstall.bat  ["path\to\game folder"]
REM ---------------------------------------------------------------------------

set "MODDIR=%~dp0"
set "GAME=%~1"

if not defined GAME if exist "%MODDIR%data.win" set "GAME=%MODDIR:~0,-1%"
if not defined GAME call :try "%ProgramFiles(x86)%\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "%ProgramFiles%\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "C:\GOG Games\Bad Dream Coma"
if not defined GAME call :try "%ProgramFiles(x86)%\GOG Galaxy\Games\Bad Dream Coma"
if not defined GAME call :try "D:\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "D:\SteamLibrary\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "E:\modgames\bdc\Bad Dream Coma"
if not defined GAME (
    echo Paste the game folder - the one holding data.win - and press Enter.
    set /p "GAME=Game folder: "
)
if defined GAME if "%GAME:~-1%"=="\" set "GAME=%GAME:~0,-1%"

set "DATA=%GAME%\data.win"
set "BACKUP=%GAME%\data.win.BDC-A11Y-BACKUP"

echo.
echo Bad Dream: Coma - Accessibility uninstaller
echo   game : %GAME%
echo.

if not exist "%BACKUP%" (
    echo ERROR: no backup found at "%BACKUP%".
    echo Nothing was restored - verify the game files through GOG/Steam instead.
    exit /b 1
)

echo Restoring original data.win...
copy /Y "%BACKUP%" "%DATA%" >nul || (
    echo ERROR: could not restore data.win. Is the game running?
    exit /b 1
)
del /Q "%BACKUP%"

if exist "%GAME%\bdcspeech.dll" del /Q "%GAME%\bdcspeech.dll"
if exist "%GAME%\prism.dll"     del /Q "%GAME%\prism.dll"

echo.
echo Done. The game is back to its original state.
exit /b 0

:try
if exist "%~1\data.win" set "GAME=%~1"
exit /b 0
