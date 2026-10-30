-- Contador separado para líneas descartadas deliberadamente por NO ser registros de deudor
-- con CUIT (marcador ≠ "11": control 98/99, encabezados/cierres de lote, otros tipos de
-- identificación). Antes se reportaban como "rechazadas", que sonaba a error del archivo.
-- Las existentes NO se migran: el import que las cargó ya cerró con la métrica vieja.
ALTER TABLE import_job ADD COLUMN IF NOT EXISTS lines_skipped_control bigint NOT NULL DEFAULT 0;
