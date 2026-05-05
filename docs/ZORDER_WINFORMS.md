# Z-order: WinForms overlay vs Windows 11 Start menu

## Проблема

WinForms overlay (`HWND_TOPMOST`, без рамки, поверх `Shell_TrayWnd`) скрывается при открытии
меню Пуск в Windows 11. При клике на любое другое окно — возвращается.

---

## Архитектура текущего решения

```
MainForm
  FormBorderStyle = None
  ShowInTaskbar   = false
  Positioned at Shell_TrayWnd screen coords
  SetWindowPos(HWND_TOPMOST, tb.Left, tb.Top, w, h)

Защитные механизмы:
  SetProp("NonRudeHWND", Handle)           — просим RWM нас не трогать
  WM_WINDOWPOSCHANGING intercept           — блокируем понижение z-order + SWP_HIDEWINDOW
  WM_SHOWWINDOW(0) suppression             — подавляем явный hide
  WinEventHook(EVENT_SYSTEM_FOREGROUND)    — поднимаемся при смене foreground
  Timer 100ms → SetWindowPos(HWND_TOPMOST) — страховочный повтор
  ABM_NEW (без ABM_SETPOS)                 — регистрация как AppBar
```

---

## Диагностика: что именно Shell присылает

Из лога (снят через `WM_WINDOWPOSCHANGING` дамп):

```
flags = 0x00000097
     = SWP_NOSIZE(0x01) | SWP_NOMOVE(0x02) | SWP_NOZORDER(0x04)
       | SWP_NOACTIVATE(0x10) | SWP_HIDEWINDOW(0x80)
```

Shell вызывает `SetWindowPos(hwnd, ..., SWP_HIDEWINDOW | SWP_NOZORDER)` — явный hide.

---

## Что пробовали (хронология)

| # | Метод | Результат |
|---|-------|-----------|
| 1 | `HWND_TOPMOST` + ничего | Пуск скрывает, не возвращается |
| 2 | `WinEventHook(EVENT_SYSTEM_FOREGROUND)` → re-top | Возвращается после **закрытия** Пуска |
| 3 | Timer 100ms → `SetWindowPos(HWND_TOPMOST)` | Аналогично #2, Пуск всё равно скрывает |
| 4 | `NonRudeHWND = (IntPtr)1` | Не помогло |
| 5 | `WM_WINDOWPOSCHANGING`: блок `HWND_BOTTOM/NOTOPMOST` | Не помогло против `SWP_HIDEWINDOW` |
| 6 | `WM_WINDOWPOSCHANGING`: стриппинг `SWP_HIDEWINDOW` | Не помогло (Shell присылает отдельно?) |
| 7 | `WM_SHOWWINDOW(0)` suppress + исправить `NonRudeHWND = Handle` | **Частично:** тулза возвращается при клике на другое окно (раньше не возвращалась без смены foreground) |
| 8 | `ABM_NEW` (без `ABM_SETPOS`) | Без изменений |

**Прогресс после #7:** WS_VISIBLE похоже остаётся `true` (раз SetWindowPos без SWP_SHOWWINDOW
восстанавливает видимость). Значит окно не прячется — оно перекрывается.

---

## Текущая гипотеза

**Не visibility, а Z-order.**

Windows 11 Start menu — immersive/XAML-окно в Shell Z-band. Этот Z-band:

```
[Normal windows]  ← HWND_TOPMOST окна живут здесь
[Shell Z-band]    ← Taskbar, Start menu, Quick Settings, Action Center
[Always on top]   ← Taskbar поверх своего же Z-band — особый случай
```

Наш `HWND_TOPMOST` оверлей живёт в Normal windows Z-band. Start menu при открытии
занимает весь экран в Shell Z-band и физически перекрывает нас. Окно не hidden — оно
просто ниже по Z-order.

Почему **возвращается** при клике на другое окно: WinEventHook → `SetWindowPos(HWND_TOPMOST)`
→ поднимаемся в верх Normal Z-band → оказываемся выше закрытого Start-а.

---

## Почему ABM_NEW не помог

`ABM_NEW` только **регистрирует** окно как AppBar и даёт callback message ID.
Перемещение в Shell Z-band происходит только при `ABM_SETPOS`, который
подтверждает зарезервированную позицию и уменьшает work area.

`ABM_NEW` без `ABM_SETPOS` = окно остаётся в Normal Z-band.

---

## Почему SetParent не работает (WinForms + GDI)

`SetParent(hwnd, Shell_TrayWnd)` работало в старом WinUI-варианте.
Для WinForms + GDI — нет: taskbar в Windows 11 рендерится через
DirectComposition (DCOMP). GDI-рисование в WS_CHILD выполняется, но
контент невидим под DCOMP-слоем. `WM_PAINT` срабатывает, `Graphics.FromHdc` рисует,
но на экране ничего нет.

---

## Варианты решения (не испробованные)

### Вариант A: ABM_SETPOS (принять уменьшение work area)

Полный AppBar-цикл: `ABM_NEW → ABM_QUERYPOS → ABM_SETPOS → SetWindowPos`.
После `ABM_SETPOS` окно перемещается в Shell Z-band — Start его не перекроет.

**Побочный эффект:** work area уменьшается на ~48px снизу. Максимизированные окна
будут короче. На одном мониторе — заметно.

**Обходной путь:** сделать ширину тулзы 0 или передать нулевой rect в `ABM_SETPOS`?
Нужно проверить что скажет Shell.

### Вариант B: RegisterShellHookWindow + HSHELL_RUDEAPPACTIVATED

`RegisterShellHookWindow` + обработка `HSHELL_RUDEAPPACTIVATED (0x8004)` — Shell сигналит
до того как поднимет rudeWindow. Можно успеть вскочить выше.

Сложность: это racing condition, не гарантия.

### Вариант C: WS_EX_LAYERED + Transparent window trick

Окно с `WS_EX_LAYERED | WS_EX_TRANSPARENT` не участвует в обычном hit-testing,
Shell может его не трогать. Но тогда нельзя получать mouse-события напрямую —
нужен отдельный input-capture HWND.

### Вариант D: DLL-инъекция в Explorer.exe

Inject DLL → hook `CGlobalRudeWindowManager::SetWindowBand` или аналог.
Нереалистично для личного инструмента.

### Вариант E: Диагностика — добавить лог WS_VISIBLE

Перед тем как идти дальше: добавить проверку в `WinEventHook`-handler:
```csharp
void OnForegroundChanged(...)
{
    var style = GetWindowLong(Handle, GWL_STYLE);
    var visible = (style & WS_VISIBLE) != 0;
    // лог: visible=true/false при открытом Пуске
    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
```

Подтвердит/опровергнет гипотезу о Z-order vs скрытии.

---

## Рекомендация

Попробовать **Вариант A** с нулевым или минимальным rect в `ABM_SETPOS` — 
проверить можно ли войти в Shell Z-band не уменьшая work area.
Если work area всё равно меняется — принять это как компромисс.
