@echo off
setlocal
set VS=C:\Program Files\Microsoft Visual Studio\18\Community
call "%VS%\VC\Auxiliary\Build\vcvars32.bat" || exit /b 1
set PATH=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;%PATH%
cd /d "e:\modgames\bdc\mod\src\prism" || exit /b 1
cmake -S . -B build-x86 -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DBUILD_SHARED_LIBS=ON ^
  -DPRISM_ENABLE_TESTS=OFF ^
  -DPRISM_ENABLE_DEMOS=OFF ^
  -DPRISM_ENABLE_GDEXTENSION=OFF ^
  -DPRISM_ENABLE_SHIMS=OFF || exit /b 1
cmake --build build-x86 --config Release || exit /b 1
echo BUILD_OK
