# Development Guide

## Сборка

### Релиз (основной путь)
```bat
build_release.bat
```
Собирает через **Crinkler** → `zig-out\bin\WinToolbarMediaButtons.exe` (~6 КБ).  
Если `crinkler\Crinkler.exe` не найден — fallback на `zig build + UPX` (~11 КБ).

### Debug / разработка
```
zig build
```
Выход: `zig-out\bin\WinToolbarMediaButtons.exe` (x64, без оптимизаций).

### Запуск после сборки
```
zig-out\bin\WinToolbarMediaButtons.exe
```
Приложение живёт в трее. Правый клик на иконке → меню.

---

## Архитектура сборки

| Шаг | Инструмент | Результат |
|-----|-----------|-----------|
| Компиляция | `zig build-obj -target x86-windows` | `.obj` (COFF x86) |
| Линковка + сжатие | Crinkler 3.0 | ~6 КБ EXE |
| Fallback сжатие | `upx --best --lzma` | ~11 КБ EXE |

Crinkler требует x86 (32-bit) COFF — поэтому `-target x86-windows`.  
На x64 Windows работает через WoW64.

### kernel32_stdcall.lib
Crinkler требует MSVC-формат import library со stdcall-именами (`_ExitProcess@4`).  
Zig генерирует только cdecl — поэтому lib создаётся вручную скриптом:
```
powershell -ExecutionPolicy Bypass -File crinkler\make_kernel32_lib.ps1
```
`build_release.bat` запускает скрипт автоматически если lib отсутствует.

---

## Структура проекта

```
src/
  main.zig          — точка входа wWinMainCRTStartup, message loop
  win32.zig         — Win32 типы, константы, динамическая загрузка DLL
  toolbar_window.zig — создание окна, layout, WndProc, mouse handling
  renderer.zig      — D3D11 + D2D1 рендеринг через DXGI swap chain
  composition.zig   — Windows.UI.Composition (COM vtable)
  audio.zig         — WASAPI громкость + peak meter
  smtc.zig          — SMTC (Windows.Media.Control) статус воспроизведения
  tray.zig          — иконка в трее, контекстное меню
  autostart.zig     — автозагрузка через реестр HKCU\...\Run

crinkler/
  Crinkler.exe           — демосценовый линкер-компрессор
  kernel32_stdcall.lib   — MSVC-формат import library (7 функций)
  make_kernel32_lib.ps1  — генератор lib (PowerShell, без Windows SDK)

docs/
  development.md    — этот файл
  size_reduction.md — история уменьшения размера EXE
```

---

## Ключевые технические решения

### Нет std
`@import("std")` не используется нигде — тянет runtime init и увеличивает размер.  
Вместо этого: ручная конвертация чисел, `@memset`, comptime `L()` для UTF-16.

### Все DLL динамические
`user32`, `shell32`, `d3d11`, `d2d1`, `dwrite`, `combase`, `ole32`, `advapi32` —  
загружаются через `LoadLibraryW` + `GetProcAddress` в `initWin32()`.  
Статически только `kernel32`: `ExitProcess`, `GetModuleHandleW`, `LoadLibraryW`, `GetProcAddress`, `GetModuleFileNameW`.

### COM без обёрток
Все COM-вызовы — напрямую через vtable: `comp.vtbl(obj)[N]`.  
`comp.vtbl`, `comp.qi`, `comp.release` — общие хелперы в `composition.zig`.

### Точка входа
`pub export fn wWinMainCRTStartup() callconv(.winapi) noreturn` —  
убирает Zig runtime wrapper, Crinkler находит символ напрямую.

### Автозагрузка
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`  
Значение `WinToolbarMediaButtons` = полный путь к EXE (`GetModuleFileNameW`).

---

## Терминальная разработка (без Visual Studio)

### Редактор
Любой с поддержкой Zig LSP. Рекомендуется VS Code с расширением `ziglang.vscode-zig`.

### Запуск Zig LSP вручную
```
zls
```
(установить отдельно: https://github.com/zigtools/zls)

### Полезные команды
```bat
zig build                         # debug сборка
zig build -Doptimize=ReleaseSmall # release без Crinkler
zig build-obj src/main.zig -target x86-windows -O ReleaseSmall -fsingle-threaded -fstrip --name tmp
# проверить символы в .obj:
zig objdump --syms tmp.obj | findstr "wWinMain"
```

### Проверить размер секций
```bat
zig objdump --section-headers zig-out\bin\WinToolbarMediaButtons.exe
```

### Посмотреть импорты EXE
```bat
zig objdump --private-headers zig-out\bin\WinToolbarMediaButtons.exe | findstr "DLL"
```
