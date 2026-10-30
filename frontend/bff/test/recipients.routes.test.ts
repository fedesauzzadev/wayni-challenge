import request from "supertest";
import { describe, expect, it } from "vitest";
import { buildTestApp } from "./helpers.js";

describe("GET /api/notification-recipients", () => {
  it("lista los destinatarios registrados", async () => {
    const { app } = buildTestApp();

    const response = await request(app).get("/api/notification-recipients");

    expect(response.status).toBe(200);
    expect(response.body.recipients).toHaveLength(1);
    expect(response.body.recipients[0]).toMatchObject({ id: 1, email: "ops@example.com" });
  });

  it("error de BD responde 500 con mensaje", async () => {
    const { app, db } = buildTestApp();
    db.error = new Error("relation \"notification_recipient\" does not exist");

    const response = await request(app).get("/api/notification-recipients");

    expect(response.status).toBe(500);
    expect(response.body.error).toContain("notification_recipient");
  });
});

describe("POST /api/notification-recipients", () => {
  it("alta valida responde 201 con el destinatario", async () => {
    const { app, db } = buildTestApp();

    const response = await request(app)
      .post("/api/notification-recipients")
      .send({ email: "Nuevo@Example.com" });

    expect(response.status).toBe(201);
    expect(response.body.recipient.email).toBe("nuevo@example.com");
    expect(response.body.alreadyExisted).toBe(false);
    expect(db.recipients).toHaveLength(2);
  });

  it("duplicado responde 200 alreadyExisted", async () => {
    const { app } = buildTestApp();

    const response = await request(app)
      .post("/api/notification-recipients")
      .send({ email: "ops@example.com" });

    expect(response.status).toBe(200);
    expect(response.body.alreadyExisted).toBe(true);
  });

  it("email inválido responde 400", async () => {
    const { app } = buildTestApp();

    for (const email of ["no-es-mail", "a@b", "", undefined]) {
      const response = await request(app).post("/api/notification-recipients").send({ email });
      expect(response.status).toBe(400, `email '${email}' debe rechazarse`);
    }
  });
});

describe("DELETE /api/notification-recipients/:id", () => {
  it("baja existente responde 204", async () => {
    const { app } = buildTestApp();

    const response = await request(app).delete("/api/notification-recipients/1");

    expect(response.status).toBe(204);
  });

  it("id inexistente responde 404", async () => {
    const { app } = buildTestApp();

    const response = await request(app).delete("/api/notification-recipients/999");

    expect(response.status).toBe(404);
  });

  it("id inválido responde 400", async () => {
    const { app } = buildTestApp();

    const response = await request(app).delete("/api/notification-recipients/abc");

    expect(response.status).toBe(400);
  });
});
