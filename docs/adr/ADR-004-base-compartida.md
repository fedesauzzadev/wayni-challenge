# ADR-004: Base de datos compartida entre ingestion y query

**Estado:** Aceptado
**Contexto:** Tres microservicios independientes. Opciones: (a) base por servicio con replicación, (b) una única instancia PostgreSQL compartida, (c) dumps exportados por servicio.
**Decisión:** Una única instancia PostgreSQL; `ingestion-service` es el ÚNICO escritor (rol propietario); `query-service` conecta con rol `bcra_readonly` (solo SELECT, sin login de escritura); `notifier-service` NO accede a la base (solo consume SQS).
**Motivos:**
- El swap atómico de `active_import` solo es barato si escritor y lector ven la misma base: el lector cambia de versión en un UPDATE, sin replicación ni consistencia eventual.
- CQRS en la misma base: tablas de agregado escritas por ingestion y leídas por query — la separación es de roles y modelos, no de storage.
- Evita infraestructura de replicación fuera de alcance.
**Consecuencias:** Acoplamiento físico del storage (aceptado y explícito). La separación lógica queda protegida por roles de DB: `bcra_readonly` no puede escribir aunque el código lo intente. Migrar a bases separadas con replicación lógica es un paso futuro documentado en el README.
