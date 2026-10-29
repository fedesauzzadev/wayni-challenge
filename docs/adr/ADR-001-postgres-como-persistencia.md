# ADR-001: PostgreSQL como única persistencia

**Estado:** Aceptado
**Contexto:** La solución necesita cargar ~35M registros crudos, agregar por deudor/entidad con sumas exactas de `numeric(15,1)`, y servir consultas de punto y ranking con baja latencia. Se evaluó también SQL Server y ClickHouse.
**Decisión:** PostgreSQL 16 (imagen fijada `postgres:16.4-alpine`).
**Motivos:**
- `numeric(15,1)` nativo: sumas exactas, nunca float (requisito del negocio).
- `COPY BINARY` de Npgsql: carga masiva de staging a cientos de miles de filas/segundo.
- Agregación como operaciones de conjunto en SQL (`GROUP BY`), con swap atómico de una sola fila (`active_import`).
- Particiones `LIST (import_id)` en staging: crear partición por job y `DROP` para limpiar es O(1) y no bloquea.
- Ecosistema maduro para Testcontainers y health checks.
**Consecuencias:** Un solo store para staging y agregados (simplifica transacciones del swap). ClickHouse sería superior para analítica a largo plazo, pero está fuera de alcance.
