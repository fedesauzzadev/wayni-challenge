-- Idempotencia de uploads: el mismo archivo (source, etag, periodo) no crea un nuevo import_job.
-- Las filas con etag/periodo NULL no colisionan (NULLs distintos en PostgreSQL).
CREATE UNIQUE INDEX uq_import_job_source_etag_periodo
    ON import_job (source, etag, periodo);

-- Un solo import activo a la vez: a lo sumo una fila en estado Loading/Aggregating.
-- Índice único parcial sobre expresion constante; compatible con pooling y crash-safe
-- (ver ADR-006; el lock de sesion pg_try_advisory_lock fue descartado).
CREATE UNIQUE INDEX uq_import_job_single_active
    ON import_job ((1))
    WHERE status IN ('Loading', 'Aggregating');
