-- Destinatarios de email de "archivo cargado", gestionados por el BFF del admin UI.
-- La crean las migraciones de ingestion (único dueño del schema); notifier lee (rol
-- bcra_readonly) y el BFF escribe con el rol owner desde el host.

CREATE TABLE notification_recipient (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email       text NOT NULL UNIQUE,
    created_at  timestamptz NOT NULL DEFAULT now()
);

GRANT SELECT ON notification_recipient TO bcra_readonly;
