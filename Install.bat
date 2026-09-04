@echo off
setlocal EnableDelayedExpansion

REM ---------------------------------------------------------------------------
REM Bad Dream: Coma - Accessibility installer
REM
REM Patches the game's data.win to add screen-reader support and keyboard
REM navigation, and drops the speech bridge next to the game.
REM
REM The original data.win is backed up first, and every run patches FROM that
REM backup - so re-running this after a mod update always produces a cleanly
REM patched file rather than layering changes on top of each other.
REM
REM Usage:  Install.bat  ["path\to\game folder"]
REM ---------------------------------------------------------------------------

set "MODDIR=%~dp0"
set "GAME=%~1"

REM --- where is the game? --------------------------------------------------
REM 1. the folder given on the command line
REM 2. the folder this installer is sitting in (unzipped into the game folder)
REM 3. the usual Steam / GOG locations
REM 4. ask
if not defined GAME if exist "%MODDIR%data.win" set "GAME=%MODDIR:~0,-1%"
if not defined GAME call :try "%ProgramFiles(x86)%\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "%ProgramFiles%\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "C:\GOG Games\Bad Dream Coma"
if not defined GAME call :try "%ProgramFiles(x86)%\GOG Galaxy\Games\Bad Dream Coma"
if not defined GAME call :try "D:\Steam\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "D:\SteamLibrary\steamapps\common\Bad Dream Coma"
if not defined GAME call :try "E:\modgames\bdc\Bad Dream Coma"
if not defined GAME (
    echo I could not find Bad Dream: Coma automatically.
    echo Paste the game folder - the one holding data.win - and press Enter.
    echo.
    set /p "GAME=Game folder: "
)
if defined GAME if "%GAME:~-1%"=="\" set "GAME=%GAME:~0,-1%"

set "DATA=%GAME%\data.win"
set "BACKUP=%GAME%\data.win.BDC-A11Y-BACKUP"
set "TEMP_OUT=%GAME%\data.win.a11y-tmp"

echo.
echo Bad Dream: Coma - Accessibility installer
echo   game : %GAME%
echo.

if not exist "%DATA%" (
    echo ERROR: data.win not found in "%GAME%".
    echo Pass the game folder as an argument, e.g.  Install.bat "D:\Games\Bad Dream Coma"
    exit /b 1
)

REM --- the patcher ---------------------------------------------------------
set "CLI="
call :tool CLI "%MODDIR%tools\UTMT_CLI\UndertaleModCli.exe"
if not defined CLI (
    echo ERROR: UndertaleModCli.exe not found under "%MODDIR%tools\UTMT_CLI".
    echo Download the UndertaleModTool CLI and put it there, or use the
    echo release package, which already includes it.
    exit /b 1
)

set "SCRIPT=%MODDIR%gscripts\inject_a11y.csx"
if not exist "%SCRIPT%" (
    echo ERROR: gscripts\inject_a11y.csx is missing next to this installer.
    exit /b 1
)

REM --- the two DLLs, wherever they happen to live --------------------------
set "SPEECHDLL="
call :tool SPEECHDLL "%MODDIR%bin\bdcspeech.dll"
call :tool SPEECHDLL "%MODDIR%src\bridge\bdcspeech.dll"
set "PRISMDLL="
call :tool PRISMDLL "%MODDIR%bin\prism.dll"
call :tool PRISMDLL "%MODDIR%src\prism\build-x86\prism.dll"

REM --- is the game running? ------------------------------------------------
tasklist /FI "IMAGENAME eq Bad Dream Coma.exe" 2>nul | find /I "Bad Dream Coma.exe" >nul
if not errorlevel 1 (
    echo NOTE: the game is running right now.
    echo   data.win will still be patched, but the running game keeps using the
    echo   copy it loaded at startup, and the speech DLLs cannot be replaced
    echo   while they are loaded. Close the game and start it again.
    echo.
)

REM --- back up the pristine data.win, once and only once -------------------
if exist "%BACKUP%" (
    echo Backup already exists, keeping it: data.win.BDC-A11Y-BACKUP
) else (
    echo Backing up data.win -^> data.win.BDC-A11Y-BACKUP
    copy /Y "%DATA%" "%BACKUP%" >nul || (
        echo ERROR: could not create the backup. Is the game running?
        exit /b 1
    )
)

REM --- patch from the backup, so re-running is always clean ----------------
echo Patching (this takes a moment - data.win is 237 MB)...
"%CLI%" load "%BACKUP%" -s "%SCRIPT%" -o "%TEMP_OUT%" >"%TEMP%\bdc_a11y_patch.log" 2>&1 <nul
if errorlevel 1 (
    echo ERROR: patching failed. Log: %TEMP%\bdc_a11y_patch.log
    if exist "%TEMP_OUT%" del /Q "%TEMP_OUT%"
    exit /b 1
)
if not exist "%TEMP_OUT%" (
    echo ERROR: patcher produced no output. Log: %TEMP%\bdc_a11y_patch.log
    exit /b 1
)

move /Y "%TEMP_OUT%" "%DATA%" >nul || (
    echo ERROR: could not replace data.win. Is the game running?
    exit /b 1
)

REM --- speech bridge -------------------------------------------------------
echo Installing speech bridge...
if defined SPEECHDLL (
    copy /Y "%SPEECHDLL%" "%GAME%\" >nul || echo   bdcspeech.dll is in use - keeping the copy already there.
) else (
    if exist "%GAME%\bdcspeech.dll" (
        echo   bdcspeech.dll already installed.
    ) else (
        echo ERROR: bdcspeech.dll missing - build it with src\bridge\build_bridge_x86.bat
        exit /b 1
    )
)
if defined PRISMDLL (
    copy /Y "%PRISMDLL%" "%GAME%\" >nul || echo   prism.dll is in use - keeping the copy already there.
) else (
    if exist "%GAME%\prism.dll" (
        echo   prism.dll already installed.
    ) else (
        echo ERROR: prism.dll missing - build it with src\build_prism_x86.bat
        exit /b 1
    )
)

echo.
echo Done. Start the game normally.
echo.
echo   Arrow keys   move between menu items
echo   Enter/Space  activate
echo   F3           repeat the current item
echo   Space        in the board game, rolls the die or moves your piece
echo   F4           where am I - room, object counts, what is blocking
echo   F5           describe the picture in this room again
echo   Ctrl         stop speech
echo.
echo   In the world:
echo   A / D        switch view - everything, exits, objects, scenery
echo   Accessibility settings - last item on the title screen and in the
echo                pause menu. Warnings, hints, area names, clutter.
echo   F1           area name in front of every entry, on/off
echo   F2           hide ambient clutter, on/off
echo   Enter        on scenery, says what the picture shows - once, until
echo                you move to something else
echo   H            check your health
echo   S            status screen
echo.
echo   In dialogue: Enter advances a line, any arrow key repeats it,
echo                Space or Escape skips the rest - the game's own keys.
echo   I            open/close the inventory reader; arrows browse, Enter
echo                selects an item, exactly as clicking it would.
echo.
echo Run Uninstall.bat to put the game back exactly as it was.
exit /b 0

:try
if exist "%~1\data.win" set "GAME=%~1"
exit /b 0

:tool
if defined %1 exit /b 0
if exist "%~2" set "%1=%~2"
exit /b 0
