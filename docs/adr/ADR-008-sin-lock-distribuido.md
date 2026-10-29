# ADR-008: Sin lock distribuido

- **Estado**: Aceptado
- **Contexto**: con múltiples instancias de workers/API se podría introducir un lock distribuido (Redis, ZooKeeper, advisory locks de sesión) para coordinar el reclamo y el import activo.
- **Decisión**: no introducir lock distribuido. Toda la coordinación es de base de datos: `FOR UPDATE SKIP LOCKED` + lease con heartbeat para el reclamo (ADR-005), índice único parcial para el import activo (ADR-006) e `INSERT ... ON CONFLICT DO NOTHING` para idempotencia. No hay SQS ni cambios en los microservicios para coordinación.
- **Consecuencias**: menos piezas operativas (sin Redis/ZooKeeper, sin afinidad de sesión, compatible con connection pooling) y crash-safe por diseño. Costo: la base centraliza la contención; el lease tiene una ventana de vencimiento (`Ingestion:LeaseTimeoutSeconds`) que demora la recuperación, y la exclusión global del import activo serializa los imports (aceptable: es un requerimiento).
