# Wayni Challenge — Pipeline ELT de deudores bancarios (BCRA)

Solución de microservicios en .NET para el pipeline ELT de deudores financieros: ingestion → agregación → swap → consulta.

> Documento de presentación del repositorio: [`docs/overview.html`](docs/overview.html) (HTML autocontenido, funciona offline con doble click).

## Concurrencia y bloqueos

Toda la estrategia vive en PostgreSQL, sin locks distribuidos ni afinidad de sesión (ver `docs/adr/`).

| Situación | Mecanismo |
|---|---|
| Crear un import (upload) | `INSERT ... ON CONFLICT (source, etag, periodo) DO NOTHING` sobre índice único `uq_import_job_source_etag_periodo`; si ya existe, se devuelve el job existente (nunca "buscar y después insertar"). |
| Worker reclama un job | `SELECT ... FOR UPDATE SKIP LOCKED` + `UPDATE` en una transacción READ COMMITTED corta: Pending → Loading (o takeover de Loading/Aggregating con lease vencido), seteando `lease_owner` y `heartbeat_at`. |
| Un solo import activo | Índice único parcial `uq_import_job_single_active` sobre estados `Loading`/`Aggregating`. Colisión (23505) → el job queda Pending y se reintenta, sin fallar. |
| Worker caído / conexión perdida | Sin heartbeat, el lease vence (`heartbeat_at <= cutoff`); otro worker lo reclama vía SKIP LOCKED y retoma desde `bytes_committed`. El worker que pierde la conexión aborta sin confirmar nada más. |
| Commit de lote a staging | El COPY binario del lote y el UPDATE del checkpoint (`bytes_committed`, líneas, etag/size, heartbeat) se confirman en UNA transacción READ COMMITTED: no existe lote en staging sin su checkpoint (reanudar no duplica filas). |
| Fuente modificada al reanudar | Si al reabrir la fuente su ETag o size no coinciden con el job, termina `Failed` (no retry infinito con lease vencido). |
| Renovar el lease | `UPDATE import_job SET heartbeat_at = now WHERE id = @id AND lease_owner = @owner AND status IN ('Loading','Aggregating')`. |
| Swap de versión publicada | `UPDATE` de la fila única de `active_import` en transacción corta con `FOR UPDATE`; las lecturas del query-service son MVCC (ven la versión vieja o la nueva, nunca mixta). |
| Índices del import nuevo | `CREATE INDEX` (no CONCURRENTLY) sobre la partición de staging y los agregados del import, ANTES del swap. |
| Limpieza del import anterior | Después del swap: `DROP TABLE` de la partición de staging y `DROP INDEX` de los índices parciales del import anterior. Nunca del activo; sin `DELETE` masivo. |
| Aislamiento | READ COMMITTED explícito en todas las transacciones de escritura. Cero usos de SERIALIZABLE o `LOCK TABLE`. |

Detalle de decisiones: [ADR-005](docs/adr/ADR-005-reclamo-skip-locked-lease.md), [ADR-006](docs/adr/ADR-006-un-solo-import-activo-indice-parcial.md), [ADR-007](docs/adr/ADR-007-read-committed.md), [ADR-008](docs/adr/ADR-008-sin-lock-distribuido.md). Visión general: [ARCHITECTURE.md](ARCHITECTURE.md).

## Comandos

```
dotnet build Bcra.Microservices.sln
dotnet test tests/unit/Bcra.Ingestion.Domain.Tests
dotnet test tests/unit/Bcra.Ingestion.Application.Tests
dotnet test tests/unit/Bcra.Ingestion.Infrastructure.Tests
dotnet test tests/unit/Bcra.Notifier.Application.Tests
dotnet test tests/integration/Bcra.Ingestion.IntegrationTests   # requiere Docker (Testcontainers)

docker compose up -d --build                                    # stack completo con LocalStack + Prometheus + Grafana
docker compose exec localstack awslocal sqs list-queues         # cola import-completed
docker compose exec localstack awslocal s3 ls                   # bucket bcra-local
curl http://localhost:18082/health/ready                        # notifier (incluye chequeo SQS)
curl http://localhost:18080/metrics                             # métricas ingestion (formato Prometheus)
curl http://localhost:18081/metrics                             # métricas query
curl http://localhost:18082/metrics                             # métricas notifier
curl http://localhost:9090/api/v1/targets                       # 3 targets en "up"
```

Al levantar el compose, `localstack/init/01-init.sh` crea el bucket `S3_BUCKET` (por defecto `bcra-local`) y la cola `import-completed` (idempotente). Las variables `SQS_IMPORT_COMPLETED_URL` y `WEBHOOK_URL` están documentadas en `.env.example`: ingestion publica ahí el `ImportCompletedEvent` al completar un import y notifier lo consume; si `WEBHOOK_URL` se configura, notifier además hace HTTP POST del evento.

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

Medición con el dataset real de muestra (`samples/deudores.txt`, ~5.9 GB, 34,385,567 líneas: 19,247,633 válidas + 15,137,934 rechazadas por el parser), sobre Docker Desktop (Postgres 16.4 compartiendo CPU con otros stacks):

| Fase | Duración | Nota |
|---|---|---|
| Staging (COPY por lotes + checkpoint) | 245.5 s | **~78,400 líneas válidas/s** (~140,000 líneas totales/s), 97 lotes |
| Agregación SQL (GROUP BY) | 132.5 s | CPU-bound en Postgres |
| Índices pre-swap | 97.5 s | `CREATE INDEX` sobre staging + agregados parciales |
| Swap de `active_import` | 0.05 s | Transacción corta con `FOR UPDATE` |
| **End-to-end** | **~486 s (~8 min)** | Desde claim hasta Completed |

Config usada (defaults en `.env.example` / compose): `Ingestion__BatchSize=200000` (97 lotes de 200k: tasa estable, sin presión de memoria), `MaxPoolSize=50`, `Max Auto Prepare=16` en el connection string de ingestion.

Tuning aplicado justificado por la medición:

- `CommandTimeout=0` en los comandos pesados (`AggregateImportAsync`, `CreateImportIndexesAsync`, `DropImportStorageAsync`): el default de Npgsql (30 s) es menor que la agregación medida (132 s) — sin esto, el import del dataset real entraba en loop de takeover (timeout → `DbException` → lease vencido → re-agregación idempotente que volvía a expirar). La reanudación por checkpoint ya cubre fallos reales de conexión.
- Batch size 200k y pool 50 se mantienen: la medición no muestra cuello de botella del lado .NET (el limiter es Postgres en la agregación).

## Datos de muestra

La carpeta `samples/` está ignorada en git (el archivo principal pesa ~5 GB). El ingestion-service lee desde `DATA_DIR` (por defecto `./samples`), definido en `.env`. Para correr localmente, descargar los archivos de texto de "Información de deudores" publicados diariamente por el BCRA en [www.bcra.gob.ar](https://www.bcra.gob.ar) (sección Estadísticas → Información de deudores) y ubicarlos ahí:

```
samples/
├── deudores.txt   # deuda por CUIT y entidad (~5 GB, publicación diaria)
├── Maeent.txt     # maestro de entidades financieras
└── Nomdeu.txt     # nomenclador de deudores
```
