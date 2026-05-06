# Варианты встраивания тулбара внутрь Shell_TrayWnd (WS_CHILD)

Цель: разместить окно тулбара как дочернее окно (`WS_CHILD`) внутри `Shell_TrayWnd`, чтобы оно было частью панели задач и не скрывалось кнопкой Пуск.

---

## Вариант 1: Raw Win32 HWND (CreateWindowEx)

**Суть:** Создать нативное Win32-окно с флагом `WS_CHILD` и передать `Shell_TrayWnd` как `hWndParent`. Рендеринг через `Graphics.FromHwnd` (GDI+).

**Как это работает у WinUI 3:** именно так — создаёт `WS_CHILD` HWND и прикрепляет к `Shell_TrayWnd`.

**Реализация:**
```csharp
IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);

IntPtr hwnd = CreateWindowEx(
    0,
    wndClassName,       // зарегистрированный WNDCLASS
    null,
    WS_CHILD | WS_VISIBLE,
    x, y, width, height,
    taskbarHwnd,        // parent = Shell_TrayWnd
    IntPtr.Zero,
    hInstance,
    IntPtr.Zero
);

// Рендеринг:
using var g = Graphics.FromHwnd(hwnd);
// ... рисуем кнопки, слайдер
```

**Плюсы:**
- Самый надёжный подход — именно так работает WinUI 3
- Z-order решается автоматически (дочернее окно не может скрыться за родителем)
- Нет проблемы с Rude Window Manager
- Полный контроль над WndProc

**Минусы:**
- Нужно регистрировать WNDCLASS вручную (P/Invoke `RegisterClassEx`)
- WinForms-фреймворк не используется для этого окна, вся логика на чистом Win32
- Мышь, клавиатура — всё через WndProc вручную
- Больше boilerplate-кода

**Сложность:** Высокая

---

## Вариант 2: CreateParams.Parent в WinForms

**Суть:** Переопределить `CreateParams` в WinForms-форме, задав `cp.Parent = taskbarHwnd` и добавив `WS_CHILD` до создания HWND. Окно рождается уже как дочернее.

**Реализация:**
```csharp
private IntPtr _taskbarHwnd;

protected override CreateParams CreateParams
{
    get
    {
        var cp = base.CreateParams;
        _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (_taskbarHwnd != IntPtr.Zero)
        {
            cp.Parent = _taskbarHwnd;
            cp.Style |= WS_CHILD;
            cp.Style &= ~WS_POPUP;
            cp.ExStyle &= ~(WS_EX_APPWINDOW | WS_EX_TOOLWINDOW);
        }
        return cp;
    }
}
```

**Плюсы:**
- Минимум изменений в существующем коде
- WinForms-инфраструктура (OnPaint, мышь) работает как обычно
- Быстро проверить

**Минусы:**
- WinForms отправляет `WM_SHOWWINDOW(0)` в процессе `SetParent` — окно может скрыться
- Поведение `AutoScaleMode`, `SetBoundsCore` может конфликтовать
- Нестабильно: WinForms не рассчитан на `WS_CHILD` у `Shell_TrayWnd`
- Требует подавления `WM_SHOWWINDOW` в WndProc

**Сложность:** Средняя (но поведение непредсказуемо)

---

## Вариант 3: SetParent + подавление WM_SHOWWINDOW

**Суть:** Создать обычное WinForms-окно, затем вызвать `SetParent(Handle, taskbarHwnd)` в `OnShown`. Переопределить `WndProc` для перехвата `WM_SHOWWINDOW(wParam=0)` — не давать WinForms скрыть окно.

**Реализация:**
```csharp
protected override void OnShown(EventArgs e)
{
    base.OnShown(e);
    var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
    SetParent(Handle, taskbarHwnd);
    // Добавить WS_CHILD, убрать WS_POPUP
    int style = GetWindowLong(Handle, GWL_STYLE);
    style = (style | WS_CHILD) & ~WS_POPUP;
    SetWindowLong(Handle, GWL_STYLE, style);
    SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

protected override void WndProc(ref Message m)
{
    const int WM_SHOWWINDOW = 0x0018;
    if (m.Msg == WM_SHOWWINDOW && m.WParam == IntPtr.Zero)
        return; // подавляем скрытие
    base.WndProc(ref m);
}
```

**Плюсы:**
- Не нужно регистрировать WNDCLASS
- WinForms-рендеринг (OnPaint) работает
- Теоретически достижимо с минимальными изменениями

**Минусы:**
- **Подтверждено не работает**: в WinForms `WM_SHOWWINDOW(0)` — не единственная причина скрытия. Окно остаётся невидимым даже с подавлением (проверено дважды в текущей кодовой базе)
- WinForms устанавливает `Visible = false` внутренне, не только через WM_SHOWWINDOW
- Требует дополнительного исследования и хаков

**Сложность:** Средняя (но с известными проблемами)

---

## Вариант 4: Deskband COM (IDeskBand)

**Суть:** Реализовать COM-интерфейс `IDeskBand` / `IDeskBand2`, зарегистрировать как Shell Band, встроить в панель задач через COM-регистрацию.

**Реализация:** Реализация интерфейсов `IOleWindow`, `IDockingWindow`, `IDeskBand`, `IObjectWithSite`, `IPersist`, `IPersistStream` + регистрация в реестре как Shell Extension.

**Плюсы:**
- Официальный механизм встраивания в панель задач (до Windows 10)
- Поддерживается документацией Microsoft (устаревшей)

**Минусы:**
- **Дескбанды полностью удалены в Windows 11** (удалены из Explorer)
- Огромный объём COM-кода
- Требует регистрации в реестре (`HKLM\SOFTWARE\Microsoft\Internet Explorer\Toolbar`)
- Требует подписанной DLL в некоторых конфигурациях

**Сложность:** Очень высокая, **не применимо для Windows 11**

---

## Итоговая таблица

| Вариант | Надёжность | Сложность | Применимость |
|---|---|---|---|
| 1. Raw Win32 CreateWindowEx | ★★★★★ | Высокая | Windows 10/11 ✓ |
| 2. CreateParams.Parent | ★★★ | Средняя | Неопределённо |
| 3. SetParent + WM_SHOWWINDOW | ★★ | Средняя | Не работает (проверено) |
| 4. Deskband COM | ★ | Очень высокая | Windows 11 ✗ |

## Рекомендация

**Вариант 1 (Raw Win32)** — единственный надёжный путь. Именно так работает WinUI 3 под капотом. Текущий рендеринг через GDI+ (`OnPaint`) переносится почти без изменений через `Graphics.FromHwnd`. Основной объём работы — написать Win32 message loop для одного дочернего окна.

Альтернатива: **Вариант 2** можно попробовать быстро (~30 минут), но высок риск что поведение будет нестабильным.
