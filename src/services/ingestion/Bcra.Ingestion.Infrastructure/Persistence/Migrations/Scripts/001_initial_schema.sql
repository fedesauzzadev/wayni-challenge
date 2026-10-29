CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE import_job (
    id                uuid PRIMARY KEY,
    source            text NOT NULL,
    etag              text,
    size              bigint NOT NULL DEFAULT 0,
    periodo           integer,
    status            text NOT NULL
                      CONSTRAINT ck_import_job_status
                      CHECK (status IN ('Pending', 'Loading', 'Aggregating', 'Completed', 'Failed')),
    bytes_committed   bigint NOT NULL DEFAULT 0,
    lines_processed   bigint NOT NULL DEFAULT 0,
    lines_rejected    bigint NOT NULL DEFAULT 0,
    lease_owner       text,
    heartbeat_at      timestamptz,
    created_at        timestamptz NOT NULL,
    started_at        timestamptz,
    finished_at       timestamptz
);

CREATE INDEX ix_import_job_status_created ON import_job (status, created_at DESC);

CREATE TABLE debt_staging (
    import_id   uuid        NOT NULL,
    entidad     smallint    NOT NULL,
    cuit        char(11)    NOT NULL,
    situacion   smallint    NOT NULL,
    monto       numeric(15,1) NOT NULL
) PARTITION BY LIST (import_id);

CREATE TABLE deudor_agg (
    import_id         uuid          NOT NULL,
    cuit              char(11)      NOT NULL,
    situacion_maxima  smallint      NOT NULL,
    suma_total        numeric(15,1) NOT NULL
);

CREATE TABLE entidad_agg (
    import_id       uuid          NOT NULL,
    codigo_entidad  smallint      NOT NULL,
    suma_total      numeric(15,1) NOT NULL
);

CREATE TABLE active_import (
    id         smallint PRIMARY KEY CHECK (id = 1),
    import_id  uuid REFERENCES import_job (id) ON DELETE SET NULL
);

INSERT INTO active_import (id, import_id) VALUES (1, NULL) ON CONFLICT (id) DO NOTHING;

CREATE TABLE entidad (
    codigo  smallint PRIMARY KEY,
    nombre  text NOT NULL
);

CREATE TABLE deudor_nombre (
    cuit    char(11) PRIMARY KEY,
    nombre  text NOT NULL
);
