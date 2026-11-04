# HANDOFF — SoundDeck

## Где остановились (2026-11-04)

**v1.4.1 выпущен** (2026-11-04) — хвосты после 1.4.0:

- `AudioService` кэширует список устройств и id устройства по умолчанию, сбрасывает кэш по
  уведомлениям Windows (`Notify` → `Invalidate`) и после `SetDefaultDevice`. Причина: `Dispose` в NAudio
  не освобождает COM-объекты, а пересоздание на каждом опросе копило хэндлы
  (1.4.0: 818 → 1485 за 2 мин; 1.4.1: держится ~950).
- Меню трея перечитывает подписи при каждом открытии — язык меняется без перезапуска.
- `installer/SoundDeck.iss` удалён; README на de/es/ja/zh догнаны до en/ru.
- **Сжатие exe проверено и отвергнуто:** `EnableCompressionInSingleFile=true` даёт 73 МБ вместо 164,
  но +75 МБ оперативки на всё время работы (сборки распаковываются в память). Пользователь выбрал
  меньше оперативки. Не включать обратно.

1.4.0: самоустановка в `%LocalAppData%\Programs\SoundDeck`, автообновление с GitHub releases,
доска без горизонтального скролла, скрытие устройств — см. README.

## Что дальше

1. Следующий релиз: поднять версию в csproj, `dotnet publish` (команда в README), подписать
   signtool `/sha1 E8CF795F… /s My /fd SHA256 /tr http://timestamp.digicert.com`, `gh release create vX.Y.Z`
   с ассетами `SoundDeck.exe` + `SoundDeck-PublicCert.cer`. Имя ассета ровно `SoundDeck.exe`, тег `vX.Y.Z`.
2. У пользователя установлена копия в `%LocalAppData%\Programs\SoundDeck`, обновляется сама.
3. Открытых хвостов из ревью нет.
