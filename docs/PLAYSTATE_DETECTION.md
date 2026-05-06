# Play State Detection — исследование и варианты

## Проблема

Тулбар должен показывать состояние воспроизведения (play/pause) и, в перспективе, название трека. Основная сложность — мьют в плеере: пик-метр видит тишину и не может отличить «замьютил» от «поставил на паузу».

## Текущее решение (fallback)

**Endpoint peak meter** (`IAudioMeterInformation::GetPeak`):
- Пик > 0 → показываем play
- Пик = 0 в течение 6 тиков (3 сек) → показываем pause

**Ограничение:** мьют на уровне плеера (не системный) → пик = 0 → тулбар ошибочно показывает паузу.

---

## SMTC (Windows.Media.Control)

`GlobalSystemMediaTransportControlsSessionManager` — публичный WinRT API для чтения состояния медиасессий любых приложений.

### Что реализовано

`native/src/smtc.zig` — полная реализация через raw COM vtable:

- `RoGetActivationFactory` → `IGlobalSystemMediaTransportControlsSessionManagerStatics` (IID `{2050C4EE-11A0-57DE-AED7-C97C70338245}`, верифицирован через `GetIids` и `Windows.Media.winmd`)
- `RequestAsync` → `IAsyncOperation<SessionManager>` → poll `GetResults` [vtable[8]]
- `GetSessions` [vtable[6]] → `IVectorView` → перебор сессий
- `GetPlaybackInfo` [vtable[9]] → `get_PlaybackStatus` [vtable[11]] → 4=Playing

### Проблема с Яндекс Браузером

Яндекс Браузер **не регистрирует SMTC-сессии**. `GetSessions()` возвращает `hr=0, ptr=null`. Подтверждено:
- C# тест через .NET WinRT projection: `Sessions count: 0`
- Windows volume overlay при нажатии кнопки громкости: показывает **только ползунок**, без обложки и кнопок управления → браузер не публикует медиасессию в SMTC

Для Spotify, VLC 3+, Windows Media Player, Groove Music — SMTC работает и используется автоматически (см. `toolbar_window.zig`, `WM_TIMER`).

### Vtable индексы (верифицированы через Windows.Media.winmd)

`IGlobalSystemMediaTransportControlsSessionManager` (IID `{CACE8EAC-E86E-504A-AB31-5FF8FF1BCE49}`):
| Индекс | Метод |
|--------|-------|
| [6] | GetSessions |
| [7] | get_CurrentSession |
| [8] | add_CurrentSessionChanged |
| [9] | remove_CurrentSessionChanged |

`IGlobalSystemMediaTransportControlsSession`:
| Индекс | Метод |
|--------|-------|
| [6] | get_SourceAppUserModelId |
| [7] | TryGetMediaPropertiesAsync |
| [8] | GetTimelineProperties |
| [9] | GetPlaybackInfo |

`IGlobalSystemMediaTransportControlsSessionPlaybackInfo`:
| Индекс | Метод |
|--------|-------|
| [6] | GetControls |
| [7] | get_IsShuffleActive |
| [8] | get_PlaybackRate |
| [9] | get_PlaybackType |
| [10] | get_AutoRepeatMode |
| [11] | get_PlaybackStatus |

`MediaPlaybackStatus`: 0=Closed, 1=Opened, 2=Changing, 3=Stopped, **4=Playing**, 5=Paused

---

## Варианты решения для Яндекс Браузера

### 1. Заголовок окна браузера
`GetWindowText` → парсинг `"Название видео - YouTube"`

- ✅ Название трека
- ❌ Нет статуса play/pause
- ✅ Сложность: минимальная

### 2. UI Automation
`IUIAutomation` читает accessibility-дерево браузера. YouTube expose кнопку play/pause с `aria-label="Pause"` / `"Play"`.

- ✅ Точный статус play/pause
- ✅ Название трека (через `<title>`)
- ✅ Мьют (через aria-label кнопки mute)
- ❌ Хрупко: зависит от DOM структуры YouTube
- ❌ Сложность: высокая (COM IUIAutomation)

### 3. Браузерное расширение (рекомендуется)
Расширение слушает `video.onplay/onpause/onvolumechange`, пишет состояние в файл:

```json
{"playing": true, "muted": false, "title": "Song Name"}
```

Тулбар читает файл раз в 500 мс.

- ✅ Точный play/pause/mute
- ✅ Название трека
- ✅ Надёжно, работает для любого плеера в браузере
- ❌ Требует установки расширения

**Установка расширения:**
- Режим разработчика: `browser://extensions` → "Загрузить распакованное" → показывает баннер при запуске
- Через реестр (без предупреждений): `HKLM\SOFTWARE\Policies\Yandex\YandexBrowser\ExtensionInstallForcelist`
