@echo off
setlocal
set VS=C:\Program Files\Microsoft Visual Studio\18\Community
call "%VS%\VC\Auxiliary\Build\vcvars32.bat" || exit /b 1

cd /d "%~dp0" || exit /b 1

set PRISM=..\prism
set BUILD=..\prism\build-x86

cl /nologo /LD /O2 /W3 /MT ^
   /I "%PRISM%\include" /I "%BUILD%\generated\include" /I "%BUILD%" ^
   bdcspeech.c ^
   /link /OUT:bdcspeech.dll /MACHINE:X86 "%BUILD%\prism.lib" ole32.lib oleaut32.lib || exit /b 1

echo BRIDGE_OK
