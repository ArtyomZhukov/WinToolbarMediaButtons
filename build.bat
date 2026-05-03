@echo off
setlocal

if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"

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
    -o "%~dp0publish"

if %ERRORLEVEL% neq 0 (
    echo.
    echo BUILD FAILED
    pause & exit /b 1
)

echo.
for %%F in ("%~dp0publish\WinToolbarMediaButtons.exe") do echo Size: %%~zF bytes
echo.
echo Done: %~dp0publish\WinToolbarMediaButtons.exe
echo.
pause
