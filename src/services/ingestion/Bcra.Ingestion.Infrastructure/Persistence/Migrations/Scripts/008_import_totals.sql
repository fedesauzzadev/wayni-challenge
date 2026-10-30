-- Totales del import precomputados en la misma transacción de la agregación:
-- /totales y el COUNT de la paginación del query-service pasan de escanear ~20M filas
-- (10s+ con cache frío justo después de un import) a leer una sola fila de import_job.
ALTER TABLE import_job ADD COLUMN IF NOT EXISTS deudores_count bigint NOT NULL DEFAULT 0;
ALTER TABLE import_job ADD COLUMN IF NOT EXISTS entidades_count bigint NOT NULL DEFAULT 0;
ALTER TABLE import_job ADD COLUMN IF NOT EXISTS suma_total numeric(30,1) NOT NULL DEFAULT 0;

-- Backfill one-shot: los jobs completados que aún tienen agregados (el activo) obtienen
-- sus totales reales; los jobs viejos sin agregados quedan en 0 (nunca más consultados).
UPDATE import_job j
SET deudores_count = t.deudores,
    entidades_count = t.entidades,
    suma_total = t.suma
FROM (
    SELECT d.import_id,
           COUNT(*) AS deudores,
           COALESCE(SUM(d.suma_total), 0) AS suma,
           (SELECT COUNT(*) FROM entidad_agg e WHERE e.import_id = d.import_id) AS entidades
    FROM deudor_agg d
    GROUP BY d.import_id
) t
WHERE j.id = t.import_id;
