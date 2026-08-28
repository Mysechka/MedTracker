# Сборка и запуск на Android

Head-проект — `src/Med.Android`, `net10.0-android`, `ApplicationId`
`com.mysechka.medtracker`, минимальная версия ОС — API 26.

Сборка APK на этой машине пока не выполнялась осознанно: текущая цель фазы —
рабочая сборка под macOS. Ниже — полный порядок шагов, чтобы APK собирался
без разбирательств.

## Что нужно установить

| Инструмент | Версия | Проверка |
|---|---|---|
| .NET SDK | 10.0.400 | `dotnet --version` |
| workload `android` | 36.1.69 | `dotnet workload list` |
| JDK | 17 | `java -version` |
| Android SDK Platform | android-35 или android-36 | `ls $ANDROID_HOME/platforms` |
| Android SDK Build-Tools | 35.0.0 или 36.0.0 | `ls $ANDROID_HOME/build-tools` |
| platform-tools (`adb`) | актуальные | `adb version` |

Состояние на этой машине: .NET в `~/.dotnet`, Android SDK в `~/Android/Sdk`
(symlink на Homebrew cmdline-tools), JDK 17 в `~/Android/jdk` (symlink на
`openjdk@17`). Переменные `DOTNET_ROOT`, `ANDROID_HOME` и `JAVA_HOME` прописаны
в `~/.zshrc` и `~/.bashrc`. Установлены build-tools 35.0.0 и 36.0.0,
платформы android-35 и android-36.

Если workload отсутствует: `dotnet workload restore MedTracker.slnx`.

## Debug: сборка и запуск на устройстве

```bash
# Устройство подключено по USB, отладка по USB включена
adb devices                       # устройство должно быть в списке как "device"
dotnet build src/Med.Android -t:Run
```

Эмулятор запускается так же, отдельной конфигурации не требует.

## Release: подписанный APK

Keystore создаётся один раз и **не хранится в репозитории** (`*.keystore` и
`*.jks` в `.gitignore`). Держать его лучше вне рабочей копии.

```bash
keytool -genkeypair -v \
  -keystore ~/keys/medtracker-release.keystore \
  -alias medtracker \
  -keyalg RSA -keysize 2048 -validity 10000
```

Пароли передаются переменными окружения, а не аргументами в истории команд:

```bash
export MEDTRACKER_KEYSTORE=~/keys/medtracker-release.keystore
export MEDTRACKER_KEYSTORE_PASS=...
export MEDTRACKER_KEY_ALIAS=medtracker
export MEDTRACKER_KEY_PASS=...

dotnet publish src/Med.Android \
  -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$MEDTRACKER_KEYSTORE" \
  -p:AndroidSigningStorePass="$MEDTRACKER_KEYSTORE_PASS" \
  -p:AndroidSigningKeyAlias="$MEDTRACKER_KEY_ALIAS" \
  -p:AndroidSigningKeyPass="$MEDTRACKER_KEY_PASS"
```

APK окажется в `src/Med.Android/bin/Release/net10.0-android/publish/`.
Установка: `adb install -r <файл>.apk`.

## Подводные камни

**Потерянный keystore = потерянное приложение.** Обновить установленный APK
можно только тем же ключом. Бэкап keystore и паролей обязателен, восстановления
не существует.

**Пароли не должны попасть в репозиторий и в историю оболочки.** Только
переменные окружения. В CI — секреты раннера. Пароль в `csproj` или в
`.gitignore`-файле, который однажды закоммитили, — это утечка навсегда.

**`JAVA_HOME` должен указывать на JDK 17.** На JDK 21+ Android-таргеты .NET
падают на этапе `aapt2`/`d8` с невнятной ошибкой Java-версии. Проверять
`java -version`, а не только наличие `java`.

**Конфигурация читается из ассета, а не из переменных окружения.** На устройстве
`MEDTRACKER_*` не существует. Настройки берутся из
`src/Med.Android/Assets/appsettings.json`, который попадает в APK. Значит, в этом
файле допустим **только anon key**: всё, что лежит в APK, считается публичным.
`service_role` там — недопустимо.

**`SupportedOSPlatformVersion=26`.** На устройстве с более старым API приложение
просто не установится. Понижать без причины не нужно: Avalonia на API < 26
работает нестабильно.

**AOT и тримминг.** `AndroidEnableProfiledAot` выключен осознанно: профильный AOT
удлиняет сборку, а выигрыш на техническом каркасе не проверяем. Тримминг включать
нельзя по той же причине, что и на macOS: Avalonia и клиент Supabase используют
рефлексию.

**Realtime и фоновые потоки.** События Supabase Realtime приходят в фоновом
потоке; коллекции обновляются через `IUiDispatcher`, реализация которого
регистрируется вызовом `AddMedUi()` в `MedAndroidApplication`. Если этот вызов
убрать, приложение упадёт при первом же событии из сети, а не на старте.

**Планировщика в клиенте нет.** Закрытое приложение ничего не присылает: время
приёма отслеживает `pg_cron` + Edge Function `tick`. Локальные таймеры и
`WorkManager` в этой архитектуре не используются.
