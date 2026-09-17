# Нативные компоненты

Руками сюда ничего класть не нужно. Утилита `golether-components` (её вызывает `Scripts/publish-win.ps1`)
скачивает закреплённые версии, сверяет SHA-256 и раскладывает файлы:

```
Native/<RID>/libmpv-2.dll          компонент видео
Native/<RID>/gstreamer/...         компонент камер и голоса (только нужные плагины и их зависимости, ~37 МБ)
```

Сборка UI-проекта копирует содержимое `Native/<RID>/` в `native/` рядом с приложением. Подпапки игнорируются git.

Подготовить компоненты для отладки:

```powershell
dotnet run --project Sources/Tools/Golether.Tools.Components -- fetch --output Native/win-x64 --cache artifacts/cache/components
```

Если компонентов нет ни в пакете, ни в `native/`, приложение показывает кнопку «Установить» и ставит их в папку
данных пользователя (`components/`) без прав администратора.

| Компонент | Windows | Linux | macOS |
|---|---|---|---|
| Видео (libmpv) | архив `mpv-dev-*.7z` (shinchiro/mpv-winbuild-cmake), распаковка встроенным `tar.exe` | пакет дистрибутива (`libmpv2`, `mpv-libs`); приложение подскажет команду | `brew install mpv` |
| Камеры и голос (GStreamer) | официальный установщик 1.28.7: тихая установка во временную папку, отбор нужных файлов, удаление временной установки. Уже установленный полный GStreamer используется без скачивания | пакеты `gstreamer1.0-plugins-{base,good,bad}`, `gstreamer1.0-nice` | `brew install gstreamer` |

Явный путь к libmpv по-прежнему можно задать переменной `GOLETHER_LIBMPV`.
