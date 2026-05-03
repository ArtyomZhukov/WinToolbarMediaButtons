# WinForms Migration Log

Миграция WinToolbarMediaButtons с WinUI 3 / Windows App SDK на WinForms.

## Мотивация

| Проблема | Деталь |
|----------|--------|
| Размер publish | ~170 МБ (`Microsoft.Windows.SDK.NET.dll` + WinAppSDK runtime) |
| Цель | < 5 МБ (framework-dependent публикация) |
| Корень проблем | WinUI 3 внутренне несовместим с режимом WS_CHILD (дочернее окно Shell_TrayWnd) |

WinUI 3-краши, полученные за время работы с WS_CHILD:

| Симптом | Причина |
|---------|---------|
| `STATUS_STOWED_EXCEPTION` (0xc000027b) при клике на Slider | WinUI Slider + PointerCapture ломается в WS_CHILD |
| `E_POINTER` → CoreMessagingXP crash при `AppWindow.Resize` | COM IAppWindow инвалидируется после `SetParent` |
| `STATUS_STOWED_EXCEPTION` при показе/скрытии слайдера | `AppWindow.Resize` вызывал reentrancy в XAML через синхронный `WM_SIZE` |
| XAML noexcept crash на `ExtendsContentIntoTitleBar` | Применяется лениво при первом `WM_SHOWWINDOW` как WS_CHILD |
| MenuFlyout в неправильной позиции | `GetPosition(element)` не учитывает offset Shell_TrayWnd |
| XAML parsing failed при publish | `WinToolbarMediaButtons.pri` не копировался publish-таргетом |

---

## Что изменилось

### Удалено
- `App.xaml` / `App.xaml.cs`
- `MainWindow.xaml` / `MainWindow.xaml.cs`
- `Services/AppBarService.cs` (WndProc subclass)
- NuGet: WinAppSDK, `H.NotifyIcon.WinUI`, `Microsoft.Windows.SDK.BuildTools`
- TFM: `net10.0-windows10.0.22621.0` → `net10.0-windows` (убрало `Microsoft.Windows.SDK.NET.dll`, 25 МБ)

### Добавлено
- `MainForm.cs` — WinForms `Form` без рамки, весь UI через `OnPaint` (GDI+)
- `Program.cs` — WinForms entry point

### Не тронуто
- `Services/WasapiMonitorService.cs` — WASAPI peak meter (COM, без WinRT)
- `Services/VolumeEndpointService.cs` — IAudioEndpointVolume (COM)
- `Services/MediaKeyService.cs` — keybd_event P/Invoke
- `Services/AutostartService.cs` — реестр HKCU\Run

### Результат
Publish: 170 МБ → 194 КБ (framework-dependent, `--no-self-contained`)

---

## WS_CHILD Embedding — история попыток

Это был основной технический вызов. Цель: форма должна появляться внутри taskbar как дочернее окно `Shell_TrayWnd`.

### Попытка 1: SetParent в OnHandleCreated

```csharp
protected override void OnHandleCreated(EventArgs e)
{
    base.OnHandleCreated(e);
    var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
    int style = GetWindowLong(Handle, GWL_STYLE);
    SetWindowLong(Handle, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
    SetParent(Handle, taskbarHwnd);
    SetWindowPos(Handle, HWND_TOP, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
}
```

**Результат: ❌ форма не появлялась в тулбаре.**

Причина: WinForms вызывает `SetWindowPos` ПОСЛЕ возврата из `OnHandleCreated` — внутри `SetVisibleCore` / `Form.Show`. Это переопределяет наш `SetParent` и возвращает окно на экран как обычное окно.

### Попытка 2: CreateParams + SetBoundsCore

Встраивание перенесено в `CreateParams` — WinForms создаёт HWND уже как WS_CHILD Shell_TrayWnd до любого другого кода:

```csharp
protected override CreateParams CreateParams
{
    get
    {
        var cp = base.CreateParams;
        var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd != IntPtr.Zero)
        {
            cp.Style  = (cp.Style & ~WS_POPUP) | WS_CHILD;
            cp.Parent = taskbarHwnd;
            cp.X = 0; cp.Y = 0;
            cp.Width = LogicalWidth; cp.Height = LogicalHeight;
        }
        return cp;
    }
}
```

`SetBoundsCore` блокирует попытки WinForms переместить окно после встраивания:

```csharp
protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
{
    if (_embedded) return;
    base.SetBoundsCore(x, y, width, height, specified);
}
```

`OnHandleCreated` выполняет DPI-корректный ресайз и взводит флаг:

```csharp
SetWindowPos(Handle, HWND_TOP, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
_embedded = true;
```

**Результат: ❌ форма по-прежнему не появлялась в тулбаре.**

Состояние на момент остановки: неизвестно, срабатывает ли `CreateParams` корректно; не исключено что WinForms игнорирует `cp.Parent` для top-level форм при `Show`.

---

## Текущее состояние кода

`MainForm.cs` — полностью написан, компилируется без ошибок и предупреждений.

Логика встраивания (попытка 2) на месте. Форма визуально готова — `OnPaint` рисует все кнопки, глифы Segoe Fluent Icons, volume bar. Остальная функциональность (WASAPI, громкость, drag, tray icon, автозапуск) работает.

**Нерешённая проблема:** форма не отображается в тулбаре при запуске.

---

## Попытка 3: HWND_TOPMOST overlay поверх тасклета

Окно не является WS_CHILD. Обычный top-level WS_POPUP с `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`.  
Позиция берётся через `GetWindowRect(Shell_TrayWnd)`, окно ставится туда же через `SetWindowPos(HWND_TOPMOST)`.  
Таймер 100 мс удерживает HWND_TOPMOST.

**Результат: ✅ окно видно** — GDI рисует нормально вне DC-слоя тасклета.  
**Проблема: ❌ z-order** — при клике на Пуск окно уходит под тасклет и не возвращается.  
Таймер не успевает: Shell перемещает наше окно в нижний z-band до следующего тика.

---

## Корень проблемы с видимостью (подтверждено)

**Windows 11 taskbar рендерит всё через DirectComposition.** GDI-пиксели WS_CHILD окна composite-ятся DWM-ом ПОД DC-слоем Shell_TrayWnd — и остаются невидимы независимо от HWND z-order.

WinUI 3 работал как WS_CHILD потому что XAML Island сам является DirectComposition-визуалом и участвует в DC-слое.

---

## Следующие попытки

### Попытка 4: WS_EX_LAYERED + WS_CHILD *(в процессе)*

Layered child windows (Windows 8+) composited DWM-ом отдельно от GDI-pipeline.  
Гипотеза: layered child окажется выше DC-слоя тасклета.

```csharp
// CreateParams
cp.Style  = (cp.Style & ~WS_POPUP) | WS_CHILD;
cp.Parent = taskbarHwnd;
cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE;

// OnHandleCreated
SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
```

**Результат: ❌ не отображается.** DWM composites layered child тоже под DC-слоем Shell_TrayWnd.

---

### Вариант B: SHAppBarMessage(ABM_SETPOS)

Регистрация как Application Desktop Toolbar даёт отдельный Shell Z-band над тасклетом.  
GDI рисует нормально (не WS_CHILD), z-order стабилен гарантированно.  
**Трейдофф:** work area уменьшается на ~52px — максимизированные окна не доходят до тасклета.  
**Усилие:** среднее (~50 строк).

---

### Вариант C: WPF как WS_CHILD

WPF рендерит через DirectX/MIL — возможно, виден внутри DC-слоя тасклета в отличие от WinForms/GDI.  
`.NET Desktop Runtime` включает WPF → publish-size тот же ~200 КБ.  
**Трейдофф:** переписывать UI на XAML. Неизвестно есть ли те же крэши что у WinUI 3 в WS_CHILD.

---

### Вариант D: Рисовать в WM_ERASEBKGND Shell_TrayWnd

`SetWindowSubclass` на Shell_TrayWnd, рисовать свой контент в HDC тасклета.  
**Трейдофф:** хрупко, обновления Windows могут сломать, риск крэша Explorer.

---

## Текущая стратегия: overlay поверх тасклета + стабилизация z-order

WS_CHILD внутри Shell_TrayWnd невозможен для GDI (ни обычный, ни Layered) из-за DirectComposition.  
Вместо этого: обычное top-level окно, позиционированное поверх тасклета физически.  
Проблема: при клике на Пуск окно уходит под тасклет (z-band механизм Shell).

### Попытка 5: SetWindowBand (undocumented) *(в процессе)*

`user32.dll` содержит недокументированную функцию `SetWindowBand` которая помещает окно  
в тот же z-band что и сам тасклет (`ZBID_IMMERSIVE_MOGO = 5`).  
Окно остаётся top-level (overlay), но находится в shell z-band — Start menu его не утопит.

```csharp
[DllImport("user32.dll")] static extern bool SetWindowBand(IntPtr hwnd, IntPtr hwndInsertAfter, uint dwBand);
// После позиционирования:
SetWindowBand(Handle, IntPtr.Zero, 5); // ZBID_IMMERSIVE_MOGO
```

**Риск:** недокументировано, может не работать на некоторых сборках Windows 11.

---

### Попытка 6: WPF как WS_CHILD

WPF рендерит через DirectX/MIL — гипотеза: может быть виден внутри DC-слоя Shell_TrayWnd.  
`.NET Desktop Runtime` включает WPF → publish-size тот же ~200 КБ.  
Реализовано через `HwndSource` с `WS_CHILD | WS_VISIBLE`, parent = Shell_TrayWnd.

**Результат: ❌ не отображается.** WPF использует DXGI swap chain через DWM redirection path, не DirectComposition visual tree — compositing происходит так же как у GDI, ниже DC-слоя Shell_TrayWnd.

---

## Итоговый вывод по WS_CHILD

| Рендеринг | Результат |
|-----------|-----------|
| GDI (WinForms OnPaint) | ❌ invisible под DC-слоем |
| GDI + WS_EX_LAYERED | ❌ invisible под DC-слоем |
| WPF (DirectX / DXGI) | ❌ invisible под DC-слоем |
| WinUI 3 (XAML Island / DComp) | ✅ visible — участвует в DC visual tree |

**Вывод:** единственный способ быть видимым внутри Shell_TrayWnd — рендерить через DirectComposition visual tree. В управляемом C# это доступно только через WinUI 3 / XAML Island.

---

## ✅ Финальное решение: WinUI 3 + WinAppSDK 1.7 + PublishSingleFile

### Итоговая конфигурация

| Параметр | Значение |
|----------|----------|
| NuGet WinAppSDK | `1.7.*` |
| `WindowsAppSDKSelfContained` | `false` |
| `SelfContained` | `false` |
| `PublishSingleFile` | `true` |
| `IncludeNativeLibrariesForSelfExtract` | `true` |
| `EnableMsixTooling` | `true` |
| Publish size | **~40 МБ** (единый EXE) |

### Z-order: стабилен ✅

При клике на Пуск окно НЕ уходит под тасклет. Решение — `AppBarService`:
- `SetWindowSubclass` перехватывает `WM_WINDOWPOSCHANGING` → блокирует изменение z-order
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` → восстанавливает topmost при смене foreground
- `SHAppBarMessage(ABM_NEW)` → регистрирует окно в z-band Shell'а
- `SetProp(hwnd, "NonRudeHWND")` → запрещает Rude Window Manager опускать окно

---

## Проблемы при возврате к WinUI 3

### Проблема 1: Версия WinAppSDK NuGet не совпадает с установленным runtime

**Симптом:** диалог "This application requires the Windows App Runtime version 1.6 (MSIX package version >= 6000.519.329.0)"

**Причина:** NuGet `1.6.*` резолвится в `1.6.250602001` (июнь 2025), но bootstrap от этой версии требует runtime новее установленного `6000.519.329.0` (= WinAppSDK `1.6.5`, май 2025).

**Решение:** Привязать NuGet к версии, соответствующей установленному runtime. Декодирование MSIX-версии:
```
6000.519.329.0  →  6000 = WinAppSDK 1.6,  519 = май 19  →  NuGet 1.6.250519001
7000.785.xxx.0  →  7000 = WinAppSDK 1.7
8000.836.xxx.0  →  8000 = WinAppSDK 1.8   ← использовали
```

Команда проверки установленных версий:
```powershell
Get-AppxPackage | Where-Object { $_.Name -match "WindowsAppRuntime" } | Select-Object Name, Version
```

На машине установлен `1.8` → итоговый выбор: `Version="1.8.*"`.

---

### Проблема 2: PublishSingleFile падает сразу при запуске (0x80670016)

**Симптом:** EXE запускается и сразу пропадает из диспетчера задач. В Event Log: `Bootstrapper initialization failed`.

**Причина:** `IncludeAllContentForSelfExtract=true` (обязательное требование WinAppSDK .targets) упаковывает нативный `Microsoft.WindowsAppRuntime.Bootstrap.dll` в EXE. При запуске он распаковывается в `%TEMP%\.net\...\` и оттуда не может найти установленный WinAppSDK runtime.

**Решение:** `[ModuleInitializer]` устанавливает env var до инициализации WinAppSDK:

```csharp
// Init.cs
using System.Runtime.CompilerServices;
namespace WinToolbarMediaButtons;
static class Startup
{
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            AppContext.BaseDirectory);
    }
}
```

Для framework-dependent single-file (`SelfContained=false`) `AppContext.BaseDirectory` указывает на директорию EXE — это сообщает bootstrap'у корректную точку отсчёта.

---

### Проблема 3: XAML noexcept crash на ExtendsContentIntoTitleBar

**Симптом:** краш при первом `WM_SHOWWINDOW` после `ReparentToTaskbar`.

**Причина:** `AppWindow.TitleBar.ExtendsContentIntoTitleBar = true` применяется лениво при первом показе окна, уже как `WS_CHILD` — это крашит WinUI 3.

**Решение:** убрать блок `AppWindow.TitleBar.*` из `ConfigureWindow`. `SetBorderAndTitleBar(false, false)` и так убирает рамку.

---

## Оптимизация размера EXE

Baseline: WinAppSDK 1.8 + PublishSingleFile = **~82 МБ**

Managed DLL — основные виновники размера:

| Файл | Размер |
|------|--------|
| `Microsoft.Windows.SDK.NET.dll` | ~25 МБ |
| `Microsoft.WinUI.dll` | ~7 МБ |
| `Microsoft.InteractiveExperiences.Projection.dll` | ~1.5 МБ |
| `System.Private.Windows.Core.dll` | ~1.2 МБ |
| `Microsoft.Web.WebView2.Core.dll` | ~0.8 МБ |
| `H.NotifyIcon` + `System.Drawing.Common` | ~3–4 МБ |

### Вариант 1: WinAppSDK 1.7 вместо 1.8 ✅

1.6 — не существует нужный NuGet `1.6.250519001`, а `1.6.250602001` требует runtime новее установленного `6000.519.329.0`.  
1.7 (`7000.785.2325.0`) установлен и работает.

- Размер: **~40 МБ** (вдвое меньше 1.8)
- Риск: минимальный
- Усилие: изменить одну строку в csproj

### Вариант 2: Убрать H.NotifyIcon.WinUI → нативный Shell_NotifyIcon

`H.NotifyIcon.WinUI` тянет `System.Drawing.Common`, `System.Private.Windows.Core`, WebView2. Замена на P/Invoke `Shell_NotifyIcon` — ~50 строк кода, нет зависимостей.

- Ожидаемый размер: **−3–4 МБ**
- Риск: минимальный
- Усилие: небольшое

### Вариант 3: PublishTrimmed

Триммер удаляет неиспользуемый код. `Microsoft.Windows.SDK.NET.dll` (25 МБ) использован на ~1% — потенциально сжимается до 2–5 МБ. WinUI 3 + trimming официально не поддерживается (XAML-рефлексия), требует аннотаций.

- Ожидаемый размер: **10–25 МБ** (при удаче)
- Риск: средний — XAML может упасть в рантайме
- Усилие: среднее

### Вариант 4 (комбо): 1.6 + убрать H.NotifyIcon

- Ожидаемый размер: **~35 МБ**

### Вариант 5 (комбо): 1.6 + убрать H.NotifyIcon + trimming

- Ожидаемый размер: **10–20 МБ** при удаче
- Риск: высокий
