# Plan: Native rewrite (C / Zig → ~20–50 KB EXE)

## Цель

Заменить .NET single-file EXE (~203 KB) на нативный Windows PE без зависимости от рантайма.
Целевой размер: **20–50 KB** (один .exe, только системные DLL).

---

## Выбор языка: C vs Zig

### C (MSVC или MinGW/Clang)

**Плюсы:**
- Максимально знаком, миллионы примеров Win32/COM на Stack Overflow
- MSVC умеет собирать без CRT (`/NODEFAULTLIB`, кастомный entry point) → маленький PE
- Windows SDK хедеры для D2D1/DXGI/DirectComposition идут «из коробки»
- `.rc` файлы — стандартный способ встроить иконку и манифест

**Минусы:**
- Нужен MSVC или MinGW + Windows SDK — громоздкий тулчейн
- COM через `lpVtbl` — очень многословно (`((IFoo*)p)->lpVtbl->Method(p, ...)`)
- HRESULT-ошибки надо проверять вручную, забываешь → баги
- Нет сборщика зависимостей — нужен CMake / nmake

### Zig

**Плюсы:**
- **Меньший бинарь по умолчанию** — Zig с `-Osize` + `link_libc = false` даёт самые маленькие PE
- Тулчейн встроенный: `zig build` — один бинарь, нет MSVC/CMake
- Можно `@cImport(@cInclude("d2d1.h"))` — те же Windows SDK хедеры, без переписывания
- Обработка ошибок через `!T` / `try` — чище, чем ручные `if (FAILED(hr))`
- `comptime` — GUID-константы и vtable-структуры генерируются элегантно
- Cross-compilation из коробки (хотя нам не нужна)

**Минусы:**
- Язык pre-1.0 (0.14.x), между версиями бывают breaking changes
- Меньше ресурсов по Win32/COM конкретно на Zig
- COM vtable structs нужно определять вручную (или тянуть пакет `zigwin32`)

### Вывод: **Zig**

Наша главная цель — минимальный бинарь. Zig выигрывает по размеру за счёт агрессивного оптимизатора и отсутствия CRT-накладных расходов. При этом весь наш COM/vtable код (уже отлаженный в C#) переносится практически 1-в-1 — мы уже работаем с сырыми указателями и vtable-смещениями.

Если что-то не компилируется — всегда можно дописать C-файл и скомпилировать через `zig cc`.

---

## Архитектура нативного приложения

```
WinToolbarMediaButtons.exe  (~20–50 KB)
├── WinMain (entry point)
├── TrayIcon         — Shell_NotifyIconW + popup menu
├── ToolbarWindow    — WS_CHILD HWND дочерний Shell_TrayWnd
├── CompositionLayer — те же COM vtable вызовы (ICompositor, ISpriteVisual, ...)
├── D2D1Renderer     — рендеринг через ID2D1DeviceContext + IDWriteFactory
├── AudioControl     — IAudioEndpointVolume (WASAPI)
└── AutostartService — RegOpenKeyExW / RegSetValueExW
```

Зависимости только на системные DLL:
`user32.dll`, `shell32.dll`, `ole32.dll`, `dxgi.dll`, `d2d1.dll`, `dwrite.dll`,
`mmdevapi.dll`, `combase.dll`

---

## Ключевые изменения относительно C# версии

| Компонент | C# сейчас | Нативный |
|---|---|---|
| Message loop | WinForms `Application.Run` | `GetMessage` / `DispatchMessage` Win32 |
| Tray icon | `NotifyIcon` (WinForms) | `Shell_NotifyIconW` + `WM_APP+1` |
| Контекстное меню | `ContextMenuStrip` | `CreatePopupMenu` + `TrackPopupMenu` |
| Рендеринг | GDI+ (`System.Drawing`) → D2D1 upload | D2D1 напрямую (нет CPU roundtrip) |
| Текст/глифы | GDI+ `DrawString` | DirectWrite `IDWriteTextLayout` |
| COM interop | `delegate* unmanaged` + `Marshal` | прямые указатели, нет маршаллинга |
| Авто-запуск | `Registry` (.NET) | `RegSetValueExW` |
| Сборка | `dotnet publish` | `zig build` |

---

## Фазы

### Фаза 1: Scaffold проекта

- [ ] Установить Zig 0.14.x
- [ ] Создать `native/` рядом с C# проектом (или отдельный репо)
- [ ] `build.zig` с таргетом `x86_64-windows-gnu`, `-Osize`, `subsystem = windows`
- [ ] `WinMain` точка входа, пустое окно, пустой message loop
- [ ] Встроить `app.ico` и `app.manifest` через `.rc`-файл или `@embedFile`
- [ ] Убедиться что собирается: `zig build` → PE файл

### Фаза 2: Win32 окно в тулбаре

- [ ] `FindWindowW(L"Shell_TrayWnd", NULL)` — найти taskbar
- [ ] `RegisterClassExW` + `CreateWindowExW` с `WS_CHILD | WS_VISIBLE` в Shell_TrayWnd
- [ ] `WM_NCHITTEST → HTTRANSPARENT` (чтобы клики проходили)
- [ ] Тест: окно появляется в taskbar без артефактов
- [ ] `WM_MOUSEMOVE`, `WM_LBUTTONDOWN`, `WM_LBUTTONUP`, `WM_RBUTTONDOWN`
- [ ] Логика hit-testing кнопок и слайдера (скопировать из C# ToolbarWindow)

### Фаза 3: Tray icon

- [ ] Зарегистрировать `WM_APP+1` как callback сообщение
- [ ] `Shell_NotifyIconW(NIM_ADD, ...)` при старте
- [ ] `Shell_NotifyIconW(NIM_DELETE, ...)` при выходе
- [ ] Правый клик → `CreatePopupMenu` + пункты «Автозапуск» / «Выход»
- [ ] `TrackPopupMenu` + `WM_COMMAND` для обработки

### Фаза 4: Windows.UI.Composition (COM vtable)

Переносим COM-вызовы из C# в Zig. Логика идентична — только синтаксис.

- [ ] Определить vtable-структуры для: `ICompositorDesktopInterop`, `ICompositorInterop`,
  `IVisual`, `IVisual2`, `ISpriteVisual`, `ICompositionTarget`,
  `ICompositionSurfaceBrush`
- [ ] `RoGetActivationFactory` → `ICompositor`
- [ ] `ICompositorDesktopInterop.CreateDesktopWindowTarget`
- [ ] `ISpriteVisual` как root, `IVisual2.SetRelativeSize(1,1)`
- [ ] `ICompositorInterop.CreateCompositionSurfaceForSwapChain` → `ICompositionSurfaceBrush`
- [ ] `ISpriteVisual.SetBrush`

### Фаза 5: DXGI + D2D1 рендеринг

Главное изменение: убираем GDI+, рисуем через D2D1 напрямую.

**Инициализация:**
- [ ] `D3D11CreateDevice` → `IDXGIDevice` → `ID2D1Factory1.CreateDevice` → `ID2D1DeviceContext`
- [ ] `IDXGIFactory2.CreateSwapChainForComposition` (тот же swap chain что сейчас)
- [ ] `ID2D1DeviceContext.CreateBitmapFromDxgiSurface` (render target на swap chain)

**Рендеринг `Render()`:**
- [ ] `ID2D1DeviceContext.BeginDraw()`
- [ ] Фон: `ID2D1RenderTarget.FillRoundedRectangle` (тёмный цвет с alpha)
- [ ] Кнопки: `FillRoundedRectangle` для hover/press overlay
- [ ] Разделитель: `DrawLine`
- [ ] Слайдер bg: `FillRoundedRectangle`
- [ ] Volume fill: `FillRoundedRectangle` (ширина = процент)
- [ ] Глифы: `IDWriteFactory.CreateTextFormat` (Segoe Fluent Icons) + `DrawTextLayout`
- [ ] `ID2D1DeviceContext.EndDraw()`
- [ ] `IDXGISwapChain.Present(1, 0)`

Цвета и размеры — константы, скопировать из `RenderToolbarBgra()` в C#.

### Фаза 6: Audio (WASAPI)

- [ ] `CoCreateInstance(CLSID_MMDeviceEnumerator)` → `IMMDeviceEnumerator`
- [ ] `GetDefaultAudioEndpoint(eRender, eConsole)` → `IMMDevice`
- [ ] `IMMDevice.Activate(IID_IAudioEndpointVolume)` → `IAudioEndpointVolume`
- [ ] `GetMasterVolumeLevelScalar` / `SetMasterVolumeLevelScalar`
- [ ] `GetMute` / `SetMute`
- [ ] `RegisterControlChangeNotify` для реакции на внешнее изменение громкости

### Фаза 7: Media keys (SMTC)

- [ ] `SendMessageW(WM_APPCOMMAND)` — предыдущий/следующий/пауза
- [ ] Или `keybd_event(VK_MEDIA_*)` если APPCOMMAND не работает

### Фаза 8: Автозапуск

- [ ] `RegOpenKeyExW(HKEY_CURRENT_USER, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run")`
- [ ] `RegQueryValueExW` — проверить включён ли
- [ ] `RegSetValueExW` / `RegDeleteValueW` — включить / выключить

### Фаза 9: Финальная оптимизация размера

- [ ] `build.zig`: `-Osize`, `strip = true`, `single_threaded = true`
- [ ] Убрать все `std.debug.print` / panic messages из release
- [ ] Проверить что в PE нет лишних секций (`.pdata`, debug info)
- [ ] Опционально: UPX поверх (обычно ещё -50%, но ломает некоторые антивирусы)
- [ ] Замерить итоговый размер

---

## Структура файлов

```
WinToolbarMediaButtons/
├── native/                     ← новый Zig проект
│   ├── build.zig
│   ├── src/
│   │   ├── main.zig            ← WinMain, message loop
│   │   ├── tray.zig            ← Shell_NotifyIconW, меню
│   │   ├── toolbar_window.zig  ← HWND, hit test, mouse
│   │   ├── composition.zig     ← COM vtable (ICompositor, ...)
│   │   ├── renderer.zig        ← D2D1 + DirectWrite
│   │   ├── audio.zig           ← WASAPI
│   │   ├── autostart.zig       ← Registry
│   │   └── com.zig             ← GUID helpers, IUnknown, vtable utils
│   └── res/
│       ├── app.ico
│       └── app.manifest
└── (существующий C# проект остаётся)
```

---

## Ожидаемый размер по фазам

| После фазы | Ориентировочный размер |
|---|---|
| Фаза 1 (scaffold) | ~8 KB |
| Фаза 2–3 (окно + tray) | ~15 KB |
| Фаза 4 (Composition) | ~25 KB |
| Фаза 5 (D2D1 рендеринг) | ~35 KB |
| Фаза 6–8 (audio + autostart) | ~40 KB |
| Фаза 9 (оптимизация) | **~20–35 KB** |

---

## Риски

| Риск | Митигация |
|---|---|
| Zig 0.14 ломает синтаксис при обновлении | Зафиксировать версию в `.tool-versions` / `flake.nix` |
| DirectWrite с Segoe Fluent Icons даёт другой результат | Тестировать глифы отдельно на фазе 5 |
| `WS_CHILD` в Shell_TrayWnd вдруг меняет поведение | Логика та же что в C# — уже проверена |
| COM vtable смещения отличаются от C# версии | Использовать те же GUIDs и смещения, сверять с C# |
