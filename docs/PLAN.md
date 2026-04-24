# WinToolbarMediaButtons — C# WinUI 3

Портативная панель медиа-кнопок для Windows 11, прилипающая к нижнему левому углу экрана поверх тулбара.

## Кнопки

- ⏮ Prev track
- ⏯ Play / Pause (иконка меняется в зависимости от состояния воспроизведения)
- ⏭ Next track
- 🔉 Volume Down
- 🔊 Volume Up
- 🔇 Mute toggle

## Стек

- .NET 10 + WinUI 3 (Windows App SDK 1.6+)
- `H.NotifyIcon.WinUI` — трей-иконка
- P/Invoke `keybd_event` — медиа-клавиши и громкость
- `GlobalSystemMediaTransportControlsSessionManager` — мониторинг состояния воспроизведения
- Реестр `HKCU\...\Run` — автозапуск без прав администратора
- DWM API — убранная обводка, скруглённые углы
- `DesktopAcrylicBackdrop` — фон окна (решает проблему белого XAML Island)
- `SetWindowSubclass` (comctl32) — перехват WndProc для управления Z-порядком

## Структура проекта

```
WinToolbarMediaButtons/
├── WinToolbarMediaButtons.sln
├── WinToolbarMediaButtons.csproj
├── app.manifest
├── App.xaml / App.xaml.cs            — трей + автозапуск + CleanExit
├── MainWindow.xaml / MainWindow.xaml.cs
└── Services/
    ├── MediaKeyService.cs
    ├── VolumeService.cs
    ├── AutostartService.cs
    └── AppBarService.cs              — WndProc subclass для Z-порядка
```

## Ключевые технические детали

### csproj
```xml
<TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```

### Окно
```csharp
presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
presenter.IsAlwaysOnTop = true;
presenter.IsResizable = false;
AppWindow.IsShownInSwitchers = false;

// Фон — явный цвет тулбара вместо DesktopAcrylicBackdrop (решает белый XAML Island)
// Grid Background="#1c1c1c" в XAML; SystemBackdrop не используется

// Убираем DWM-обводку
DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);

// Убираем скруглённые углы Windows 11 (давали белый highlight)
DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);

// Обнуляем фоновую кисть оконного класса
SetClassLongPtr(hwnd, GCL_HBRBACKGROUND, 0);
```

### Позиционирование — внутри тулбара (текущее решение)

Окно — дочернее `Shell_TrayWnd`. Позиция в координатах client area тулбара:

```csharp
var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
int style = GetWindowLong(hwnd, GWL_STYLE);
SetWindowLong(hwnd, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
SetParent(hwnd, taskbarHwnd);

var s = GetDpiForWindow(hwnd) / 96.0;   // 1.5 при 150% DPI
int w = (int)(LogicalWidth  * s);
int h = (int)(LogicalHeight * s);       // физические пиксели
SetWindowPos(hwnd, HWND_TOP, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
```

DPI-aware: `LogicalHeight = 52` → при 150% DPI = 78 физических пикселей.
XAML-кнопки заданы в логических пикселях (52×52) — XAML сам применяет масштаб.

### UI — FontIcon (Segoe Fluent Icons)
Кнопки используют векторные глифы из системного шрифта Windows 11.

| Кнопка | Глиф |
|--------|------|
| Prev   | U+E892 |
| Play   | U+E768 |
| Pause  | U+E769 |
| Next   | U+E893 |
| Vol-   | U+E993 |
| Vol+   | U+E995 |
| Mute   | U+E74F |

Кнопки громкости — `RepeatButton` с `Delay="500"` и `Interval="33"` (удержание работает).

Все кнопки `Width="52" Height="52" Padding="0"` — одинаковый размер у `Button` и `RepeatButton`.

### UI — нативный вид кнопок

Переопределение ресурсов в `ResourceDictionary.ThemeDictionaries[Dark]`:

```xml
<SolidColorBrush x:Key="ButtonBackground"                   Color="Transparent"/>
<SolidColorBrush x:Key="ButtonBackgroundPointerOver"        Color="#22FFFFFF"/>
<SolidColorBrush x:Key="ButtonBackgroundPressed"            Color="#11FFFFFF"/>
<!-- Аналогично для RepeatButton* -->
```

`RequestedTheme="Dark"` на корневом Grid — белые иконки на тёмном фоне.

### UI — фон и разделитель

`Grid Background="#1c1c1c"` — приближение к цвету Windows 11 dark taskbar.

Вертикальный разделитель: `Border Width="1" Margin="10,10"` — 10px пространства с каждой стороны.

### Расчёт LogicalWidth

```
6 кнопок × 52px          = 312
6 пробелов × 2px          =  12  (Spacing="2" между всеми элементами)
Слот разделителя 10+1+10  =  21
Левый Padding StackPanel  =   4
─────────────────────────
LogicalWidth              = 349
```

### Мониторинг воспроизведения
`GlobalSystemMediaTransportControlsSessionManager` — отслеживает все медиа-сессии (браузер, Spotify, VLC и т.д.).
Подписка на `SessionsChanged` + `PlaybackInfoChanged` для каждой сессии.
Иконка Play/Pause меняется если хотя бы одна сессия в состоянии `Playing`.

### Трей (H.NotifyIcon.WinUI)
Меню:
- **Автозапуск: включить / ✓ выключить** — реестр `HKCU\...\Run`
- **Выход** → `CleanExit()` (dispose иконки + `Environment.Exit(0)`)

### ПКМ на окне
`MenuFlyout` с пунктом "Выход" через `RightTapped` на корневом Grid.

### Автозапуск
`AutostartService` читает/пишет реестр используя `Environment.ProcessPath` — работает из любого расположения.

### Z-порядок — итоговое решение: SetParent

**Проблема:** Windows 11 Shell использует закрытый Z-band механизм (`CGlobalRudeWindowManager`). При клике на Пуск окно с `HWND_TOPMOST` в зоне тулбара проваливается под него — реактивные подходы не успевают (8 вариантов исчерпаны, см. таблицу ниже).

**Решение:** `SetParent(hwnd, Shell_TrayWnd)` — окно становится дочерним тулбара. Z-band конфликт исчезает полностью: дочернее окно не участвует в Z-order соревновании с тулбаром.

```csharp
int style = GetWindowLong(hwnd, GWL_STYLE);
SetWindowLong(hwnd, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
SetParent(hwnd, taskbarHwnd);
```

**Ограничение:** дочернее окно клипируется до размеров client area родителя. Поэтому высота окна = `LogicalHeight * scale` (а не `taskbarClient.Bottom`) — подбирается так, чтобы совпасть с реальной высотой тулбара.

### Z-порядок (AppBarService)
Два механизма вместе:

**1. WndProc subclass** (`SetWindowSubclass` из comctl32):
- `WM_WINDOWPOSCHANGING` — если кто-то явно пытается опустить нас (hwndInsertAfter ≠ HWND_TOPMOST) — добавляем `SWP_NOZORDER`, блокируя изменение Z-порядка.
- `WM_WINDOWPOSCHANGED` — если нас опустили косвенно (taskbar поднял себя) — немедленно поднимаемся через `SetWindowPos(HWND_TOPMOST)`. Флаг `_reordering` защищает от рекурсии.

**2. DispatcherTimer 250мс** — резервный механизм, страховка.

### SHAppBarMessage и Z-порядок

Полный стандартный цикл AppBar:
1. `ABM_NEW` — регистрация, получаем callback message ID
2. `ABM_QUERYPOS` — запрашиваем у шелла скорректированный rect (шелл сдвигает нас выше taskbar)
3. `ABM_SETPOS` — подтверждаем позицию; шелл уменьшает work area и переводит нас в AppBar Z-band
4. `SetWindowPos` — физически перемещаем окно в rect из шага 3
5. При `ABN_POSCHANGED` (taskbar переместился) — повторить шаги 2–4
6. `ABM_REMOVE` — при выходе

`ABM_SETPOS` уменьшает work area на высоту панели (~52px логических). Это означает что максимизированные окна будут на 52px короче снизу — **принято как допустимый компромисс**.

### Почему НЕ SHAppBarMessage (исходная позиция — пересмотрена)
~~`SHAppBarMessage(ABM_NEW + ABM_SETPOS)` — регистрирует окно как Application Desktop Toolbar.
Даёт Z-band выше taskbar, но **изменяет work area** (рабочая область экрана уменьшается снизу, все окна поднимаются). Это неприемлемый побочный эффект.~~

После исчерпания всех HWND_TOPMOST подходов — `ABM_SETPOS` является единственным решением без DLL-инъекции.

### Почему НЕ DwmExtendFrameIntoClientArea
`DwmExtendFrameIntoClientArea(-1)` применяется к родительскому HWND.
XAML Island — это дочернее HWND (`Microsoft.UI.Content.DesktopChildSiteBridge`), которое рендерит свой фон поверх DWM-эффектов родителя. Любые DWM-трюки на родительском HWND не дают прозрачности — XAML Island рисует белое поверх.
**Решение:** `SystemBackdrop = new DesktopAcrylicBackdrop()` — встроенный в Windows App SDK механизм, который работает на уровне XAML compositor.

---

## Известные проблемы

### ✅ Решено: Z-порядок — уходит под тулбар при клике на Пуск

`SetParent(hwnd, Shell_TrayWnd)` — панель становится дочерним окном тулбара. Z-конфликт невозможен по архитектуре. Подробности — в разделе «Z-порядок — итоговое решение» выше.

### ✅ Решено: размер кнопок (DPI-aware)

Кнопки 52×52 логических пикселя в XAML. Окно: `LogicalHeight * GetScale()` физических пикселей. При 150% DPI = 78px — совпадает с высотой тулбара → квадратные кнопки без обрезки.

### ✅ Решено: белый фон окна

`Grid Background="#1c1c1c"` — явный цвет тулбара устраняет белый XAML Island без `DesktopAcrylicBackdrop`.
Дополнительно: `DWMWA_BORDER_COLOR = NONE`, `DWMWCP_DONOTROUND`, `GCL_HBRBACKGROUND = 0`.

### ✅ Решено: удержание кнопок громкости
`RepeatButton` с `Delay="500"` и `Interval="33"`.

### ✅ Решено: трей-иконка
`Assets/app.ico` подключён в csproj.

### ~~❌ Не решено: Z-порядок~~ → ✅ Решено через SetParent

Решение и история попыток — в разделе «Z-порядок — итоговое решение» выше.


### ❌ Не решено: иконка Play/Pause не меняется (Яндекс Браузер)

Яндекс Браузер не интегрирован с Windows SMTC — `GlobalSystemMediaTransportControlsSessionManager` его не видит.

**Выбранное решение: WASAPI `IAudioSessionManager2`**

#### Цепочка вызовов

```
IMMDeviceEnumerator
  └─ GetDefaultAudioEndpoint()            → IMMDevice (динамик по умолчанию)
       └─ Activate(IAudioSessionManager2) → IAudioSessionManager2
            └─ GetSessionEnumerator()     → IAudioSessionEnumerator
                 └─ [каждая сессия]       → IAudioSessionControl
                      └─ GetState()       → AudioSessionStateActive
```

`AudioSessionStateActive` = звук воспроизводится прямо сейчас.

#### Мониторинг изменений (без поллинга)

- `IAudioSessionManager2::RegisterSessionNotification` — новая сессия появилась
- `IAudioSessionEvents::OnStateChanged` — сессия перешла в Active/Inactive

Всё на колбэках.

#### Реализация

Новый файл `Services/WasapiMonitorService.cs`:
- COM-интерфейсы через `[ComImport]`
- Событие `PlaybackStateChanged(bool isPlaying)`
- `MainWindow` комбинирует SMTC и WASAPI через `||` — работает для всех источников

#### Сравнение подходов

| | SMTC (текущий) | WASAPI |
|---|---|---|
| Яндекс Браузер | ❌ | ✅ |
| Spotify, VLC | ✅ | ✅ |
| Название трека | ✅ | ❌ |
| Системные звуки | ❌ | ⚠️ мигнёт на миг |

#### Прочие варианты (отклонены)

**A. Оптимистичное обновление** — переключать иконку локально при клике. Не синхронизируется с внешними паузами.

**C. Парсинг заголовка окна браузера** — костыль, хрупко, привязано к конкретному браузеру.

---

### 💾 60 МБ оперативной памяти

WinUI 3 + .NET 10 + Windows App SDK — неизбежный базовый overhead фреймворка (~50–80 МБ).
Для Desktop-приложения с self-contained runtime это нормально.

`SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1)` после инициализации может снизить RSS на ~10–20 МБ — косметика.

---

## Портативность

`WindowsAppSDKSelfContained=true` — Windows App Runtime упакован внутри папки publish, пользователю ничего устанавливать не нужно.

## Сборка

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

Результат: папка `bin\Release\net10.0-windows...\win-x64\publish\` — скопировать целиком.
