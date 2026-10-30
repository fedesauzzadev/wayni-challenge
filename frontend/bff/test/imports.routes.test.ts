import request from "supertest";
import { describe, expect, it } from "vitest";
import { createHash } from "node:crypto";
import { buildTestApp } from "./helpers.js";

describe("GET /health", () => {
  it("responde ok", async () => {
    const { app } = buildTestApp();
    const response = await request(app).get("/health");
    expect(response.status).toBe(200);
    expect(response.body).toEqual({ status: "ok", service: "bcra-admin-bff" });
  });
});

describe("GET /api/imports/latest", () => {
  it("envuelve el último import del ingestion", async () => {
    const { app, upstream } = buildTestApp();
    upstream.latestImportResponse = { status: 200, body: { id: "job-1", status: "Completed", linesProcessed: 10 } };

    const response = await request(app).get("/api/imports/latest");

    expect(response.status).toBe(200);
    expect(response.body).toEqual({ latest: { id: "job-1", status: "Completed", linesProcessed: 10 } });  });

  it("mapea 404 (nunca hubo cargas) a latest null para el empty state del front", async () => {
    const { app, upstream } = buildTestApp();
    upstream.latestImportResponse = { status: 404, body: { title: "ImportJob.NoImports" } };

    const response = await request(app).get("/api/imports/latest");

    expect(response.status).toBe(200);
    expect(response.body).toEqual({ latest: null });
  });

  it("pasa tal cual los errores del upstream (ej. 503)", async () => {
    const { app, upstream } = buildTestApp();
    upstream.latestImportResponse = { status: 503, body: { title: "Query.NoActiveImport" } };

    const response = await request(app).get("/api/imports/latest");

    expect(response.status).toBe(503);
    expect(response.body).toEqual({ title: "Query.NoActiveImport" });
  });

  it("upstream caído responde 502 con mensaje", async () => {
    const { app, upstream } = buildTestApp();
    upstream.latestImport = () => Promise.reject(new TypeError("fetch failed"));

    const response = await request(app).get("/api/imports/latest");

    expect(response.status).toBe(502);
    expect(response.body.error).toContain("fetch failed");
  });
});

describe("GET /api/imports/:id", () => {
  it("proxya el estado del import", async () => {
    const { app } = buildTestApp();
    const response = await request(app).get("/api/imports/0d7abb5c-753e-45ab-957a-8a7dcf7bc680");

    expect(response.status).toBe(200);
    expect(response.body.id).toBe("0d7abb5c-753e-45ab-957a-8a7dcf7bc680");
  });
});

describe("POST /api/imports/upload", () => {
  const fileContent = "3050000000000000";

  it("sube a S3 y disparo el import con source s3://, etag sha256, size y periodo", async () => {
    const { app, upstream, s3 } = buildTestApp();

    const response = await request(app)
      .post("/api/imports/upload")
      .field("periodo", "202610")
      .attach("file", Buffer.from(fileContent, "utf-8"), "deudores.txt");

    expect(response.status).toBe(201);
    expect(response.body.importId).toBe("job-1");
    const expectedTag = createHash("sha256").update(fileContent).digest("hex").slice(0, 16);
    expect(response.body.source).toBe(`s3://bcra-local/uploads/202610/deudores-${expectedTag}.txt`);
    expect(s3.uploads).toHaveLength(1);
    expect(s3.uploads[0]?.size).toBe(fileContent.length);

    expect(upstream.requestImportCalls).toHaveLength(1);
    const payload = upstream.requestImportCalls[0];
    expect(payload?.source).toBe(response.body.source);
    expect(payload?.etag).toBe(createHash("sha256").update(fileContent).digest("hex"));
    expect(payload?.size).toBe(fileContent.length);
    expect(payload?.periodo).toBe(202610);
  });

  it("sube Maeent.txt y Nomdeu.txt opcionales al mismo prefijo del periodo (los busca el pipeline junto al source)", async () => {
    const { app, s3 } = buildTestApp();

    const response = await request(app)
      .post("/api/imports/upload")
      .field("periodo", "202610")
      .attach("maeent", Buffer.from("11ENTIDAD 11"), "Maeent.txt")
      .attach("nomdeu", Buffer.from("20000000001DEUDOR UNO"), "Nomdeu.txt")
      .attach("file", Buffer.from(fileContent, "utf-8"), "deudores.txt");

    expect(response.status).toBe(201);
    expect(s3.uploads.map((upload) => upload.key)).toEqual([
      "uploads/202610/Maeent.txt",
      "uploads/202610/Nomdeu.txt",
      expect.stringMatching(/^uploads\/202610\/deudores-/),
    ]);
  });

  it("sin archivo responde 400", async () => {
    const { app } = buildTestApp();
    const response = await request(app).post("/api/imports/upload").field("periodo", "202610");

    expect(response.status).toBe(400);
    expect(response.body.error).toBeDefined();
  });

  it("rechazo del ingestion (ej. periodo inválido) pasa tal cual", async () => {
    const { app, upstream } = buildTestApp();
    upstream.requestImportResponse = { status: 400, body: { title: "ImportJob.PeriodoInvalid" } };

    const response = await request(app)
      .post("/api/imports/upload")
      .field("periodo", "99")
      .attach("file", Buffer.from("x"), "deudores.txt");

    expect(response.status).toBe(400);
    expect(response.body).toEqual({ title: "ImportJob.PeriodoInvalid" });
  });

  it("periodo ausente usa el mes corriente", async () => {
    const { app, upstream } = buildTestApp();
    const now = new Date();

    await request(app)
      .post("/api/imports/upload")
      .attach("file", Buffer.from("x"), "deudores.txt");

    expect(upstream.requestImportCalls[0]?.periodo).toBe(now.getUTCFullYear() * 100 + now.getUTCMonth() + 1);
  });
});
