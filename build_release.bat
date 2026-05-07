@echo off
setlocal

set CRINKLER=%~dp0crinkler\Crinkler.exe
set KERNEL32_LIB=%~dp0crinkler\kernel32_stdcall.lib
set OUT=%~dp0zig-out\bin\WinToolbarMediaButtons.exe

if not exist "%CRINKLER%" goto :upx_build

echo [1/2] Compiling x86 obj...
zig build-obj src/main.zig ^
  -O ReleaseSmall ^
  -target x86-windows ^
  -fsingle-threaded ^
  -fstrip ^
  --name WinToolbarMediaButtons_x86 ^
  --cache-dir .zig-cache ^
  --global-cache-dir .zig-cache
if %errorlevel% neq 0 goto :end

if not exist "%KERNEL32_LIB%" (
    echo Generating kernel32_stdcall.lib...
    powershell -ExecutionPolicy Bypass -File "%~dp0crinkler\make_kernel32_lib.ps1"
)

if not exist "%~dp0zig-out\bin" mkdir "%~dp0zig-out\bin"

echo [2/2] Linking with Crinkler...
"%CRINKLER%" /SUBSYSTEM:WINDOWS /ENTRY:wWinMainCRTStartup ^
  /HASHTRIES:1000 /ORDERTRIES:100000 /COMPMODE:SLOW /TINYIMPORT ^
  /OUT:"%OUT%" ^
  "%KERNEL32_LIB%" ^
  WinToolbarMediaButtons_x86.obj
del /q WinToolbarMediaButtons_x86.obj 2>nul
goto :result

:upx_build
echo [Crinkler not found -- fallback: zig build + UPX]
zig build -Doptimize=ReleaseSmall -Dtarget=x86_64-windows
if %errorlevel% neq 0 goto :end
where upx >nul 2>&1 && upx --best --lzma "%OUT%"

:result
echo.
echo Result:
for %%F in ("%OUT%") do echo   %%F  %%~zF bytes

:end
