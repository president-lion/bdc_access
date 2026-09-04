@echo off
setlocal

REM ---------------------------------------------------------------------------
REM Assembles release\ - exactly what goes in the downloadable package - and
REM zips it.
REM
REM   make_release.bat [version]      default: 0.91b
REM
REM The zip is named bdcaccess<version>.zip and its contents are the contents of
REM release\, so unzipping it anywhere gives a folder you can run the setup from.
REM
REM release\tools\UTMT_CLI is the UndertaleModTool CLI, copied from tools\. It is
REM 118 MB and not ours, so it is not in the repository - fetch it once into
REM tools\UTMT_CLI and this script will bundle it.
REM ---------------------------------------------------------------------------

set "HERE=%~dp0"
set "VER=%~1"
if "%VER%"=="" set "VER=0.91b"
set "REL=%HERE%release"
set "ZIP=%HERE%bdcaccess%VER%.zip"

if not exist "%HERE%bin\Setup bdc_access.exe" (
    echo Building the setup program first...
    call "%HERE%src\installer\build_installer.bat" || exit /b 1
)

echo Assembling %REL% ...
if exist "%REL%" rmdir /S /Q "%REL%"
mkdir "%REL%\gscripts" 2>nul
mkdir "%REL%\bin" 2>nul

copy /Y "%HERE%Install.bat"        "%REL%\" >nul
copy /Y "%HERE%Uninstall.bat"      "%REL%\" >nul
copy /Y "%HERE%README.md"          "%REL%\" >nul
copy /Y "%HERE%LICENSE"            "%REL%\" >nul
copy /Y "%HERE%THIRD-PARTY.txt"    "%REL%\" >nul
copy /Y "%HERE%START HERE.txt"     "%REL%\" >nul
copy /Y "%HERE%bin\Setup bdc_access.exe" "%REL%\" >nul

copy /Y "%HERE%gscripts\inject_a11y.csx"  "%REL%\gscripts\" >nul
copy /Y "%HERE%gscripts\verify_a11y.csx"  "%REL%\gscripts\" >nul
copy /Y "%HERE%gscripts\sweep_safety.csx" "%REL%\gscripts\" >nul

copy /Y "%HERE%bin\bdcspeech.dll" "%REL%\bin\" >nul
copy /Y "%HERE%bin\prism.dll"     "%REL%\bin\" >nul

if not exist "%HERE%tools\UTMT_CLI\UndertaleModCli.exe" (
    echo ERROR: tools\UTMT_CLI\UndertaleModCli.exe is missing - the package needs it.
    exit /b 1
)
echo Copying the patcher...
robocopy "%HERE%tools\UTMT_CLI" "%REL%\tools\UTMT_CLI" /E /NFL /NDL /NJH /NJS /XD Scripts >nul
if errorlevel 8 (
    echo ERROR: could not copy tools\UTMT_CLI.
    exit /b 1
)

echo Zipping -^> %ZIP%
if exist "%ZIP%" del /Q "%ZIP%"
REM ZipFile.CreateFromDirectory, not Compress-Archive: the cmdlet fell over on
REM this many files with an unhelpful constructor exception, and is far slower.
REM The last argument, false, keeps the release folder's own name out of the zip,
REM so the files land where you unzip it.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('%REL%', '%ZIP%', [System.IO.Compression.CompressionLevel]::Optimal, $false)"
if errorlevel 1 (
    echo ERROR: zipping failed.
    exit /b 1
)
if not exist "%ZIP%" (
    echo ERROR: no zip was produced.
    exit /b 1
)

echo.
echo Done: %ZIP%
exit /b 0
