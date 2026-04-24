# WinToolbarMediaButtons

Портативная панель медиа-кнопок для Windows 11, прилипающая над тулбаром.

## Кнопки

⏮ Prev · ⏯ Play/Pause · ⏭ Next · 🔉 Vol− · 🔊 Vol+ · 🔇 Mute

## Использование

Никакого инсталлятора. Просто запусти `WinToolbarMediaButtons.exe` — панель появится над тулбаром.

Управление через иконку в трее (правая кнопка):
- **Автозапуск: включить / выключить** — добавляет/убирает из автозагрузки Windows
- **Выход** — закрыть приложение

## Сборка

```
dotnet publish -c Release -r win-x64 --self-contained true
```

Результат: папка `bin\Release\net10.0-windows...\win-x64\publish\` — скопировать целиком.
Windows App Runtime уже внутри, пользователю ничего устанавливать не нужно.

## Стек

- .NET 10 + WinUI 3 (Windows App SDK 1.6+)
- `H.NotifyIcon.WinUI` — трей-иконка
- P/Invoke `keybd_event` — медиа-клавиши и громкость (никакого аудио-драйвера)
- Реестр `HKCU\...\Run` — автозапуск без прав администратора

## Требования

- Windows 11 22H2+ (сборка 22621)
- Для dev-сборки: Visual Studio с ворклоадом WinUI / Windows App SDK
