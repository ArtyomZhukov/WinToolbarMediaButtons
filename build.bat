@echo off
setlocal

if exist "%~dp0build" rmdir /s /q "%~dp0build"

echo Building...
echo.

dotnet publish "%~dp0WinToolbarMediaButtons.csproj" ^
    -c Release ^
    -r win-x64 ^
    --no-self-contained ^
    -p:DebugSymbols=false ^
    -p:DebugType=none ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%~dp0build\publish"

if %ERRORLEVEL% neq 0 (
    echo.
    echo BUILD FAILED
    pause & exit /b 1
)

echo.
for %%F in ("%~dp0build\publish\WinToolbarMediaButtons.exe") do echo Size: %%~zF bytes
echo.
echo Done: %~dp0build\publish\WinToolbarMediaButtons.exe
echo.
pause
