DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'bcra_readonly') THEN
        CREATE ROLE bcra_readonly NOLOGIN;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO bcra_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO bcra_readonly;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO bcra_readonly;
