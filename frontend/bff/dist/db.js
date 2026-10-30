import pg from "pg";
/** CRUD de la tabla `notification_recipient` (creada por las migraciones DbUp de ingestion). */
export function createRecipientStore(config) {
    const pool = new pg.Pool({
        host: config.postgres.host,
        port: config.postgres.port,
        database: config.postgres.database,
        user: config.postgres.user,
        password: config.postgres.password,
        max: 5,
    });
    const toRecipient = (row) => ({
        id: Number(row.id),
        email: row.email,
        createdAt: new Date(row.created_at).toISOString(),
    });
    return {
        async list() {
            const result = await pool.query("SELECT id, email, created_at FROM notification_recipient ORDER BY id");
            return result.rows.map(toRecipient);
        },
        async add(email) {
            const inserted = await pool.query(`INSERT INTO notification_recipient (email) VALUES ($1)
         ON CONFLICT (email) DO NOTHING
         RETURNING id, email, created_at`, [email]);
            if (inserted.rows[0]) {
                return { recipient: toRecipient(inserted.rows[0]), alreadyExisted: false };
            }
            const existing = await pool.query("SELECT id, email, created_at FROM notification_recipient WHERE email = $1", [email]);
            return { recipient: toRecipient(existing.rows[0]), alreadyExisted: true };
        },
        async remove(id) {
            const result = await pool.query("DELETE FROM notification_recipient WHERE id = $1 RETURNING id", [id]);
            return result.rows.length > 0;
        },
    };
}
