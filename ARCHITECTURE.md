# Arquitectura

## Visión general

Pipeline ELT de deudores bancarios con 3 microservicios (.NET 10, minimal APIs) y PostgreSQL:

```
[SQS / LocalStack] --> [ingestion]  staging -> agregación -> swap -> [DROP anterior]
                              |                        |
                              v                        v
                        PostgreSQL  <--- fila única active_import --->  [query] (rol readonly, MVCC)
                              |
                              v
                         [notifier] (notificación de imports completados)
```

- **Bcra.Ingestion** — acepta uploads idempotentes (`POST /imports`), orquesta la ejecución del import bajo lease (`ImportJobProcessor`): reclamo SKIP LOCKED → staging → agregación → índices pre-swap → swap atómico → DROP del import anterior. Migraciones DbUp (`Scripts/00x_*.sql`).
- **Bcra.Query** — lee la versión publicada resolviendo `active_import`; conexiones de solo lectura sin locks (MVCC).
- **Bcra.Notifier** — notifica fin de import.

## Flujo de un import (concurrencia)

1. **Upload idempotente**: `POST /imports` hace `INSERT ... ON CONFLICT (source, etag, periodo) DO NOTHING`; duplicado → devuelve el job existente con `alreadyExisted=true` (201 nuevo / 200 existente).
2. **Reclamo**: `DapperImportJobRepository.ClaimNextAsync` selecciona el job más antiguo elegible (Pending, o Loading/Aggregating con lease vencido) con `FOR UPDATE SKIP LOCKED` y lo reclama (`lease_owner`, `heartbeat_at`). Si la activación choca con el índice único parcial de import activo, devuelve null: el job queda Pending y se reintenta.
3. **Ejecución**: `ImportJobProcessor` crea la partición de staging del import, agrega, renueva lease, crea índices (`CREATE INDEX`, antes del swap), swapea `active_import` (transacción corta) y marca Completed. Pérdida de conexión (`DbException`) → abort inmediato sin confirmar nada; el lease vence y otro worker reclama.
4. **Limpieza**: post-swap, `DROP` de la partición de staging e índices del import anterior (nunca del activo, sin `DELETE` masivo).

## Decisiones (ADR)

- [ADR-005 — Reclamo con FOR UPDATE SKIP LOCKED + lease](docs/adr/ADR-005-reclamo-skip-locked-lease.md)
- [ADR-006 — Un solo import activo con índice único parcial](docs/adr/ADR-006-un-solo-import-activo-indice-parcial.md)
- [ADR-007 — READ COMMITTED en todas las escrituras](docs/adr/ADR-007-read-committed.md)
- [ADR-008 — Sin lock distribuido](docs/adr/ADR-008-sin-lock-distribuido.md)

## Tests

- `tests/unit/Bcra.Ingestion.Domain.Tests` — ciclo de vida del job, reclamo y decisión de lease vencido con reloj inyectable (`TimeProvider`).
- `tests/integration/Bcra.Ingestion.IntegrationTests` — Testcontainers + PostgreSQL real: (a) dos workers, un ganador; (b) uploads simultáneos → un solo job y una sola ejecución; (c) segundo import queda Pending mientras hay uno activo; (d) lecturas concurrentes al swap ven versión vieja o nueva, nunca mixta.
