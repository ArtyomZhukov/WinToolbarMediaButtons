# Попытки отрисовки глифов Segoe Fluent Icons

## Контекст

Тулбар — WS_CHILD окно внутри Shell_TrayWnd, рендеринг через `Windows.UI.Composition`
(`DesktopWindowTarget` → `ContainerVisual` → `SpriteVisual`). GDI-контент под DCOMP-слоем
невидим. Кнопки, hover-состояния, слайдер — всё работает. Нужно добавить иконки
(Prev/Play/Pause/Next/Speaker/Mute) из шрифта Segoe Fluent Icons на `SpriteVisual` через
`CompositionSurfaceBrush`.

Система: Windows 11 Pro 10.0.26200. Приложение **unpackaged** (без MSIX).

---

## Корневая причина (установлена после анализа)

Все попытки, использующие `ICompositorInterop::CreateGraphicsDevice`, передавали в него
**ID3D11Device**. Это неверно. Официальный путь требует **ID2D1Device**:

```
D3D11Device (+ BGRA_SUPPORT)
  → QI для IDXGIDevice
    → D2D1Factory1::CreateDevice(dxgiDevice)
      → ID2D1Device  ← передаётся в CreateGraphicsDevice
        → CompositionGraphicsDevice с поддержкой ICompositionDrawingSurfaceInterop
```

Когда передаётся D3D11Device вместо D2D1Device, `CompositionGraphicsDevice` создаётся
в "заглушечном" режиме — объект валиден, `CreateDrawingSurface` возвращает ненулевой
`CompositionDrawingSurface`, но `ICompositionDrawingSurfaceInterop` QI возвращает
E_NOINTERFACE, потому что нет D2D-контекста для `BeginDraw`.

Источник: официальная документация MS (C++ пример), подтверждён анализом.

---

## Попытки

### Попытка 1 — прямой `ICompositionDrawingSurfaceInterop` через vtable

**Подход:** GDI+ → D2D1 через `ICompositionDrawingSurfaceInterop::BeginDraw`. Использовался
raw vtable dispatch без корректных `[ComImport]` интерфейсов.

**Результат:** `Specified cast is not valid` / `SurfInterop QI 0x80004002` — E_NOINTERFACE
на этапе QI поверхности.

**Лог:**
```
TW: LoadGlyphs: Specified cast is not valid.
TW: LoadGlyphs: System.Exception: SurfInterop QI 0x80004002
```

---

### Попытка 2 — `ICompositorInterop::CreateGraphicsDevice(null)` + vtable dispatch

**Подход:** `_compositor.As<ICompositorInterop>()` → `CreateGraphicsDevice(IntPtr.Zero)` →
`CompositionGraphicsDevice` → `CreateDrawingSurface` → QI для `ICompositionDrawingSurfaceInterop`.

**Результат:** Graphics device и drawing surface созданы (hr=0, ptr ненулевой, RuntimeClassName
корректен), но QI для обоих вариантов интерфейса провалился:
```
QI IUnknown:     0x00000000   ← объект валиден
QI IInspectable: 0x00000000
QI InteropV1:    0x80004002   ← E_NOINTERFACE
QI InteropV2:    0x80004002   ← E_NOINTERFACE
```

**Причина:** Передан null вместо D2D1Device → surface без D2D-бэкенда.

---

### Попытка 3 — DXGI SwapChain + `CreateCompositionSurfaceForSwapChain`

**Подход:** `D3D11CreateDevice` → `CreateDXGIFactory2` (независимая фабрика) →
`IDXGIFactory2::CreateSwapChainForComposition` → `ICompositorInterop::CreateCompositionSurfaceForSwapChain`.

**Результат:** `CreateSwapChainForComposition` возвращает **hr=0x0 (S_OK), sc=0x0 (null)**.

**Причина:** Независимая DXGI-фабрика (из `CreateDXGIFactory2`) несовместима с D3D11
устройством для composition swap chain. Нужна фабрика, полученная через
`d3dDevice → IDXGIDevice → GetAdapter → GetParent(IDXGIFactory2)`.

**Лог:**
```
TW: CreateSwapChainForComposition hr=0x00000000 sc=0x0
```

---

### Попытка 4 — `CreateGraphicsDevice(d3dDevice)`, Flags=0

**Подход:** `D3D11CreateDevice(HARDWARE, Flags=0)` → передать D3D11 `IntPtr` в
`CreateGraphicsDevice` → `CreateDrawingSurface` → `surf.As<ICompositionDrawingSurfaceInterop>()`.

**Результат:** D3D11 ok, CGD ok, но QI E_NOINTERFACE.

**Причина:** Передан D3D11Device вместо D2D1Device (см. корневую причину выше).

**Лог:**
```
TW: D3D11 ok
TW: CGD ok
TW: LoadGlyphs: System.InvalidCastException: Specified cast is not valid.
```

---

### Попытка 5 — `CreateGraphicsDevice(d3dDevice)`, Flags=0x20 (BGRA_SUPPORT)

**Подход:** Та же, что 4, но с флагом `D3D11_CREATE_DEVICE_BGRA_SUPPORT`.

**Результат:** Идентичен попытке 4. Флаг сам по себе не помогает, потому что
корень проблемы — не в D3D11 устройстве, а в отсутствии D2D1Device.

**Лог:**
```
TW: D3D11 ok
TW: CGD ok
TW: LoadGlyphs: System.InvalidCastException: Specified cast is not valid.
```

---

### Попытка 6 — `LoadedImageSurface.StartLoadFromStream`

**Подход:** GDI+ → PNG → `InMemoryRandomAccessStream` → `LoadedImageSurface.StartLoadFromStream`.
Обходит D3D/DXGI полностью.

**Результат:** Не компилируется. `LoadedImageSurface` отсутствует в
`microsoft.windows.sdk.net.ref 10.0.17763.57` и `10.0.22621.57` — класса нет ни в SDK,
ни в системных WinMD файлах.

---

## ✅ Итог — Вариант 3 (SwapChain) реализован и работает

### Ключевые ошибки которые были в ходе реализации

1. `DXGI_ALPHA_MODE_PREMULTIPLIED = 1`, а не 2 (2 = STRAIGHT) — неверное значение вызывало `DXGI_ERROR_INVALID_CALL`.
2. `D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW = 3` (не 1) — требуется для render-target bitmap из DXGI surface.

### Итоговая цепочка (C# unsafe vtable, всё unpackaged)

```
D3D11CreateDevice(BGRA_SUPPORT)
  → IDXGIDevice (QI)
     → D2D1Factory1::CreateDevice   [vtbl[17]]  → ID2D1Device
        → ID2D1Device::CreateDeviceContext [vtbl[4]] → ID2D1DeviceContext (хранить живым!)
     → IDXGIDevice::GetAdapter       [vtbl[7]]  → IDXGIAdapter
        → IDXGIObject::GetParent     [vtbl[6]]  → IDXGIFactory2

Per glyph / per surface:
  IDXGIFactory2::CreateSwapChainForComposition [vtbl[24]]
    (BGRA, FLIP_SEQUENTIAL=3, ALPHA_PREMULTIPLIED=1, BufferCount=2)
  IDXGISwapChain1::GetBuffer(0) [vtbl[9]] → IDXGISurface
  ID2D1DeviceContext::CreateBitmapFromDxgiSurface [vtbl[62]]
    (bitmapOptions = TARGET|CANNOT_DRAW = 3)
  SetTarget [vtbl[74]] → BeginDraw [vtbl[48]] → Clear [vtbl[47]]
  → CreateBitmap [vtbl[4]] → DrawBitmap [vtbl[26]]
  → EndDraw [vtbl[49]] → SetTarget(null) [vtbl[74]]
  IDXGISwapChain1::Present [vtbl[8]]
  ICompositorInterop::CreateCompositionSurfaceForSwapChain → CompositionSurfaceBrush
```

Swap chain pointers нужно держать живыми (List<IntPtr> _swapChains).  
ID2D1DeviceContext нужно держать живым для динамических обновлений.

---

## Правильный путь (Вариант 1) — не реализован (Вариант 3 оказался проще)

Официальная цепочка для `ICompositionDrawingSurfaceInterop` в unpackaged Win32:

```
1. D3D11CreateDevice(HARDWARE/WARP, D3D11_CREATE_DEVICE_BGRA_SUPPORT)  → ID3D11Device
2. Marshal.QueryInterface(d3d, IID_IDXGIDevice)                         → IDXGIDevice
3. D2D1CreateFactory(SINGLE_THREADED, IID_ID2D1Factory1)                → ID2D1Factory1
4. ID2D1Factory1::CreateDevice(dxgiDevice)                              → ID2D1Device  ← ключ
5. ICompositorInterop::CreateGraphicsDevice(d2dDevice)                  → CompositionGraphicsDevice
6. cgd.CreateDrawingSurface(size, B8G8R8A8, Premultiplied)              → CompositionDrawingSurface
7. surf.As<ICompositionDrawingSurfaceInterop>()                         → должен работать
8. interop.BeginDraw(→ ID2D1DeviceContext)                              → рисуем
9. DrawGlyphRun / CreateBitmap + DrawBitmap
10. interop.EndDraw()
```

Работает в unpackaged apps — никаких пакетных ограничений нет. Подтверждено официальной
документацией и C++ примерами Microsoft.

---

## Альтернативные варианты

### Вариант 2 — Win2D (`Win2D.uwp` NuGet)

`Win2D.uwp` (v1.28.3) работает в unpackaged Win32 — есть официальные семплы Microsoft
(WinForms AcrylicEffect). Требует:
- NuGet: `Win2D.uwp`
- NuGet: `Microsoft.VCRTForwarders`
- Записи activatable classes в `app.manifest`
- Копирование `Microsoft.Graphics.Canvas.dll` в output

Использование:
```csharp
var canvasDev = new CanvasDevice();
var cgd = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, canvasDev);
var surf = cgd.CreateDrawingSurface(new Size(px, px), ...);
using var ds = CanvasComposition.CreateDrawingSession(surf);
ds.DrawText(glyph.ToString(), 0, 0, Colors.White,
    new CanvasTextFormat { FontFamily = "Segoe Fluent Icons", FontSize = px * 0.6f });
```

**Плюсы:** рисование текста — одна строка, всё interop внутри Win2D.
**Минусы:** NuGet + native DLL зависимость, сложнее для single-file publish.

### Вариант 3 — SwapChain через правильную DXGI-фабрику

Получить фабрику не через `CreateDXGIFactory2`, а через
`d3d → IDXGIDevice → GetAdapter → GetParent(IDXGIFactory2)`. Тогда
`CreateSwapChainForComposition` вернёт ненулевой swap chain. Рисовать через D3D/D2D
на swap chain buffer, потом `Present`. Есть рабочий C# пример (WPF ScreenCapture на GitHub).

**Плюсы:** нет зависимостей.
**Минусы:** избыточно для статичных иконок, сложнее.

### Вариант 4 — Растровые изображения как embedded resources

Прошить PNG-файлы глифов как embedded resources в сборку. В runtime — загрузить байты
и передать в `LoadedImageSurface` (если найдём в нужном SDK) или Win2D `CanvasBitmap`.

---

## Источники

- [Composition native interoperation — официальный C++ пример MS](https://learn.microsoft.com/en-us/windows/uwp/composition/composition-native-interop)
- [ICompositorInterop::CreateGraphicsDevice](https://learn.microsoft.com/en-us/windows/win32/api/windows.ui.composition.interop/nf-windows-ui-composition-interop-icompositorinterop-creategraphicsdevice)
- [Windows.UI.Composition-Win32-Samples / WinForms AcrylicEffect](https://github.com/Microsoft/Windows.UI.Composition-Win32-Samples/tree/master/dotnet/WinForms/AcrylicEffect)
- [CompositionHelper.cs — C# CreateCompositionSurfaceForSwapChain](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/dotnet/WPF/ScreenCapture/Composition.WindowsRuntimeHelpers/CompositionHelper.cs)
- [InteropCompositor and CoreDispatcher — ADeltaX Blog](https://blog.adeltax.com/interopcompositor-and-coredispatcher/)
