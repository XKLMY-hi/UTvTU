@echo off
REM Build vst_bridge.dll for UTvTU
REM Prerequisites: Visual Studio 2022 with C++ tools, VST3 SDK in ..\vst3sdk

setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: vcvars64.bat not found. Install Visual Studio 2022 with C++ tools.
    exit /b 1
)

cd /d "%~dp0"

REM Clean build
if exist build rmdir /s /q build
mkdir build

set CMAKE="C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"

echo === Configuring ===
%CMAKE% -S . -B build -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo === Building ===
%CMAKE% --build build --config Release
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo === Installing ===
copy /Y build\Release\vst_bridge.dll ..\win-x64\native\vst_bridge.dll
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo.
echo === SUCCESS: vst_bridge.dll built and installed ===
echo    Source: build\Release\vst_bridge.dll
echo    Target: ..\win-x64\native\vst_bridge.dll
endlocal
