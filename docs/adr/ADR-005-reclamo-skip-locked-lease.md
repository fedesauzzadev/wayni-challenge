# ADR-005: Reclamo de jobs con FOR UPDATE SKIP LOCKED + lease

- **Estado**: Aceptado
- **Contexto**: varios workers deben consumir jobs de `import_job` sin duplicar trabajo ni bloquearse entre sí. Un worker puede caer o perder la conexión a mitad de un import; su trabajo debe poder retomarse.
- **Decisión**: reclamar en una transacción READ COMMITTED corta con `SELECT ... FOR UPDATE SKIP LOCKED` (CTE candidato: `Pending`, o `Loading`/`Aggregating` con `heartbeat_at` vencido) seguida de `UPDATE` que setea `lease_owner` y `heartbeat_at`. El worker renueva el heartbeat periódicamente (`RenewLeaseAsync`, solo el dueño). Si el worker muere o pierde la conexión, deja de renovar; al vencer el lease (`heartbeat_at <= cutoff`) cualquier worker lo reclama (takeover). El procesador aborta ante `DbException` sin confirmar nada más.
- **Consecuencias**: sin colas de mensajes extra ni locks distribuidos; los workers no se bloquean entre sí (SKIP LOCKED); recuperación automática de workers caídos. Costo: el trabajo no confirmado del worker caído se re-ejecuta (idempotencia por `(source, etag, periodo)` mitiga el re-procesamiento del upload).
