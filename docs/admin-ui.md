# Interfaz de administración (admin UI)

Front de administración del pipeline: **web React (Vite)** + **BFF Node (Express)**. Se levanta todo el entorno (microservicios, Postgres, LocalStack, Mailpit, Prometheus, Grafana, BFF y web) con un único comando desde la raíz:

```
npm run setup
```

El script ([`scripts/setup.mjs`](../scripts/setup.mjs)) crea `.env` desde `.env.example` si no existe, hace `docker compose up -d --build`, espera los healthchecks, instala las dependencias de `frontend/bff` y `frontend/web`, arranca BFF (`:4000`) + web Vite (`:5173`) en paralelo y abre el browser. `Ctrl+C` detiene BFF y web (el stack docker sigue; `docker compose down` lo para).

| Pieza | URL | Qué es |
|---|---|---|
| Web | http://localhost:5173 | Admin UI (3 pestañas) |
| BFF | http://localhost:4000/health | Backend-for-frontend (Express + TS) |
| Mailpit | http://localhost:8025 | Bandeja donde caen los emails de "import completado" |

## Arquitectura

```
[web React :5173] --/api--> [BFF Express :4000] --HTTP--> [ingestion :18080 / query :18081]
                                 |                                        ^
                                 +--> DATA_DIR (upload local directo) --->-+
                                 +--> S3 (LocalStack, bucket bcra-local) -+
                                 +--> Postgres (tabla notification_recipient, rol owner)
```

- La web **nunca** habla directo con los microservicios ni con la DB: todo pasa por el BFF (proxy fino + lógica de upload). En dev, Vite proxya `/api` y `/health` al BFF (`vite.config.ts`).
- **Secretos solo en el BFF**: `UPLOAD_API_KEY` (API key del `POST /imports`), credenciales AWS/LocalStack y password de Postgres viven en `.env` del host y llegan únicamente al proceso del BFF.
- Stack: React 19 + TypeScript + Tailwind v4 + shadcn/ui, TanStack Query (data fetching/polling) y TanStack Table (grillas).

## Pestañas

1. **Archivo** — última carga del archivo de deudores (estado, líneas, duración, job, source) vía `GET /imports/latest` del ingestion; upload con progreso en 2 fases (ver abajo) con selector de destino: entrega directa al dir compartido del ingestion (default) o subida a S3; alta/baja de emails de notificación. El upload acepta además los archivos de referencia opcionales `Maeent.txt` (maestro de entidades) y `Nomdeu.txt` (nomenclador): el pipeline los busca junto al archivo de deudores, así que hay que incluirlos la primera vez (o cuando cambien) — después quedan junto al source por periodo.
2. **Deudores** — grilla paginada del backend (`GET /deudores` con `offset`/`limit`, total y navegación de páginas; respeta `Query:TopMax`) con filtro por situación máxima (`situacionMaxima=`, las situaciones BCRA ordenadas por severidad).
3. **Entidades** — grilla de entidades del import activo (código, nombre, `sumaTotalPrestamos`) con filtro por código vía `GET /entidades?codigo=`, más totales generales de `GET /totales`.

## Flujo del upload con progreso

1. **Fase (a) — entrega del archivo**: la web hace `POST /api/imports/upload` (multipart) al BFF con `onUploadProgress` de axios y un destino `storage`: `local` (default de la UI — el BFF copia al `DATA_DIR` del host, que el compose monta como `/data` en el ingestion, sin pasar por S3) o `s3` (sube al bucket `bcra-local` de LocalStack; si el campo no viene, el BFF defaulta a `s3`, retrocompatible). En ambos destinos las referencias opcionales van al mismo prefijo del periodo (`<periodo>/Maeent.txt`, `<periodo>/Nomdeu.txt`) y el archivo de deudores con nombre **por contenido**: `<periodo>/deudores-<sha256-16>.txt`.
2. El BFF calcula el **sha256 del archivo** (etag) y llama a `POST /imports` del ingestion con `source=s3://…/deudores-<hash>.txt` (o `/data/…` en modo local), etag, size y periodo. La idempotencia (`source+etag+periodo`, índice único) es la del backend: re-subir el mismo archivo en el mismo periodo devuelve el job existente (`alreadyExisted`), sin re-procesar.
3. **Fase (b) — procesamiento**: la web hace polling de `GET /api/imports/{id}` cada 2 s y muestra `loadingProgressPercent` (`bytesCommitted/size`) hasta el estado terminal (`Completed`/`Failed`).

## Emails de notificación

Tabla Postgres `notification_recipient` (migración `005_notification_recipients.sql`, creada por las migraciones DbUp de ingestion; notifier lee con rol `bcra_readonly`, el BFF escribe con rol owner):

- Alta/baja desde la pestaña Archivo → `GET/POST/DELETE /api/notification-recipients` del BFF (email normalizado a minúsculas, único).
- Al completarse un import, `EmailNotifier` envía a **todos** los registrados. Tabla vacía → fallback a `NOTIFICATIONS__EMAIL__TO` de `.env` (retrocompatible con la config pre-admin-UI). Sin destinatarios en ningún lado → se loguea un warning y no se envía.
- Los emails caen en Mailpit (o al SMTP real configurado). La semántica at-least-once es la del notifier ([ADR-010](adr/ADR-010-notifier-at-least-once-webhook.md)).

## Endpoints del BFF

| Método y ruta | Upstream / destino | Notas |
|---|---|---|
| `GET /health` | — | Liveness del BFF |
| `GET /api/imports/latest` | ingestion `GET /imports/latest` | 404 upstream → `{latest: null}` (empty state) |
| `POST /api/imports/upload` | `DATA_DIR` o S3 + ingestion `POST /imports` | multipart `file` + `periodo` (yyyyMM, default mes corriente) + opcionales `maeent`/`nomdeu` + `storage` (`local` \| `s3`, default `s3` si viene vacío; la UI manda `local` por defecto); agrega `X-Api-Key` |
| `GET /api/imports/:id` | ingestion `GET /imports/{id}` | Polling de estado |
| `GET /api/deudores?offset&limit&top&omitirSinNombre&situacionMaxima` | query `GET /deudores` | Envelope con `total` (respeta el filtro de situación en el conteo) |
| `GET /api/entidades?codigo=` | query `GET /entidades` | Filtro exacto/parcial por código |
| `GET /api/totales` | query `GET /totales` | Totales del import activo |
| `GET/POST/DELETE /api/notification-recipients` | Postgres | CRUD de destinatarios |

Errores upstream: el BFF mapea fallos de fetch a `502 Bad Gateway` y propaga status + body del resto.

## Variables de entorno (`.env`)

Solo las del admin UI (el resto: ver `.env.example`).

| Variable | Default | Uso |
|---|---|---|
| `BFF_PORT` | `4000` | Puerto del BFF |
| `WEB_PORT` | `5173` | Puerto de Vite |
| `INGESTION_API_URL` / `QUERY_API_URL` | `http://localhost:18080` / `:18081` | Base URLs upstream |
| `UPLOAD_API_KEY` | — (requerida) | API key del `POST /imports` (la exige el ingestion) |
| `S3_ENDPOINT`, `S3_BUCKET`, `AWS_*` | LocalStack / `bcra-local` | Subida de archivos (modo `storage=s3`) |
| `POSTGRES_HOST/PORT/DB/USER/PASSWORD` | `localhost:5544` | Tabla `notification_recipient` |
| `INGESTION_SOURCE_ROOT` | `/data` | `DATA_DIR` visto DENTRO del container del ingestion (source `/data/...` en modo local) |

Las URLs, el endpoint de S3 y el host de Postgres son opcionales (defaults sensatos); `UPLOAD_API_KEY` y `POSTGRES_PASSWORD` sí son requeridas.

## Desarrollo y tests

```
npm test              # frontend/bff (vitest + supertest, 30 tests)
npm run build && npm test   # frontend/web (tsc + vite build; vitest, 27 tests)
```

El BFF se prueba con upstream y store mockeados (`test/helpers.ts`); la web con Testing Library sobre las 3 páginas y componentes (DataGrid con paginación, RecipientesEmail, upload).
