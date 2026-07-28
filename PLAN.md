# SoundDeck — план и архитектура

Приложение для Windows, живущее в системном трее, которое управляет источниками
звука как канбан-доской: колонки — устройства вывода, карточки — приложения,
между которыми звук перетаскивается drag&drop. Запоминает раскладку в профили,
сам чинит сбросы Windows (пустое выпадающее меню / потеря дефолта) и умеет дублировать
звук приложения на два выхода одновременно.

---

## 1. Решения (зафиксированы)

| Вопрос | Решение |
|---|---|
| Дубль на 2 выхода | Свой loopback-микшер внутри приложения (без внешних драйверов) |
| UI-стек | C# / .NET 8 ядро + React/Vite фронт внутри WebView2 |
| Первый шаг | Полный план + архитектура (этот файл), затем прототип ядра |

---

## 2. Что позволяет Windows (границы возможного)

| Возможность | Статус | Механизм |
|---|---|---|
| Список устройств вывода/ввода + состояние | ✅ документировано | `IMMDeviceEnumerator`, `IMMNotificationClient` |
| Громкость / mute на приложение | ✅ документировано | `IAudioSessionManager2`, `ISimpleAudioVolume` |
| Смена устройства по умолчанию | ⚠️ недокументированный COM | `IPolicyConfig` (как в EarTrumpet, SoundSwitch) |
| Приложение → конкретный выход | ⚠️ недокументированный COM (Win10 1803+) | `IAudioPolicyConfigFactory` / `IAudioPolicyConfig` |
| Дубль одного звука на 2 выхода | ❌ нативно нет | WASAPI loopback-capture + повторное воспроизведение |
| Авто-восстановление раскладки | ✅ киллер-фича | реакция на нотификации + повторное применение профиля |

Референс (MIT, легально смотреть, как реализованы COM-хаки): **EarTrumpet**.

---

## 3. Архитектура

```
┌─────────────────────────────────────────────────────────┐
│  SoundDeck.exe  (single-instance, tray, .NET 8, WinUI/WPF host) │
│                                                           │
│  ┌───────────────┐   postMessage bridge   ┌────────────┐  │
│  │  WebView2      │◄──────────────────────►│  Core (C#) │  │
│  │  React/Vite UI │   (JSON команды/события)│            │  │
│  └───────────────┘                        │            │  │
│                                            │  Audio     │  │
│                                            │  Profiles  │  │
│                                            │  AutoHeal  │  │
│                                            │  Mixer     │  │
│                                            └─────┬──────┘  │
└──────────────────────────────────────────────────┼────────┘
                                                    ▼
                             Windows Core Audio (WASAPI / MMDevice / PolicyConfig)
```

### Модули ядра (C#)

- **AudioGraph** — обёртка над Core Audio: enumerate устройств и сессий, подписка
  на изменения (`IMMNotificationClient`, `IAudioSessionEvents`).
- **DefaultDeviceController** — смена default / default-communications через
  `IPolicyConfig` (P/Invoke, COM-интероп).
- **AppRouter** — маршрутизация приложения на устройство через `IAudioPolicyConfig`
  (per-app device preference, Win10 1803+).
- **VolumeController** — громкость/mute на устройство и на сессию.
- **LoopbackMixer** — дубль: `WasapiLoopbackCapture` источника → ресемпл →
  `WasapiOut` на второе устройство. Управление задержкой/буфером.
- **ProfileStore** — профили и авто-старт: JSON в `%AppData%\SoundDeck\`.
- **AutoHealEngine** — правила восстановления при событиях устройств.
- **Bridge** — сериализация состояния и команд между Core и WebView2.

### Фронт (React/Vite/TS в WebView2)

- `@dnd-kit/core` — drag&drop карточек между колонками.
- `framer-motion` — анимации перетаскивания, появления карточек.
- Клик по карточке приложения раскрывает ползунок громкости с перетаскиваемой
  точкой (thumb) — тянешь и меняешь громкость именно этого приложения.
- Стекло/акрил: полупрозрачное окно + backdrop, тёмная тема как в концепте.
- Состояние приходит из Core через `window.chrome.webview.postMessage`;
  команды уходят обратно (route, setVolume, applyProfile, enableMirror).

---

## 4. Модель данных

```ts
Device      { id, name, kind: 'render'|'capture', bus:'HDMI'|'USB'|'BT'|...,
              state:'active'|'unplugged'|'disabled', volume, muted,
              isDefault, isDefaultComm }
AppSession  { pid, name, iconB64, volume, muted, outputDeviceId, mirrored:boolean }
Profile     { id, name, hotkey?, defaultDeviceId, defaultCommId,
              routes: { appMatch -> deviceId }, mirrors: { appMatch -> [ids] } }
Rule        { on:'deviceReturned'|'defaultLost'|..., action: applyProfile|forceDefault }
```

Профиль сопоставляет приложение не по PID (он меняется), а по имени
исполняемого файла / пути — чтобы правило переживало перезапуск приложения.

---

## 5. Ключевые сценарии

1. **Перетащить Edge на «Наушники»** → `AppRouter.SetAppOutput(edge.exe, headphones)`.
2. **Колонка «Оба сразу»** → `LoopbackMixer.Start(app, primary, secondary)`;
   основной выход остаётся штатным, второй получает зеркальный поток.
3. **Профиль «Диван»** → одним кликом/хоткеем ставит default = TV и раскидывает
   приложения по сохранённой раскладке.
4. **Авто-лечилка** → на `DefaultDeviceChanged`/`DeviceStateChanged`: если активный
   профиль ожидает устройство, которое вернулось из сна — форсим default и
   переприменяем routes. Чиним «пустое выпадающее меню».

---

## 6. Этапы

- **M0 — прототип ядра ✅ ПРОЙДЕН (probe/):** на Win11 26200 подтверждено:
  перечисление устройств/сессий/громкости (NAudio); смена default через
  `IPolicyConfig` → S_OK; per-app роутинг через `IAudioPolicyConfig` (чтение и
  запись) → SUCCESS. Ключевой нюанс интеропа: `IAudioPolicyConfig` — WinRT-класс,
  но .NET Core не маршалит `IInspectable` → объявляем как `IUnknown` и вручную
  добавляем 3 слота IInspectable перед методами. Формат deviceId для роутинга:
  `\\?\SWD#MMDEVAPI#{endpoint}#{e6327cad-dcec-4949-ae8a-991e976a79d2}` (render).
- **M1 — трей + WebView2 + доска ✅ (app/):** окно-флаут из трея (WPF, Win11
  rounded corners), HTML/CSS/JS-доска в WebView2, мост C#↔JS (postMessage),
  живой список устройств/приложений с иконками, drag&drop = роутинг, слайдеры
  громкости с перетаскиваемой точкой (клик по карточке раскрывает). Проверено
  скриншотом: реальные устройства и маршруты (msedge→LED TV) отображаются верно.
- **M2 — профили + авто-лечилка ✅:** сохранение/переключение раскладок (JSON в
  %AppData%\SoundDeck), реакция на события устройств (IMMNotificationClient,
  debounce), авто-восстановление активного профиля при возврате устройства.
- **M3 — дубль на 2 выхода ✅ (device-level):** кнопка «＋ дубль» на устройстве,
  WasapiLoopbackCapture основного выхода → WasapiOut на второй. Точечный дубль
  одного приложения (process-loopback) — будущее улучшение.
- **M4 — полировка ⏳:** авто-старт (реестр HKCU Run) ✅; иконки приложений ✅;
  осталось: установщик (MSIX), хоткеи профилей, темы. boot.log — dev-диагностика.

---

## 7. Риски и как проверяем

- Недокументированные COM могут отличаться по версиям Windows → M0 проверяет на
  реальной машине пользователя (Win11 Pro 26200) в первую очередь.
- Задержка loopback-дубля → замер в M3, при необходимости уменьшение буфера.
- Иконки/имена сессий не всегда доступны → фолбэк на имя процесса.
