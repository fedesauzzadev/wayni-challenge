import request from "supertest";
import { describe, expect, it } from "vitest";
import { buildTestApp } from "./helpers.js";

describe("GET /api/deudores", () => {
  it("forwards offset/limit y devuelve la página del query-service", async () => {
    const { app, upstream } = buildTestApp();
    upstream.deudoresResponse = {
      status: 200,
      body: { total: 120, offset: 50, limit: 50, items: [{ cuit: "20123456784" }] },
    };

    const response = await request(app).get("/api/deudores?offset=50&limit=50");

    expect(response.status).toBe(200);
    expect(response.body.total).toBe(120);
    expect(upstream.deudoresQueries).toEqual(["offset=50&limit=50"]);
  });

  it("ignora parámetros desconocidos", async () => {
    const { app, upstream } = buildTestApp();

    await request(app).get("/api/deudores?limit=10&bogus=1");

    expect(upstream.deudoresQueries).toEqual(["limit=10"]);
  });

  it("pasa tal cual el 503 sin import activo", async () => {
    const { app, upstream } = buildTestApp();
    upstream.deudoresResponse = { status: 503, body: { title: "Query.NoActiveImport" } };

    const response = await request(app).get("/api/deudores");

    expect(response.status).toBe(503);
    expect(response.body).toEqual({ title: "Query.NoActiveImport" });
  });
});

describe("GET /api/entidades", () => {
  it("sin filtro no manda codigo", async () => {
    const { app, upstream } = buildTestApp();

    await request(app).get("/api/entidades");

    expect(upstream.entidadesQueries).toEqual([""]);
  });

  it("forwards el filtro por codigo", async () => {
    const { app, upstream } = buildTestApp();
    upstream.entidadesResponse = {
      status: 200,
      body: { total: 1, items: [{ codigo: 266, nombre: "BANCO DE LA NACION ARGENTINA" }] },
    };

    const response = await request(app).get("/api/entidades?codigo=266");

    expect(response.status).toBe(200);
    expect(response.body.items[0]).toEqual({ codigo: 266, nombre: "BANCO DE LA NACION ARGENTINA" });
    expect(upstream.entidadesQueries).toEqual(["codigo=266"]);
  });
});

describe("GET /api/totales", () => {
  it("proxya los totales", async () => {
    const { app, upstream } = buildTestApp();
    upstream.totalesResponse = {
      status: 200,
      body: { cantidadDeudores: 19247633, cantidadEntidades: 300, sumaTotalPrestamos: 12345.6 },
    };

    const response = await request(app).get("/api/totales");

    expect(response.status).toBe(200);
    expect(response.body.cantidadEntidades).toBe(300);
  });
});
