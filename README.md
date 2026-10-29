# Wayni Challenge — Pipeline ELT de deudores bancarios (BCRA)

Solución de microservicios en .NET para el pipeline ELT de deudores financieros: ingestion → agregación → swap → consulta.

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

docker compose up -d --build                                    # stack completo con LocalStack
docker compose exec localstack awslocal sqs list-queues         # cola import-completed
docker compose exec localstack awslocal s3 ls                   # bucket bcra-local
curl http://localhost:18082/health/ready                        # notifier (incluye chequeo SQS)
```

Al levantar el compose, `localstack/init/01-init.sh` crea el bucket `S3_BUCKET` (por defecto `bcra-local`) y la cola `import-completed` (idempotente). Las variables `SQS_IMPORT_COMPLETED_URL` y `WEBHOOK_URL` están documentadas en `.env.example`: ingestion publica ahí el `ImportCompletedEvent` al completar un import y notifier lo consume; si `WEBHOOK_URL` se configura, notifier además hace HTTP POST del evento.

## Datos de muestra

La carpeta `samples/` está ignorada en git (el archivo principal pesa ~5 GB). El ingestion-service lee desde `DATA_DIR` (por defecto `./samples`), definido en `.env`. Para correr localmente, descargar los archivos de texto de "Información de deudores" publicados diariamente por el BCRA en [www.bcra.gob.ar](https://www.bcra.gob.ar) (sección Estadísticas → Información de deudores) y ubicarlos ahí:

```
samples/
├── deudores.txt   # deuda por CUIT y entidad (~5 GB, publicación diaria)
├── Maeent.txt     # maestro de entidades financieras
└── Nomdeu.txt     # nomenclador de deudores
```
