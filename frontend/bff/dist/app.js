import express from "express";
import multer from "multer";
import { mkdir, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { buildReferenceKey, buildUploadKey, sha256File } from "./s3.js";
const EmailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
/** Periodo por defecto: mes corriente en formato yyyyMM. */
export function defaultPeriodo(now = new Date()) {
    return now.getUTCFullYear() * 100 + now.getUTCMonth() + 1;
}
export function parsePeriodo(value, now = new Date()) {
    return typeof value === "string" && /^\d{6}$/.test(value) ? Number(value) : defaultPeriodo(now);
}
export function isValidEmail(value) {
    return typeof value === "string" && EmailRegex.test(value.trim());
}
export function createApp(deps) {
    const app = express();
    app.use(express.json());
    const uploadDir = path.join(os.tmpdir(), "bcra-bff-uploads");
    const upload = multer({
        storage: multer.diskStorage({
            destination: async (_req, _file, cb) => {
                await mkdir(uploadDir, { recursive: true });
                cb(null, uploadDir);
            },
            filename: (_req, file, cb) => cb(null, `${Date.now()}-${Math.random().toString(36).slice(2)}-${file.originalname}`),
        }),
    });
    // file = deudores; maeent/nomdeu opcionales (referencias que el pipeline busca junto al source).
    const uploadFields = upload.fields([
        { name: "file", maxCount: 1 },
        { name: "maeent", maxCount: 1 },
        { name: "nomdeu", maxCount: 1 },
    ]);
    app.get("/health", (_req, res) => {
        res.json({ status: "ok", service: "bcra-admin-bff" });
    });
    app.get("/api/imports/latest", async (_req, res, next) => {
        try {
            const upstream = await deps.upstream.latestImport();
            // 404 = nunca hubo cargas: el front muestra el empty state en vez de un error.
            if (upstream.status === 404) {
                res.json({ latest: null });
                return;
            }
            if (upstream.status === 200) {
                res.json({ latest: upstream.body });
                return;
            }
            sendUpstream(res, upstream);
        }
        catch (error) {
            next(error);
        }
    });
    app.get("/api/imports/:id", async (req, res, next) => {
        try {
            sendUpstream(res, await deps.upstream.importStatus(req.params.id));
        }
        catch (error) {
            next(error);
        }
    });
    /**
     * Upload en pasos desde el punto de vista del pipeline: (1) referencias opcionales
     * (Maeent.txt/Nomdeu.txt) y el archivo de deudores → S3 (LocalStack) o al dir local
     * compartido con el ingestion (DATA_DIR), con keys por periodo — el ingestion resuelve
     * las referencias en el mismo prefijo del source; (2) POST /imports al ingestion con el
     * source (s3:// o /data/...) + etag sha256 (idempotencia por source+etag+periodo del
     * backend). storage: "s3" (default, retrocompatible) o "local" (sin pasar por S3).
     */
    app.post("/api/imports/upload", uploadFields, async (req, res, next) => {
        const files = req.files;
        const file = files?.file?.[0];
        if (!file) {
            res.status(400).json({ error: "Campo 'file' (multipart) es requerido." });
            return;
        }
        try {
            const periodo = parsePeriodo(req.body.periodo);
            const store = req.body.storage === "local" ? deps.localUploader : deps.s3Uploader;
            const references = [
                ["maeent", "Maeent.txt"],
                ["nomdeu", "Nomdeu.txt"],
            ];
            for (const [fieldName, fileName] of references) {
                const reference = files?.[fieldName]?.[0];
                if (reference) {
                    await store(reference.path, reference.size, buildReferenceKey(periodo, fileName));
                    await rm(reference.path, { force: true }).catch(() => { });
                }
            }
            const etag = await sha256File(file.path);
            const uploaded = await store(file.path, file.size, buildUploadKey(periodo, etag));
            const importResult = await deps.upstream.requestImport({
                source: uploaded.source,
                etag,
                size: file.size,
                periodo,
            });
            if (importResult.status >= 400) {
                sendUpstream(res, importResult);
                return;
            }
            const body = importResult.body;
            res.status(importResult.status).json({
                importId: body?.id ?? null,
                status: body?.status ?? null,
                alreadyExisted: body?.alreadyExisted ?? false,
                source: uploaded.source,
            });
        }
        catch (error) {
            next(error);
        }
        finally {
            await rm(file.path, { force: true }).catch(() => { });
        }
    });
    app.get("/api/deudores", async (req, res, next) => {
        try {
            const query = new URLSearchParams();
            for (const key of ["offset", "limit", "top", "omitirSinNombre", "situacionMaxima"]) {
                const value = req.query[key];
                if (typeof value === "string" && value !== "") {
                    query.set(key, value);
                }
            }
            sendUpstream(res, await deps.upstream.deudores(query));
        }
        catch (error) {
            next(error);
        }
    });
    app.get("/api/entidades", async (req, res, next) => {
        try {
            const query = new URLSearchParams();
            const codigo = req.query.codigo;
            if (typeof codigo === "string" && codigo.trim() !== "") {
                query.set("codigo", codigo.trim());
            }
            sendUpstream(res, await deps.upstream.entidades(query));
        }
        catch (error) {
            next(error);
        }
    });
    app.get("/api/totales", async (_req, res, next) => {
        try {
            sendUpstream(res, await deps.upstream.totales());
        }
        catch (error) {
            next(error);
        }
    });
    app.get("/api/notification-recipients", async (_req, res, next) => {
        try {
            res.json({ recipients: await deps.db.list() });
        }
        catch (error) {
            next(error);
        }
    });
    app.post("/api/notification-recipients", async (req, res, next) => {
        try {
            const email = req.body?.email;
            if (!isValidEmail(email)) {
                res.status(400).json({ error: "Email inválido." });
                return;
            }
            const result = await deps.db.add(email.trim().toLowerCase());
            res.status(result.alreadyExisted ? 200 : 201).json(result);
        }
        catch (error) {
            next(error);
        }
    });
    app.delete("/api/notification-recipients/:id", async (req, res, next) => {
        try {
            const id = Number(req.params.id);
            if (!Number.isInteger(id) || id < 1) {
                res.status(400).json({ error: "Id inválido." });
                return;
            }
            const removed = await deps.db.remove(id);
            res.status(removed ? 204 : 404).send();
        }
        catch (error) {
            next(error);
        }
    });
    app.use((_req, res) => {
        res.status(404).json({ error: "Not found" });
    });
    app.use((error, _req, res, _next) => {
        const message = error instanceof Error ? error.message : "internal error";
        const upstream = error instanceof Error && error.name === "TypeError";
        res.status(upstream ? 502 : 500).json({
            error: upstream ? `Bad Gateway: ${message}` : message,
        });
    });
    return app;
}
function sendUpstream(res, upstream) {
    res.status(upstream.status).json(upstream.body);
}
