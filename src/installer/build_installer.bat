@echo off
setlocal

REM ---------------------------------------------------------------------------
REM Builds the windowed installer.
REM
REM Uses the C# compiler that ships with .NET Framework 4, which is already on
REM every Windows 10 and 11 machine - so there is nothing to install to build
REM this, and nothing to install to run it.
REM
REM Output: bin\Setup bdc_access.exe
REM ---------------------------------------------------------------------------

set "HERE=%~dp0"
set "OUT=%HERE%..\..\bin\Setup bdc_access.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo ERROR: csc.exe not found at "%CSC%".
    echo .NET Framework 4 is missing, which should not happen on Windows 10 or 11.
    exit /b 1
)

echo Building the installer...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /out:"%OUT%" ^
    /reference:System.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    "%HERE%BdcAccessInstaller.cs"
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo Built: %OUT%
exit /b 0
