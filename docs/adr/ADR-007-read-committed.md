# ADR-007: READ COMMITTED en todas las conexiones de escritura

- **Estado**: Aceptado
- **Contexto**: el pipeline necesita exclusión mutua (reclamo, import activo, swap) pero con alta concurrencia de lecturas en el query-service. SERIALIZABLE añade retrabajo por serialización y `LOCK TABLE` serializa de más.
- **Decisión**: todas las transacciones de escritura se abren explícitamente con `IsolationLevel.ReadCommitted` (default de PostgreSQL). La exclusión mutua necesaria se logra con mecanismos puntuales: `FOR UPDATE SKIP LOCKED` (reclamo), `FOR UPDATE` en la fila única de `active_import` (swap) e índices únicos (idempotencia e import activo). Las lecturas del query-service son MVCC sin locks: durante el swap ven la versión vieja o la nueva, nunca un estado mixto.
- **Consecuencias**: 0 usos de SERIALIZABLE o `LOCK TABLE`; menos conflictos y retrabajos; la corrección descansa en constraints + row locks acotados a transacciones cortas. Requiere que cada camino crítico tenga su mecanismo explícito (documentado en el README).
