# Резервное копирование и восстановление БД

Автоматический бэкап PostgreSQL (Supabase) через `pg_dump` + проверка восстановлением в CI.

## Как устроено

- Workflow: `.github/workflows/db-backup.yml`.
- Расписание: ежедневно в 03:00 UTC; плюс ручной запуск (`workflow_dispatch`).
- Дамп: схема `public`, plain SQL, gzip, `--clean --if-exists --no-owner --no-privileges`.
- Хранение: GitHub Actions artifacts, имя `db-backup`, файл `db-backup-YYYYMMDD-HHMMSS.sql.gz`, retention 90 дней.
- Job `restore-test`: разворачивает свежий дамп в чистом `postgres:17` внутри CI и проверяет таблицы/миграции.
- Из дампа исключаются `CREATE/COMMENT/ALTER EXTENSION`: в Supabase расширения стоят в схеме `extensions`, в чистом Postgres их нет. Индексы на `pg_trgm` используют `extensions.gin_trgm_ops`, поэтому перед restore создаются схема `extensions` и расширение `pg_trgm`.

> Важно: cron-расписание GitHub включает только для workflow из default-ветки (`main`). До мержа в main запускай вручную из ветки `dev`.

## Разовая настройка

1. Supabase Dashboard → **Connect** → строка подключения в формате **URI** (Session pooler), например:
   `postgresql://postgres.<ref>:<password>@aws-1-eu-central-1.pooler.supabase.com:5432/postgres?sslmode=require`
2. GitHub → Settings → Secrets and variables → Actions → **New repository secret**:
   - Name: `SUPABASE_DB_URL`
   - Secret: строка из п.1

Почему URI: `pg_dump`/libpq не понимает Npgsql-формат с `;` (`Host=...;Port=...`). Строка из Supabase уже содержит URL-encoded пароль.

## Сделать бэкап

- Вручную: Actions → **DB Backup** → Run workflow.
- Скачать: Actions → нужный запуск → Artifacts → `db-backup`.

## Восстановить из дампа

Локально (нужен `psql` 17 или Docker):

```bash
docker run --rm --network host -e PGPASSWORD=<pass> postgres:17 \
  psql -h 127.0.0.1 -U <user> -d <db> -v ON_ERROR_STOP=1 \
  -c 'CREATE SCHEMA IF NOT EXISTS extensions' \
  -c 'CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA extensions'
gunzip -c db-backup-YYYYMMDD-HHMMSS.sql.gz | docker run --rm -i --network host \
  -e PGPASSWORD=<pass> postgres:17 \
  psql -h 127.0.0.1 -U <user> -d <db> -v ON_ERROR_STOP=1
```

Схема `extensions` + `pg_trgm` нужны потому, что в дампе нет `CREATE EXTENSION` (в Supabase расширения живут в схеме `extensions`), а trgm-индексы ссылаются на `extensions.gin_trgm_ops`.

В Supabase — только при реальной аварии и после согласования с командой:

```bash
gunzip -c db-backup-YYYYMMDD-HHMMSS.sql.gz | psql "$SUPABASE_DB_URL" -v ON_ERROR_STOP=1
```

Учти: дамп содержит `DROP ... IF EXISTS` для объектов схемы `public` — восстановление перезапишет текущие таблицы и данные.

После восстановления проверить: `/health`, `/swagger`, список товаров, заказы (`/api/orders/{id}`).

## Откат миграций

```bash
dotnet ef migrations list
dotnet ef database update <ИмяПредыдущейМиграции>
```

Применённые миграции: `InitialCreate`, `AddCatalogIndexes`, `OptimizeCatalogIndexesAdvanced`, `AddOrderPublicId`.

Пример: убрать `PublicId` — `dotnet ef database update OptimizeCatalogIndexesAdvanced`. Внимание: Down-миграция возвращает только схему; колонка `PublicId` со значениями будет удалена безвозвратно.

Так как БД общая, откат миграций — только по согласованию в чате команды. Для возврата данных используй restore из дампа, а не `database update`.

## Ограничения

- Supabase free-план не делает автоматических бэкапов — этот workflow единственная копия.
- Scheduled workflows отключаются после 60 дней без активности в репозитории; любой коммит/запуск возвращает расписание.
- Artifacts старше 90 дней удаляются автоматически.
- Репозиторий публичный, поэтому artifact с дампом может скачать любой. Шифрование дампа (например, `gpg` + секрет `BACKUP_PASSPHRASE`) пока не сделано — при требовании безопасности добавить.
