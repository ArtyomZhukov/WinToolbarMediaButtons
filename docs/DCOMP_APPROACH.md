# DCOMP + SetParent: рабочая архитектура тулбара

## Суть решения

Windows 11 taskbar рендерится через DirectComposition (DCOMP). Любой контент WS_CHILD
окна, нарисованный через GDI, оказывается ниже DCOMP-слоя таскбара — невидим.

**Решение**: использовать `Windows.UI.Composition` API напрямую. DCOMP-визуалы
(`DesktopWindowTarget`) рендерятся поверх GDI-слоя и поверх родительского DCOMP-дерева,
потому что `DesktopWindowTarget` создаёт собственный `IDCompositionTarget` на HWND.

---

## Порядок инициализации (критично!)

DCOMP-дерево **нужно создавать до** реперентинга:

```
1. CreateWindowEx(WS_POPUP, ...)           — top-level окно
2. Compositor + CreateDesktopWindowTarget  — DCOMP-дерево на top-level HWND
3. ContainerVisual + BuildUI               — строим визуалы
4. SetParent(hwnd, Shell_TrayWnd)          — переносим в Shell Z-band
5. SetWindowLong: WS_POPUP → WS_CHILD     — меняем стиль
6. SetWindowPos(0, 0, w, h)               — позиционируем внутри таскбара
```

Если создать окно сразу как `WS_CHILD` и потом вешать DCOMP — ничего не видно.
(Гипотеза: `DesktopWindowTarget` на pre-existing WS_CHILD не получает корректный
composition surface от Shell'а. Точная причина не исследована.)

Это в точности повторяет то, что делал WinUI 3 вариант:
WinUI создаёт top-level XAML-окно, затем вызывает `SetParent → Shell_TrayWnd`.

---

## Z-order: почему Пуск не скрывает

После `SetParent(hwnd, Shell_TrayWnd)` наше окно становится WS_CHILD таскбара.
Таскбар живёт в Shell Z-band (ZBID_TASKBAR), который выше ZBID_IMMERSIVE (Start menu).
Дочернее окно наследует Z-band родителя → Start menu нас больше не перекрывает.

```
ZBID_TASKBAR     ← наш WS_CHILD здесь (Shell_TrayWnd → наш HWND)
ZBID_IMMERSIVE   ← Start menu, Quick Settings
ZBID_APPBAR      ← AppBar
ZBID_DEFAULT     ← HWND_TOPMOST окна — всё предыдущее решение жило здесь
```

---

## GDI vs DCOMP в WS_CHILD таскбара

| Метод         | Результат                                    |
|---------------|----------------------------------------------|
| GDI WS_CHILD  | WM_PAINT срабатывает, но невидимо под DCOMP  |
| GDI HWND_TOPMOST | Видно, но Start menu перекрывает          |
| DCOMP WS_CHILD (прямо) | Невидимо — см. выше             |
| **DCOMP → SetParent** | **Работает: видимо + Start не скрывает** |

---

## Cursor

При регистрации оконного класса (WNDCLASSEX) обязательно задавать `hCursor`:
```csharp
hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW) // 32512
```
Без этого Windows показывает курсор загрузки при наведении на окно.

---

## Мышиный ввод

WS_CHILD окно таскбара получает WM_LBUTTONDOWN, WM_MOUSEMOVE, WM_LBUTTONUP
в обычном порядке. TrackMouseEvent нужен для WM_MOUSELEAVE.
SetCapture при нажатии — для корректной обработки движения мыши за пределы окна.

---

## Требования к сборке

```xml
<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
```
Суффикс `windows10.0.17763.0` обязателен — даёт доступ к `Windows.UI.Composition`,
`Windows.UI.Composition.Desktop`, `WinRT` (CsWinRT).

CreateDispatcherQueueController (CoreMessaging.dll) нужен перед созданием Compositor.
