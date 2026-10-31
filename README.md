# Wayni Challenge — Pipeline ELT de deudores bancarios (BCRA)

Solución de microservicios en .NET para el pipeline ELT de deudores financieros: ingestion → agregación → swap → consulta.

> Documento de presentación del repositorio: [docs/overview.html](https://fedesauzzadev.github.io/wayni-challenge/overview.html) (versión renderizada; el archivo fuente es autocontenido y funciona offline con doble click).

## Concurrencia y bloqueos

Toda la estrategia vive en PostgreSQL, sin locks distribuidos ni afinidad de sesión (ver `docs/adr/`).

| Situación | Mecanismo |
|---|---|
| Crear un import (upload) | `INSERT ... ON CONFLICT (source, etag, periodo) DO NOTHING` sobre índice único `uq_import_job_source_etag_periodo`; si ya existe, se devuelve el job existente (nunca "buscar y después insertar"). Si el existente es un terminal **no publicado** — Completed cuyos agregados fueron limpiados por el swap de un import posterior, o Failed — se descarta en la misma transacción (con limpieza de su storage residual) y se crea una corrida nueva: re-subir el mismo archivo siempre termina publicándolo. |
| Worker reclama un job | `SELECT ... FOR UPDATE SKIP LOCKED` + `UPDATE` en una transacción READ COMMITTED corta: Pending → Loading (o takeover de Loading/Aggregating con lease vencido), seteando `lease_owner` y `heartbeat_at`. |
| Un solo import activo | Índice único parcial `uq_import_job_single_active` sobre estados `Loading`/`Aggregating`. Colisión (23505) → el job queda Pending y se reintenta, sin fallar. |
| Worker caído / conexión perdida | Sin heartbeat, el lease vence (`heartbeat_at <= cutoff`); otro worker lo reclama vía SKIP LOCKED y retoma desde `bytes_committed`. El worker que pierde la conexión aborta sin confirmar nada más. |
| Commit de lote a staging | El COPY binario del lote y el UPDATE del checkpoint (`bytes_committed`, líneas, etag/size, heartbeat) se confirman en UNA transacción READ COMMITTED: no existe lote en staging sin su checkpoint (reanudar no duplica filas). |
| Fuente modificada al reanudar | Si al reabrir la fuente su ETag o size no coinciden con el job, o la fuente ya no existe (archivo/objeto borrado), termina `Failed` (no retry infinito con lease vencido). |
| Renovar el lease | `UPDATE import_job SET heartbeat_at = now WHERE id = @id AND lease_owner = @owner AND status IN ('Loading','Aggregating')`. |
| Swap de versión publicada | `UPDATE` de la fila única de `active_import` en transacción corta con `FOR UPDATE`; las lecturas del query-service son MVCC (ven la versión vieja o la nueva, nunca mixta). |
| Índices del import nuevo | `CREATE INDEX` (no CONCURRENTLY) sobre la partición de staging y los agregados del import, ANTES del swap. |
| Limpieza del import anterior | Después del swap: `DROP TABLE` de la partición de staging, `DROP INDEX` de los índices parciales y `DELETE` de los agregados (`deudor_agg`/`entidad_agg`) del import anterior (sin acumulación por import). Nunca del activo; sin `DELETE` masivo sobre el activo. |
| Aislamiento | READ COMMITTED explícito en todas las transacciones de escritura. Cero usos de SERIALIZABLE o `LOCK TABLE`. |

Detalle de decisiones: [ADR-005](docs/adr/ADR-005-reclamo-skip-locked-lease.md), [ADR-006](docs/adr/ADR-006-un-solo-import-activo-indice-parcial.md), [ADR-007](docs/adr/ADR-007-read-committed.md), [ADR-008](docs/adr/ADR-008-sin-lock-distribuido.md). Visión general: [ARCHITECTURE.md](ARCHITECTURE.md).

## Comandos

```
dotnet build Bcra.Microservices.sln
dotnet test tests/unit/Bcra.Ingestion.Domain.Tests
dotnet test tests/unit/Bcra.Ingestion.Application.Tests
dotnet test tests/unit/Bcra.Ingestion.Infrastructure.Tests
dotnet test tests/unit/Bcra.Notifier.Application.Tests
dotnet test tests/unit/Bcra.Shared.Domain.Tests
dotnet test tests/integration/Bcra.Ingestion.IntegrationTests   # requiere Docker (Testcontainers)
dotnet test tests/integration/Bcra.Query.IntegrationTests       # requiere Docker (Testcontainers)

docker compose up -d --build                                    # stack completo con LocalStack + Prometheus + Grafana
docker compose exec localstack awslocal sqs list-queues         # cola import-completed
docker compose exec localstack awslocal s3 ls                   # bucket bcra-local
curl http://localhost:18082/health/ready                        # notifier (incluye chequeo SQS)
curl http://localhost:18080/metrics                             # métricas ingestion (formato Prometheus)
curl http://localhost:18081/metrics                             # métricas query
curl http://localhost:18082/metrics                             # métricas notifier
curl http://localhost:9090/api/v1/targets                       # 3 targets en "up"

npm run setup                                                   # admin UI: stack docker + BFF (:4000) + web (:5173) + browser
npm run rebuild                                                 # rebuild+restart de los servicios .NET (o pasales nombres: "npm run rebuild ingestion")
npm test                                                        # en frontend/bff y frontend/web (vitest)
```

Al levantar el compose, `localstack/init/01-init.sh` crea el bucket `S3_BUCKET` (por defecto `bcra-local`) y la cola `import-completed` (idempotente). Las variables `SQS_IMPORT_COMPLETED_URL` y `WEBHOOK_URL` están documentadas en `.env.example`: ingestion publica ahí el `ImportCompletedEvent` al completar un import y notifier lo consume; si `WEBHOOK_URL` se configura, notifier además hace HTTP POST del evento.

### Interfaz de administración

`npm run setup` desde la raíz levanta todo (stack docker + BFF Node + web React) y abre el browser en http://localhost:5173. Tres pestañas: **Archivo** (última carga, upload con progreso en 2 fases — entrega del archivo, directa o vía S3, + procesamiento del import; `Maeent.txt`/`Nomdeu.txt` opcionales pero **por periodo**: incluilos la primera vez que se cargue un periodo o el import falla — la UI advierte si faltan — y gestión de emails de notificación), **Deudores** (grilla paginada por el backend con filtro por situación máxima; el ranking top-N también va por API con `GET /deudores/top/{n}`) y **Entidades** (grilla con filtro por código + totales). Detalle: [docs/admin-ui.md](docs/admin-ui.md).

### Notificaciones por email

Al completarse un import, notifier envía además un email con el resumen del proceso — líneas procesadas/rechazadas/registros de control, totales del import (deudores, entidades, suma total de préstamos, precomputados en `import_job` por la agregación) y duración — a **todos los destinatarios registrados** en la tabla `notification_recipient` (se gestionan desde la pestaña Archivo de la admin UI). Tabla vacía → fallback al `NOTIFICATIONS__EMAIL__TO` de `.env` (p. ej. `fede.sauzza.dev@gmail.com`). Por defecto el SMTP apunta a Mailpit del compose, que captura los emails sin enviarlos de verdad (UI en http://localhost:8025). Para envío real con Gmail: `SMTP_HOST=smtp.gmail.com`, `SMTP_PORT=587`, `SMTP_SSL=true`, `SMTP_USER=<tu@gmail.com>` y `SMTP_PASSWORD=<app-password>` (nunca la contraseña de la cuenta). El envío sigue la semántica at-least-once del notifier ([ADR-010](docs/adr/ADR-010-notifier-at-least-once-webhook.md)): si falla, el mensaje vuelve a la cola y se reintenta.

## Observabilidad

Los 3 servicios registran OpenTelemetry (runtime ASP.NET + HTTP vía helper compartido `AddObservability`, ver [ADR-009](docs/adr/ADR-009-observabilidad-opentelemetry.md)) y exponen `/metrics` en formato Prometheus; Prometheus los scrapea y Grafana trae datasource + dashboard aprovisionados (sin setup manual).

| Pieza | URL | Qué mirar |
|---|---|---|
| Prometheus | http://localhost:9090 | `/api/v1/targets` con `ingestion`, `query` y `notifier` en `up` |
| Grafana | http://localhost:3000 | Dashboard "BCRA — Pipeline de import de deudores" (carpeta BCRA; viewer anónimo, o admin/admin) |
| Métricas crudas | http://localhost:18080/metrics | Exposición Prometheus de cada servicio |

Métricas custom del pipeline (meter `Bcra.ImportPipeline`, prefijo `bcra_import_*`, solo ingestion):

| Métrica | Tipo | Uso |
|---|---|---|
| `bcra_import_lines_copied_total` / `bcra_import_lines_rejected_total` | counter | Líneas COPY-eadas a staging / rechazadas por el parser. Throughput: `rate(bcra_import_lines_copied_total[1m])` |
| `bcra_import_batches_committed_total` | counter | Lotes confirmados (COPY + checkpoint atómicos) |
| `bcra_import_phase_duration_seconds` | histogram (label `phase`) | Duración por fase: `staging`, `aggregation`, `indexes`, `swap`, `total` |
| `bcra_import_jobs_total` | counter (label `outcome`) | Imports finalizados `completed` / `failed` |
| `bcra_import_jobs_by_status` | gauge (label `status`) | Jobs por estado actual (Pending/Loading/Aggregating/Completed/Failed), refrescado por el worker |

El dashboard de Grafana muestra: import en curso (Loading/Aggregating), líneas/s (válidas y rechazadas), duración p95 por fase, jobs por estado, resultado de jobs + lotes confirmados/s, requests HTTP del query-service por ruta y el estado de los 3 targets. El correlation-id (`X-Correlation-Id`) viaja como tag `correlation_id` de cada trace, y todo log de request incluye `TraceId` (W3C) además de `CorrelationId`, permitiendo cruzar logs ↔ traces.

## Performance

Medición con el dataset real de muestra (`samples/deudores.txt`, ~5.9 GB, 34,385,567 líneas — con el parser post-fix del acote `smallint`: 34,385,282 líneas válidas procesadas y ~285 registros de control con marcador 98/99 descartados; ver [ADR-011](docs/adr/ADR-011-tuning-sin-paralelizar-import.md)), sobre Docker Desktop (VM de 15.5 GB compartida con otros stacks, Postgres 16.4) **después del tuning de bulk-load**:

| Fase | Duración | Nota |
|---|---|---|
| Staging (COPY por lotes + checkpoint) | 251.5 s | **~136,700 líneas válidas/s**, 172 lotes de 200k |
| Agregación SQL (GROUP BY de ~20M de grupos) | 298.9 s | `work_mem` sessional de 2 GB (ADR-011); el resto es piso de I/O |
| Índices pre-swap | 137.4 s | `maintenance_work_mem` 512 MB + index build paralelo (ADR-011) |
| Swap de `active_import` | 0.03 s | Transacción corta con `FOR UPDATE` |
| **End-to-end** | **~689 s (~11.5 min)** | Con defaults de Postgres: ~974 s (~16 min) — **−29 %** por el tuning |

> **Post-ADR-013** (paralelismo del GROUP BY vía CTAS con 4 workers, índices concurrentes, PG17.5 + `wal_compression=lz4` + `synchronous_commit=off` dev): medido con el dataset real **624 s (10:24)** en la segunda corrida — agregación 298.9→230.4 s, staging sin cambio (~256 s: el upload web entrega por bind mount a ~80 MB/s y LocalStack S3 tampoco es más rápido), índices +8.6 s (ruido de VM compartida). Se cierra la optimización en este punto — el detalle de palancas restantes rechazadas y del piso de ~9 min está en [ADR-013](docs/adr/ADR-013-paralelismo-ctas-tuning-infra.md).

Config usada (defaults en `.env.example` / compose): `Ingestion__BatchSize=200000` (172 lotes de 200k: tasa estable, sin presión de memoria), `MaxPoolSize=50`, `Max Auto Prepare=16` en el connection string de ingestion.

Tuning aplicado justificado por la medición ([ADR-011](docs/adr/ADR-011-tuning-sin-paralelizar-import.md), ampliado en [ADR-013](docs/adr/ADR-013-paralelismo-ctas-tuning-infra.md)):

- `CommandTimeout=0` en los comandos pesados (`AggregateImportAsync`, `CreateImportIndexesAsync`, `DropImportStorageAsync`): el default de Npgsql (30 s) es menor que las fases medidas — sin esto, el import del dataset real entraba en loop de takeover (timeout → `DbException` → lease vencido → re-agregación idempotente que volvía a expirar). La reanudación por checkpoint ya cubre fallos reales de conexión.
- Postgres fuera de defaults (compose, `postgres:17.5-alpine`): `shared_buffers=512MB`, `max_wal_size=8GB`, `checkpoint_timeout=30min`, `wal_compression=lz4`, `synchronous_commit=off` (dev), `max_parallel_maintenance_workers=4` — con defaults, checkpoints cada ~1GB de WAL en un bulk que genera varios GB, full-page images sin comprimir, fsync por cada commit de lote e index build sin memoria ni paralelismo. Venir de 16.x exige reprovisionar: `docker volume rm bcra-microservices_pgdata` (las migraciones recrean el esquema al arranque).
- `SET` sessionales en el repositorio: la agregación corre `work_mem='2GB'` + costos paralelos bajos y materializa el GROUP BY con `CREATE UNLOGGED TABLE` paralelo antes del append (el `INSERT ... SELECT` directo es serial en Postgres: benchmark −38 % en la fase, ADR-013); la creación de índices usa `maintenance_work_mem='2GB'` con los 3 `CREATE INDEX` en conexiones concurrentes. Session-scoped: mueren con la conexión al volver al pool.
- Batch size 200k y pool 50 se mantienen: la medición no muestra cuello de botella del lado .NET (el limiter es Postgres en la agregación).
- Archivo fuente grande en volumen nativo: `node scripts/stage-source.mjs <archivo>` lo copia a `/data-vol` (el bind mount de Windows sirve ~80 MB/s; el volumen, a velocidad de disco del VM) — ADR-013.

## Datos de muestra

La carpeta `samples/` está ignorada en git (el archivo principal pesa ~5 GB). El ingestion-service lee desde `DATA_DIR` (por defecto `./samples`), definido en `.env`. Para correr localmente, descargar los archivos de texto de "Información de deudores" publicados diariamente por el BCRA en [www.bcra.gob.ar](https://www.bcra.gob.ar) (sección Estadísticas → Información de deudores) y ubicarlos ahí:

```
samples/
├── deudores.txt   # deuda por CUIT y entidad (~5 GB, publicación diaria)
├── Maeent.txt     # maestro de entidades financieras
└── Nomdeu.txt     # nomenclador de deudores
```
