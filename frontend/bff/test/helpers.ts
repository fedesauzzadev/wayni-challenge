import type { AppDeps, } from "../src/app.js";
import { createApp } from "../src/app.js";
import type { AppConfig } from "../src/config.js";
import type { Recipient, RecipientStore } from "../src/db.js";
import type { LocalUploadResult } from "../src/local.js";
import type { S3UploadResult } from "../src/s3.js";
import type { RequestImportPayload, UpstreamClient, UpstreamResponse } from "../src/upstream.js";

export const testConfig: AppConfig = {
  port: 0,
  ingestionApiUrl: "http://ingestion.test",
  queryApiUrl: "http://query.test",
  uploadApiKey: "test-key",
  s3: {
    endpoint: "http://s3.test",
    region: "us-east-1",
    bucket: "bcra-local",
    accessKeyId: "test",
    secretAccessKey: "test",
  },
  localUpload: {
    dataDir: "./samples",
    ingestionSourceRoot: "/data",
  },
  postgres: {
    host: "localhost",
    port: 5544,
    database: "bcra",
    user: "bcra",
    password: "test",
  },
};

export class FakeUpstream implements UpstreamClient {
  latestImportResponse: UpstreamResponse = { status: 200, body: { id: "job-1", status: "Completed" } };
  importStatusResponse: UpstreamResponse = { status: 200, body: { id: "job-1", status: "Loading" } };
  requestImportResponse: UpstreamResponse = { status: 201, body: { id: "job-1", status: "Pending", alreadyExisted: false } };
  deudoresResponse: UpstreamResponse = { status: 200, body: { total: 0, items: [] } };
  entidadesResponse: UpstreamResponse = { status: 200, body: { total: 0, items: [] } };
  totalesResponse: UpstreamResponse = { status: 200, body: { cantidadDeudores: 0 } };

  requestImportCalls: RequestImportPayload[] = [];
  deudoresQueries: string[] = [];
  entidadesQueries: string[] = [];

  latestImport() {
    return Promise.resolve(this.latestImportResponse);
  }

  importStatus(id: string) {
    return Promise.resolve({ ...this.importStatusResponse, body: { ...((this.importStatusResponse.body ?? {}) as object), id } });
  }

  requestImport(payload: RequestImportPayload) {
    this.requestImportCalls.push(payload);
    return Promise.resolve(this.requestImportResponse);
  }

  deudores(query: URLSearchParams) {
    this.deudoresQueries.push(query.toString());
    return Promise.resolve(this.deudoresResponse);
  }

  entidades(query: URLSearchParams) {
    this.entidadesQueries.push(query.toString());
    return Promise.resolve(this.entidadesResponse);
  }

  totales() {
    return Promise.resolve(this.totalesResponse);
  }
}

export class FakeRecipientStore implements RecipientStore {
  recipients: Recipient[] = [
    { id: 1, email: "ops@example.com", createdAt: "2026-10-01T00:00:00.000Z" },
  ];
  error: Error | null = null;

  private nextId = 2;

  async list() {
    if (this.error) throw this.error;
    return [...this.recipients];
  }

  async add(email: string) {
    if (this.error) throw this.error;
    const existing = this.recipients.find((r) => r.email === email);
    if (existing) {
      return { recipient: existing, alreadyExisted: true };
    }

    const recipient: Recipient = { id: this.nextId++, email, createdAt: new Date().toISOString() };
    this.recipients.push(recipient);
    return { recipient, alreadyExisted: false };
  }

  async remove(id: number) {
    if (this.error) throw this.error;
    const before = this.recipients.length;
    this.recipients = this.recipients.filter((r) => r.id !== id);
    return this.recipients.length < before;
  }
}

export class FakeS3Uploader {
  uploads: { key: string; size: number }[] = [];

  async upload(_filePath: string, size: number, key: string): Promise<S3UploadResult> {
    this.uploads.push({ key, size });
    return { bucket: "bcra-local", key, source: `s3://bcra-local/${key}` };
  }
}

export class FakeLocalUploader {
  uploads: { key: string; size: number }[] = [];

  async upload(_filePath: string, size: number, key: string): Promise<LocalUploadResult> {
    this.uploads.push({ key, size });
    return { source: `/data/${key}` };
  }
}

export function buildTestApp(overrides: Partial<AppDeps> = {}) {
  const upstream = new FakeUpstream();
  const db = new FakeRecipientStore();
  const s3 = new FakeS3Uploader();
  const local = new FakeLocalUploader();
  const app = createApp({
    config: testConfig,
    upstream,
    db,
    s3Uploader: (filePath, size, key) => s3.upload(filePath, size, key),
    localUploader: (filePath, size, key) => local.upload(filePath, size, key),
    ...overrides,
  });
  return { app, upstream, db, s3, local };
}
