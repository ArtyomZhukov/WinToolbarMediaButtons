# EXE Size Reduction

## Итог

| Версия | Размер |
|--------|--------|
| C# + WinAppSDK (один EXE, без runtime) | 206 КБ |
| Чистый Zig + UPX | **11 КБ** |
| Crinkler 3.0 | **6 КБ** ✓ |

---

## Шаги уменьшения

### 1. Переписали на Zig — 60 КБ
Убрали C# и WinAppSDK, переписали рендеринг на чистом GDI.

### 2. Убрали `@import("std")` — 16 КБ (−44 КБ)
Самый большой шаг. Даже если std используется только в comptime, `@import("std")` тянет runtime initialization. Заменили:
- `std.fmt.bufPrint` → ручная конвертация числа в строку
- `std.mem.zeroes` → `@memset`
- `std.unicode` → кастомная comptime `L()` в win32.zig

### 3. Оптимизации в build.zig — 15 КБ
```zig
exe.subsystem = .Windows;
exe.root_module.strip = true;
exe.root_module.unwind_tables = .none;
exe.root_module.omit_frame_pointer = true;
exe.lto = .full;
exe.link_function_sections = true;
exe.link_data_sections = true;
exe.link_gc_sections = true;
exe.build_id = .none;
exe.linker_dynamicbase = false;
```

### 4. GetProcAddress вместо статических импортов — 14.5 КБ
Убрали `extern "user32"` и `extern "shell32"` — заменили на `GetProcAddress` через `initWin32()`. Это удалило import directory для user32/shell32 из PE-заголовка.

Статические остались только kernel32: `ExitProcess`, `GetModuleHandleW`, `LoadLibraryW`, `GetProcAddress`.

### 5. UPX —best —lzma — **11 КБ**
```bat
upx --best --lzma zig-out\bin\WinToolbarMediaButtons.exe
```
UPX с LZMA даёт ~75% от оригинального размера.

### 6. Кастомный entry point
Заменили `pub fn main() void` на `pub export fn wWinMainCRTStartup() callconv(.winapi) noreturn` — убрали Zig runtime wrapper. Экономия ~200 байт pre-UPX (в сжатом не видно).

---

## Почему мелкие оптимизации не видны после UPX

UPX с LZMA сжимает нули почти до 0. Секции `.buildid`, `.reloc` содержат почти только нули — их удаление экономит байты pre-UPX, но не меняет сжатый размер.

Чтобы уменьшить **сжатый** размер, нужно уменьшать реальный код (non-zero байты).

Pre-UPX breakdown текущего бинаря:
```
.text   — 7576 non-zero байт (код)
.rdata  — 2611 non-zero байт (строки, иконка)
.data   — 5 non-zero байт (почти BSS)
```

---

## Crinkler — следующий шаг

**Crinkler** — демосценовый линкер-компрессор, использует контекстное моделирование x86-кода вместо LZMA. Ожидаемый результат: 4–6 КБ.

### Требования
- Crinkler работает только с **x86** (32-bit) COFF объектами
- Нужен `kernel32.lib` в MSVC-формате со stdcall-именами (`_ExitProcess@4`)
- Windows SDK содержит нужный lib: `C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\um\x86\kernel32.lib`

### Статус — ВЫПОЛНЕНО ✓
- [x] Скачан Crinkler 3.0 → `crinkler/Crinkler.exe`
- [x] Создан `crinkler/kernel32_stdcall.lib` (вручную, без Windows SDK)
- [x] Создан `build_crinkler.bat`
- [x] Точка входа `wWinMainCRTStartup` в `src/main.zig`
- [x] **Результат: 6166 байт (~6 КБ)**

### Запуск
```bat
build_crinkler.bat
```

### Ручная генерация kernel32_stdcall.lib
Windows SDK не нужен! Используется PowerShell-скрипт `C:\Temp\make_kernel32_lib.ps1` который вручную генерирует COFF import archive в формате MSVC.

Lib содержит функции из двух DLL:
- **KERNEL32.DLL**: `ExitProcess`, `GetModuleHandleW`, `LoadLibraryW`, `GetProcAddress`, `LoadLibraryA`
- **USER32.DLL**: `MessageBoxA`

`LoadLibraryA` и `MessageBoxA` нужны самому Crinkler для его распаковщика.

### Ключевые нюансы формата
- Имена объектов в ar-архиве — максимум 16 байт (иначе краш)
- IAT-символ = `__imp__` + decorated_name_без_ведущего_`_` (например `__imp__ExitProcess@4`, не `__imp___ExitProcess@4`)
- `NameType=UNDECORATE (3)` в IMPORT_OBJECT_HEADER: убирает `_` и `@N` из имени для поиска в DLL

### Почему LLVM dlltool не подходит
`zig dlltool` и `zig lib` генерируют import library с cdecl-именами (`ExitProcess`), а Crinkler ищет stdcall-имена (`_ExitProcess@4`). Решение: ручная генерация COFF archive.

---

## Инструменты

| Инструмент | Использование |
|-----------|---------------|
| `build_release.bat` | Обычная сборка: Zig ReleaseSmall + UPX → **11 КБ** |
| `build_crinkler.bat` | Crinkler-сборка: x86 + Crinkler → **6 КБ** ✓ |
| `C:\Temp\make_kernel32_lib.ps1` | Генератор `crinkler/kernel32_stdcall.lib` без Windows SDK |
