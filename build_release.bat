@echo off
zig build -Doptimize=ReleaseSmall -Dtarget=x86_64-windows
pause
