# Arquitectura

## Visión general

Pipeline ELT de deudores bancarios con 3 microservicios (.NET 10, minimal APIs) y PostgreSQL:

```
[upload] --> [ingestion]  staging -> agregación -> swap -> [DROP anterior]
                  |                        |
                  |                        +-- ImportCompletedEvent --> [SQS import-completed] --> [notifier] (log + webhook opcional + email)
                  v                        v
            PostgreSQL  <--- fila única active_import --->  [query] (rol readonly, MVCC)
                  |
                  v
             fuentes: /data (local, allow-list de dirs) o s3://bucket/key (LocalStack/S3, allow-list de buckets)

            [prometheus] --scrape /metrics--> [ingestion|query|notifier]   [grafana] <-- datasource+dashboard provisionados

            [web React :5173] --/api--> [BFF Express :4000] --> ingestion/query (HTTP) + S3 (upload) + Postgres (notification_recipient)
```

- **Bcra.Ingestion** — acepta uploads idempotentes (`POST /imports`), orquesta la ejecución del import bajo lease (`ImportJobProcessor`): reclamo SKIP LOCKED → staging → agregación → índices pre-swap → swap atómico → DROP del import anterior. Migraciones DbUp (`Scripts/00x_*.sql`). Las fuentes se rutean por esquema (`RoutingFileSourceFactory`): `s3://bucket/key` → `S3FileSource` (rango `bytes=offset-` para reanudar, ETag real, buckets restringidos a `Ingestion:AllowedS3Buckets`), el resto → `LocalFileSource`. Al completar publica `ImportCompletedEvent` en SQS de forma best-effort (el fallo del publish se loguea, no falla el import).
- **Bcra.Query** — lee la versión publicada resolviendo `active_import`; conexiones de solo lectura sin locks (MVCC).
- **Bcra.Notifier** — consume `import-completed` con long-poll (`SqsImportCompletedConsumer`), notifica vía log estructurado siempre, webhook HTTP si `Notifications:WebhookUrl` está configurado y email con las métricas del evento (`EmailNotifier` vía MailKit: destinatarios de la tabla `notification_recipient` — notificador lee con rol readonly — con fallback a `Notifications:Email__To`; ver README "Notificaciones por email"), y borra el mensaje solo si la notificación fue OK (si falla, vuelve por visibilidad). Readiness incluye chequeo de SQS.
- **Admin UI** (`frontend/`) — web React + BFF Node (ver [docs/admin-ui.md](docs/admin-ui.md)): proxy fino del BFF sobre ingestion/query, sube los archivos (directo al `DATA_DIR` compartido o a S3) antes de `POST /imports` (progreso en 2 fases) y gestiona los destinatarios de email en `notification_recipient` (tabla creada por las migraciones de ingestion; notifier lee con rol readonly, el BFF escribe con rol owner; tabla vacía → fallback a `Notifications:Email__To`). Secretos (`UPLOAD_API_KEY`, creds AWS, DB) solo en el BFF. `npm run setup` levanta todo.

**Observabilidad** ([ADR-009](docs/adr/ADR-009-observabilidad-opentelemetry.md)): los 3 servicios registran OpenTelemetry (helper compartido `AddObservability` en `Bcra.Shared.Infrastructure/Observability/`): traces HTTP de ASP.NET Core, métricas de runtime y las métricas custom del pipeline (`ImportPipelineMeter`, meter `Bcra.ImportPipeline`: líneas COPY-eadas, lotes, duración por fase, jobs por estado/resultado), exportadas a `/metrics` en formato Prometheus. El correlation-id viaja como tag del trace y el `TraceId` se enriquece en los logs de Serilog. El docker-compose agrega Prometheus (scrapea los 3) y Grafana con datasource + dashboard del pipeline aprovisionados.

## Flujo de un import (concurrencia)

1. **Upload idempotente**: `POST /imports` hace `INSERT ... ON CONFLICT (source, etag, periodo) DO NOTHING`; duplicado → devuelve el job existente con `alreadyExisted=true` (201 nuevo / 200 existente).
2. **Reclamo**: `DapperImportJobRepository.ClaimNextAsync` selecciona el job más antiguo elegible (Pending, o Loading/Aggregating con lease vencido) con `FOR UPDATE SKIP LOCKED` y lo reclama (`lease_owner`, `heartbeat_at`). Si la activación choca con el índice único parcial de import activo, devuelve null: el job queda Pending y se reintenta.
3. **Ejecución**: `ImportJobProcessor` crea la partición de staging del import, agrega, renueva lease, crea índices (`CREATE INDEX`, antes del swap), swapea `active_import` (transacción corta) y marca Completed. Cada lote confirma COPY a staging + checkpoint (`bytes_committed`, líneas, heartbeat) en UNA transacción READ COMMITTED (`CommitBatchAsync`): un worker caído se reanuda desde el checkpoint sin duplicar filas, y la re-agregación es idempotente (DELETE previo por `import_id`). Pérdida de conexión (`DbException`) → abort inmediato sin confirmar nada; el lease vence y otro worker reclama. Si al reanudar la fuente cambió (ETag/size), el job termina `Failed` en vez de reintentar para siempre.
4. **Notificación**: post-Completed, el processor publica `ImportCompletedEvent` (JobId, líneas, agregados y duración) en la cola SQS `import-completed`; el publish es best-effort y no afecta el resultado del import. notifier lo consume con long-poll (20s), notifica (log estructurado + webhook opcional) y borra el mensaje solo si la notificación fue OK; si falla, el mensaje vuelve por timeout de visibilidad. Mensajes malformados se descartan.
5. **Limpieza**: post-swap, `DROP` de la partición de staging e índices del import anterior (nunca del activo, sin `DELETE` masivo).

## Decisiones (ADR)

- [ADR-001 — PostgreSQL como persistencia](docs/adr/ADR-001-postgres-como-persistencia.md)
- [ADR-002 — ELT: staging en Postgres y agregación en SQL](docs/adr/ADR-002-elt-staging-then-sql.md)
- [ADR-003 — Situación máxima con `max` literal](docs/adr/ADR-003-situacion-maximo-literal.md)
- [ADR-004 — Base de datos compartida entre servicios](docs/adr/ADR-004-base-compartida.md)
- [ADR-005 — Reclamo con FOR UPDATE SKIP LOCKED + lease](docs/adr/ADR-005-reclamo-skip-locked-lease.md)
- [ADR-006 — Un solo import activo con índice único parcial](docs/adr/ADR-006-un-solo-import-activo-indice-parcial.md)
- [ADR-007 — READ COMMITTED en todas las escrituras](docs/adr/ADR-007-read-committed.md)
- [ADR-008 — Sin lock distribuido](docs/adr/ADR-008-sin-lock-distribuido.md)
- [ADR-009 — Observabilidad con OpenTelemetry](docs/adr/ADR-009-observabilidad-opentelemetry.md)
- [ADR-010 — Notifier at-least-once con Idempotency-Key declarativa](docs/adr/ADR-010-notifier-at-least-once-webhook.md)
- [ADR-011 — Tuning de configuración sin paralelizar el pipeline](docs/adr/ADR-011-tuning-sin-paralelizar-import.md)

## Tests

- `tests/unit/Bcra.Ingestion.Domain.Tests` — ciclo de vida del job, reclamo y decisión de lease vencido con reloj inyectable (`TimeProvider`).
- `tests/unit/Bcra.Ingestion.Application.Tests` — checkpoint por lote y offset de reanudación de `StagingCopyLoader` (commit atómico lote+checkpoint, fuente modificada).
- `tests/unit/Bcra.Ingestion.Infrastructure.Tests` — ruteo `s3://` con allow-list de buckets, offset/rango y ETag de `S3FileSource`, y publicación SQS del evento.
- `tests/unit/Bcra.Shared.Domain.Tests` — contratos compartidos (`Result`, serialización del `ImportCompletedEvent`).
- `tests/unit/Bcra.Notifier.Application.Tests` — manejo del mensaje (parseo, delete condicional, abandono por fallo), `WebhookNotifier` e `EmailNotifier` (destinatarios, métricas del evento, fallo SMTP).
- `tests/integration/Bcra.Ingestion.IntegrationTests` — Testcontainers + PostgreSQL real: (a) dos workers, un ganador; (b) uploads simultáneos → un solo job y una sola ejecución; (c) segundo import queda Pending mientras hay uno activo; (d) lecturas concurrentes al swap ven versión vieja o nueva, nunca mixta; (e) import completado publica `ImportCompletedEvent` con agregados y el fallo del publish no falla el import; (f) reanudación: takeover de Loading tras lease vencido completa sin duplicados, takeover de Aggregating re-agrega idempotente, fuente modificada o borrada → Failed.
- `tests/integration/Bcra.Query.IntegrationTests` — Testcontainers + PostgreSQL real: endpoints de consulta (por CUIT, top, entidades, totales) contra el import activo, paginación/`TopMax` y filtro por código de entidad.
