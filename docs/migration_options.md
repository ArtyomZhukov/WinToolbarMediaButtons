# WinToolbarMediaButtons — варианты миграции с WinUI 3

## Текущая проблема

WinUI 3 + WinAppSDK не предназначены для работы в режиме WS_CHILD (дочернее окно Shell_TrayWnd).
Все наши краши (STATUS_STOWED_EXCEPTION, IAppWindow E_POINTER, XamlParseException) — прямое следствие этого несоответствия.
Размер publish-папки — ~170 МБ. Цель: < 5 МБ.

---

## Вариант 1: WinForms + system .NET (рекомендуемый)

**Размер EXE:** ~300 КБ  
**Зависимости:** .NET 10 Desktop Runtime (уже установлен с VS; ~55 МБ для чистых машин)

### Что меняется
- Удаляем WinUI 3 / WinAppSDK (NuGet-пакеты, XAML, `App.xaml`, `MainWindow.xaml`)
- Переписываем UI-слой: WinForms `Form` без рамки + `OnPaint` через GDI+
- `Services/` остаются **без изменений** (WASAPI, SMTC, MediaKey, Autostart — чистый P/Invoke)
- Tray icon — встроенный `System.Windows.Forms.NotifyIcon`

### WS_CHILD embedding
WinForms HWND — обычное Win32-окно. SetWindowLong + SetParent работают без ограничений.
Никаких внутренних COM-объектов (IAppWindow и т.п.) которые инвалидируются после reparent.

### Воспроизведение дизайна через GDI+
- Цветные полосы fill-bar → `Graphics.FillRectangle`
- Текст процента → `TextRenderer.DrawText`
- Segoe Fluent Icons (иконки кнопок) → `TextRenderer.DrawText` с `new Font("Segoe Fluent Icons", size)`
- Hover-эффект фона → `WM_MOUSEMOVE` + invalidate
- DPI → `GetDpiForWindow` P/Invoke (такой же как сейчас)

### Стабильность в тулбаре
✅ Лучше WinUI 3 — WinForms не имеет ASTA, XAML-input-routing и других механизмов,
   ломающихся в WS_CHILD контексте.

### Усилие: среднее
Переписать ~300 строк UI-логики (MainWindow.xaml + часть xaml.cs).
Services трогать не нужно.

---

## Вариант 2: C# NativeAOT + чистый Win32

**Размер EXE:** ~3–7 МБ  
**Зависимости:** отсутствуют (полностью автономный)

### Что меняется
- Убираем все фреймворки
- Собственный message loop через `GetMessage` / `TranslateMessage` / `DispatchMessage`
- Отрисовка: WM_PAINT + GDI (`BeginPaint`, `FillRect`, `TextOut`)
- NativeAOT компилирует C# в нативный .exe без CLR
- WinRT-интероп (SMTC) ограничен в NativeAOT — нужно заменить на raw COM

### WS_CHILD embedding
✅ Нативный Win32 — никаких ограничений. Самый совместимый вариант.

### Стабильность в тулбаре
✅ Максимальная — работаем напрямую с Win32 без каких-либо прослоек.

### Усилие: высокое
Полная переработка архитектуры. SMTC нужно реализовать через raw COM vtable.

---

## Вариант 3: C++ (Win32 API)

**Размер EXE:** < 1 МБ  
**Зависимости:** отсутствуют

Полный переписывание на C++. Другой язык.  
Усилие: очень высокое. Оправдано только если нужен абсолютный минимум.

---

## Почему WinUI 3 несовместим с WS_CHILD

| Проблема | Причина | Наш краш |
|---|---|---|
| IAppWindow.Show() / Resize() | COM-объект инвалидируется после SetParent | E_POINTER → CoreMessagingXP crash |
| WinUI Slider / PointerCapture | Input routing ломается в WS_CHILD | STATUS_STOWED_EXCEPTION |
| AppWindow.TitleBar.ExtendsContentIntoTitleBar | Применяется лениво при первом WM_SHOWWINDOW как WS_CHILD | XAML noexcept crash |
| SetWindowPos(HWND_TOPMOST) | HWND_TOPMOST невалиден для WS_CHILD | XAML input crash |
| XamlParseException при publish | WinToolbarMediaButtons.pri не копируется publish-таргетом | XAML parsing failed |

---

## Итог

Для личного инструмента на машине с VS → **Вариант 1 (WinForms)**.  
Для полностью портативного (~5 МБ, без зависимостей) → **Вариант 2 (NativeAOT)**.  
WinUI 3 не подходит для WS_CHILD taskbar embedding — он дал нам 6+ различных крашей именно из-за этого.
