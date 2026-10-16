# TicketMonitor

Система мониторинга заявок GLPI с интеграцией в Rocket.Chat, email и Redmine, а также с сервисом наблюдаемости (Watchdog).

---

## Содержание

- [Что это](#что-это)
- [Архитектура](#архитектура)
- [Проекты решения](#проекты-решения)
- [Как это работает](#как-это-работает)
- [Быстрый старт](#быстрый-старт)
- [Конфигурация](#конфигурация)
- [API](#api)
- [Базы данных](#базы-данных)
- [Развёртывание](#развёртывание)
- [Тесты](#тесты)
- [Наблюдаемость](#наблюдаемость)
- [Переменные окружения и секреты](#переменные-окружения-и-секреты)

---

## Что это

**TicketMonitor** — набор .NET-сервисов, которые:

1. Периодически опрашивают **GLPI** по заданным категориям заявок.
2. Детектируют изменения (статус, исполнитель, приоритет, новые ответы от пользователей).
3. Отправляют уведомления в **Rocket.Chat** (с тредами и кнопками-действиями) и на **email** (HTML-шаблон).
4. Позволяют в один клик из уведомления **создать задачу в Redmine** или **добавить решение в GLPI**.
5. Следят за собственным здоровьем и здоровьем внешних сервисов через отдельный сервис **Watchdog**.
6. Пишут структурированные записи обо всех внешних операциях в локальный реестр (`operations.db`), который Watchdog агрегирует в статистику.

---

## Архитектура

```
                    ┌──────────────────────────┐
                    │   TicketMonitor.Api      │  ← основной сервис (порт 8080)
                    │  • опрос GLPI            │
                    │  • детект изменений      │
                    │  • уведомления RC/Email  │
                    │  • backup                │
                    └────────────┬─────────────┘
                                 │
              ┌──────────────────┼───────────────────┐
              │                  │                   │
              ▼                  ▼                   ▼
    ┌──────────────────┐  ┌──────────────┐  ┌──────────────────┐
    │  GLPI REST API   │  │ Rocket.Chat  │  │      SMTP        │
    └──────────────────┘  └──────────────┘  └──────────────────┘

    ┌──────────────────────────┐
    │  TicketMonitor.Redmine   │  ← веб-формы + API (порт 8082)
    │  • /redmine/create       │
    │  • /glpi/solution        │
    │  • Redmine REST API      │
    │  • polling Redmine→GLPI  │
    └──────────────────────────┘

    ┌──────────────────────────┐
    │  TicketMonitor.Watchdog  │  ← наблюдаемость (порт 8083)
    │  • HTTP health-чеки      │
    │  • чтение operations.db  │
    │  • агрегация в watchdog  │
    │  • UI статистики         │
    └──────────────────────────┘
```

---

## Проекты решения

| Проект | Назначение |
|---|---|
| `TicketMonitor.Core` | Общая библиотека: клиенты, сервисы, модели, репозитории, фоновые воркеры, шаблоны email. |
| `TicketMonitor.Api` | Основной веб-сервис: REST API, опрос GLPI, рассылка уведомлений, health-проверки, автоматический бэкап. |
| `TicketMonitor.Redmine` | Веб-сервис: формы создания задач в Redmine и добавления решения в GLPI, API проверки связей, polling закрытых задач Redmine. |
| `TicketMonitor.Watchdog` | Сервис наблюдаемости: HTTP-проверки внешних сервисов, чтение `operations.db`, дневные агрегаты, веб-UI со статистикой. |
| `TicketMonitor.Core.Tests` | Юнит- и интеграционные тесты ядра. |
| `TicketMonitor.Redmine.Tests` | Тесты Redmine-сервиса. |
| `TicketMonitor.Watchdog.Tests` | Тесты Watchdog. |

---

## Как это работает

### Опрос GLPI и детект изменений

`TicketMonitorBackgroundService` → `MonitorManagementService` → на каждый монитор создаётся `MonitorRuntime` → `TicketMonitorWorker`:

1. Раз в `CheckIntervalMinutes` дёргает `GlpiTicketService.GetTicketsByCategoryAsync`.
2. Загружает предыдущий снимок из `tickets_<name>.db` и сравнивает через `TicketChangeDetector`.
3. Значимые дельты: **Статус**, **Исполнитель**, **Приоритет**. Ответы группы поддержки игнорируются.
4. Отправляет уведомления по каждому изменению.

### Уведомления

`NotificationDispatcherService`:

- **Rocket.Chat**: первое сообщение по заявке создаёт корневой пост. Его `message._id` сохраняется в `messageIds` (ключ — ID заявки GLPI). Последующие уведомления уходят в тред (`tmid`), кнопки-действия в тред не дублируются. Для закрытых заявок корневой пост редактируется на «Завершённая заявка #N».
- **Email**: HTML по шаблону `Templates/EmailTemplate.html` (Outlook VML-совместимый, тёмная тема, кнопки-действия).

### Кнопки-действия в уведомлениях

Формируются в `TicketMonitorWorker.BuildActionLinksAsync`:

- **«➕ Создать задачу в Redmine»** / **«🔁 Повторить задачу в Redmine»** — если в настройках канала RocketChat задан `RedmineCreateFormUrl`. Лейбл зависит от `IRedmineLinkChecker.IsLinkedAsync`.
- **«✅ Добавить решение»** — если задан `GlpiSolutionFormUrl` и статус заявки не «Решена» / «Закрыта».

### Redmine-сервис

- `GET /redmine/create?ticketId=N` — форма создания задачи, с cache-aside проверкой связи (сначала локальный `redmine_links.db`, потом Redmine).
- `POST /redmine/create` — создаёт задачу, пишет `externalid` в GLPI, меняет статус на «В работе (запланирована)».
- `GET /glpi/solution?ticketId=N` — форма добавления решения, при успехе followup + перевод заявки в «Решена».
- `GET /api/redmine/links/{ticketId}/exists` — источник истины для `RedmineLinkChecker` из `TicketMonitor.Api`.

### Watchdog

- `HttpMonitorWorker` — раз в `HealthCheckIntervalSeconds` проверяет health-URL сервисов. Успех → дневной агрегат, неудача → сырая запись + счётчик падений.
- `OperationsMonitorWorker` — читает `operations.db` каждого сервиса инкрементально (по `LastProcessedStartedAt`) и агрегирует в дневные срезы.
- `RetentionWorker` — раз в сутки чистит старые записи по политике.
- `DowntimeTracker` — открывает событие падения после `FailureThreshold` неудач подряд, закрывает при восстановлении.
- Веб-UI `/` + API `/api/watchdog/*` + Swagger.

---

## Быстрый старт

### Требования

- .NET 10 SDK
- Windows (сервисы ставятся через `sc.exe`; Linux теоретически возможен, но скрипт деплоя — только Windows)

### Сборка и тесты

```powershell
dotnet build TicketMonitor.slnx -c Release
dotnet test TicketMonitor.slnx -c Release
```

### Локальный запуск

```powershell
# Основной сервис — http://localhost:8080, Swagger: /swagger
dotnet run --project TicketMonitor.Api

# Redmine-сервис — http://localhost:8082, Swagger: /swagger
dotnet run --project TicketMonitor.Redmine

# Watchdog — http://localhost:8083, UI: /, Swagger: /swagger
dotnet run --project TicketMonitor.Watchdog
```

---

## Конфигурация

Все настройки — в `appsettings.json` соответствующего сервиса. Секреты прокидываются через переменные окружения (см. ниже).

### TicketMonitor.Api

```jsonc
{
  "Monitors": [
    {
      "Name": "Docsvision",
      "IsSystem": true,
      "CategoryFilter": "Docsvision",
      "CheckIntervalMinutes": 2,
      "DatabaseFileName": "tickets_docsvision.db",
      "SupportGroupId": 3,
      "Notification": {
        "Channels": [
          {
            "Type": "RocketChat",
            "Enabled": true,
            "Settings": {
              "RedmineCreateFormUrl": "http://host:8082/redmine/create",
              "GlpiSolutionFormUrl": "http://host:8082/glpi/solution"
            }
          },
          { "Type": "Exchange", "Enabled": true, "Settings": {} }
        ]
      }
    }
  ],
  "GLPI": {
    "BaseUrl": "https://glpi.example.ru",
    "AppToken": "",
    "TimeoutSeconds": 30,
    "MaxRetries": 5,
    "RetryDelaySeconds": 60
  },
  "Email": {
    "SmtpServer": "mail.example.ru",
    "Port": 25,
    "From": "monitor@example.ru",
    "To": "admin@example.ru"
  },
  "RocketChat": {
    "ServerUrl": "https://chat.example.ru",
    "TimeoutSeconds": 30,
    "RoomIds": { "Docsvision": "GENERAL" }
  },
  "RedmineLinkChecker": { "BaseUrl": "http://host:8082", "TimeoutSeconds": 5 },
  "Backup": { "Enabled": true, "IntervalHours": 24, "RetentionCount": 10 },
  "Operations": { "ServiceName": "TicketMonitor.Api", "RetentionDays": 30 },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:8080" } } }
}
```

### TicketMonitor.Redmine

```jsonc
{
  "Redmine": {
    "BaseUrl": "http://redmine.example.ru/redmine",
    "ApiToken": "",
    "DefaultProjectId": 18,
    "DefaultTrackerId": 3,
    "DefaultStatusId": 1,
    "DefaultPriorityId": 2,
    "DefaultDevelopmentEnvironment": "OTHER",
    "PollingIntervalMinutes": 5,
    "FormBaseUrl": "http://host:8082/redmine/create",
    "CustomFieldHdNumber": 5,
    "CustomFieldTechDebt": 7,
    "CustomFieldDevEnvironment": 13,
    "PollingClosedStatusIds": [ 3, 5, 6, 11, 12 ],
    "AllowedProjectIds": [ 18, 46 ]
  },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:8082" } } }
}
```

### TicketMonitor.Watchdog

```jsonc
{
  "Watchdog": {
    "HealthCheckIntervalSeconds": 30,
    "OperationsCheckIntervalSeconds": 15,
    "FailureThreshold": 3,
    "HttpRawRetentionDays": 30,
    "HttpAggregateRetentionDays": 90,
    "OperationsAggregateRetentionDays": 90,
    "Services": [
      {
        "Name": "TicketMonitor.Api",
        "DisplayName": "TicketMonitor API",
        "HealthUrl": "http://host:8080/healthz",
        "BasePath": "C:\\TicketMonitor",
        "OperationsDbFile": "operations.db"
      }
    ]
  },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:8083" } } }
}
```

---

## API

### TicketMonitor.Api

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/health` | Состояние сервиса и всех мониторов |
| GET | `/healthz` | Детальный health-check (включая внешние сервисы) |
| GET | `/api/monitors` | Список мониторов (секреты замаскированы) |
| GET | `/api/monitors/{name}` | Один монитор |
| POST | `/api/monitors` | Добавить монитор |
| PUT | `/api/monitors/{name}` | Обновить монитор |
| DELETE | `/api/monitors/{name}` | Удалить монитор |
| POST | `/api/monitors/{name}/check` | Принудительная проверка |
| GET | `/api/monitors/{name}/status` | Статус монитора |
| GET | `/api/monitors/{name}/changes` | Изменения (без очистки) |
| GET | `/api/monitors/{name}/changes/consume` | Изменения + очистка |
| GET | `/api/monitors/{name}/tickets` | Заявки монитора (компактно) |
| GET | `/api/monitors/{name}/tickets/full` | Заявки монитора (полностью) |
| GET | `/api/monitors/{name}/tickets/{id}` | Конкретная заявка |
| POST | `/api/monitors/{name}/notify/{ticketId}` | Ручное уведомление |
| GET | `/api/backups` | Список бэкапов |
| POST | `/api/backups/create` | Создать бэкап |
| POST | `/api/backups/restore?fileName=…` | Восстановить |

Rate limiting: 100 запросов / 30 сек на IP (health-эндпоинт — без ограничений).

### TicketMonitor.Redmine

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/redmine/create?ticketId=N` | Форма создания задачи |
| POST | `/redmine/create` | Создать задачу |
| GET | `/glpi/solution?ticketId=N` | Форма добавления решения |
| POST | `/glpi/solution` | Добавить followup + перевести в «Решена» |
| GET | `/api/redmine/projects` | Список проектов |
| GET | `/api/redmine/projects/{id}/trackers` | Трекеры проекта |
| GET | `/api/redmine/projects/{id}/custom-fields` | Custom fields проекта |
| GET | `/api/redmine/projects/{id}/memberships` | Участники проекта |
| GET | `/api/redmine/links/{ticketId}/exists` | `{ "linked": true/false }` |
| GET | `/health` | Health |

### TicketMonitor.Watchdog

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/` | HTML-страница статистики |
| GET | `/api/watchdog/health` | Health |
| GET | `/api/watchdog/services` | Список сервисов |
| GET | `/api/watchdog/operation-types` | Типы операций с лейблами |
| GET | `/api/watchdog/stats/today` | Статистика за сегодня |
| GET | `/api/watchdog/stats/yesterday` | За вчера |
| GET | `/api/watchdog/stats/week` | За 7 дней |
| GET | `/api/watchdog/stats/month` | За 30 дней |
| GET | `/api/watchdog/stats/range?from=&to=` | За произвольный период |
| GET | `/api/watchdog/downtimes?from=&to=` | События падений |

---

## Базы данных

Все БД — **LiteDB**, лежат в `Data/` рядом с exe.

| Файл | Назначение |
|---|---|
| `tickets_<monitor>.db` | Снимок заявок монитора |
| `monitors.db` | Динамические (не-системные) мониторы |
| `messageIds` (коллекция в `tickets_*.db`) | ID корневых сообщений Rocket.Chat по заявкам |
| `operations.db` | Реестр операций (для Watchdog) |
| `redmine_links.db` | Связи GLPI ↔ Redmine |
| `redmine_state.db` | Состояние polling Redmine |
| `watchdog.db` | HTTP-проверки, падения, дневные агрегаты |
| `Backups/` | ZIP-архивы бэкапов |

**Никогда не коммитьте `Data/` и `Backups/` — они в `.gitignore`.**

---

## Развёртывание

`deploy.ps1` делает полный цикл: сборка → тесты → публикация → бэкап → создание/остановка служб → деплой → старт → health-check → автооткат.

```powershell
# От имени администратора
.\deploy.ps1

# Без тестов
.\deploy.ps1 -SkipTests

# Свой таймаут health-check
.\deploy.ps1 -HealthTimeoutSec 120
```

Что происходит:

1. `dotnet build -c Release` по всему решению.
2. `dotnet test` по каждому тест-проекту последовательно.
3. `dotnet publish` в `publish/{Api,Redmine,Watchdog}`.
4. Бэкап текущих папок в `deploy-backups/yyyyMMdd_HHmmss/{Api,Redmine,Watchdog}` (без `Data`, `Logs`, `Backups`).
5. `sc.exe create`, если службы нет; настройка авто-рестарта (`sc.exe failure ... restart/5000/...`).
6. `robocopy /MIR` из publish в целевые папки (`C:\TicketMonitor`, `C:\TicketMonitor-Redmine`, `C:\TicketMonitor-Watchdog`).
7. `Start-Service` для каждой.
8. Health-check:
   - Api: `http://localhost:8080/api/health`
   - Redmine: `http://localhost:8082/health`
   - Watchdog: `http://localhost:8083/api/watchdog/health`
9. При провале — остановка служб, `robocopy` из бэкапа, запуск, выход с кодом 1.

---

## Тесты

```powershell
# Все
dotnet test TicketMonitor.slnx -c Release

# По проектам
dotnet test TicketMonitor.Core.Tests/TicketMonitor.Core.Tests.csproj -c Release
dotnet test TicketMonitor.Redmine.Tests/TicketMonitor.Redmine.Tests.csproj -c Release
dotnet test TicketMonitor.Watchdog.Tests/TicketMonitor.Watchdog.Tests.csproj -c Release
```

Стек: xUnit, FluentAssertions, фейковые `HttpClient`-хендлеры и фабрики, in-memory заглушки `IOperationRegistry`.

---

## Наблюдаемость

### Реестр операций (`operations.db`)

Оборачивает все внешние вызовы через `IOperationRegistry.RunAsync("Namespace:Action", …)`. Пишет:

- `Type`, `ServiceName`, `Status` (`Success` / `Failed` / `Cancelled`)
- `StartedAt`, `CompletedAt`, `DurationMs`
- `ErrorMessage` (до 2000), `StackTrace` (до 4000), `ContextJson` (до 1000)

Буфер флашится раз в 5 секунд и при Dispose. Старые записи чистит `OperationRetentionService` (по умолчанию — старше 30 дней).

### Watchdog

- **UI `/`**: таблицы «Статус сервисов», «Операции по сервисам», «События падения»; переключатель периодов (сегодня / вчера / неделя / месяц); автообновление раз в минуту.
- **Человекочитаемые лейблы** типов операций — в `operation-types.json`.
- **Хранение**: сырые HTTP-чеки — 30 дней, агрегаты — 90 дней.

### Логи

Serilog, папка `Logs/` рядом с exe:

- `ticket-monitor-YYYYMMDD.log`, `errors-YYYYMMDD.log`, `monitor-<name>-YYYYMMDD.log`
- `redmine-YYYYMMDD.log`, `redmine-errors-YYYYMMDD.log`
- `watchdog-YYYYMMDD.log`, `watchdog-errors-YYYYMMDD.log`

Ротация — ежедневно, хранится 30 файлов, размер — до 100 МБ на файл.

---

## Переменные окружения и секреты

Секреты **не храните в `appsettings.json`**. Прокидывайте через:

- переменные окружения (в т.ч. через `HKLM\SYSTEM\CurrentControlSet\Services\<Service>\Environment` для Windows-служб);
- `dotnet user-secrets` для локальной разработки.

Двойное подчёркивание `__` — разделитель секций. Примеры:

| Ключ | Пример значения |
|---|---|
| `GLPI__BaseUrl` | `https://glpi.example.ru` |
| `GLPI__AppToken` | `...` |
| `GLPI__UserToken` | `...` |
| `Redmine__ApiToken` | `...` |
| `Email__SmtpServer` | `mail.example.ru` |
| `Email__From` | `monitor@example.ru` |
| `Email__To` | `admin@example.ru;ops@example.ru` |
| `RocketChat__ServerUrl` | `https://chat.example.ru` |
| `RocketChat__Token` | `...` |
| `RocketChat__UserId` | `...` |
| `RocketChat__RoomIds__Docsvision` | `GENERAL` |

В сервисах `TicketMonitor.Api`, `TicketMonitor.Redmine`, `TicketMonitor.Watchdog` на старте читаются переменные окружения из ключей реестра Windows-служб — так их удобно задавать без правки конфигов.

---

## Лицензия и автор

Автор: **Королев Максим** (`mkorolev@example.ru`).

Внутренний проект. Уточните условия использования у владельца репозитория.