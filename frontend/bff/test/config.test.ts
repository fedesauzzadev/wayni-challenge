import { describe, expect, it } from "vitest";
import { existsSync } from "node:fs";
import path from "node:path";
import { loadConfig } from "../src/config.js";

describe("loadConfig", () => {
  it("DATA_DIR default resuelve al <root>/samples que el compose monta como /data (sin importar el CWD)", () => {
    const config = loadConfig({
      UPLOAD_API_KEY: "test-key",
      POSTGRES_PASSWORD: "test",
    });

    expect(path.isAbsolute(config.localUpload.dataDir)).toBe(true);
    // El dir padre debe ser el root del repo (donde vive docker-compose.yml)…
    expect(existsSync(path.join(path.dirname(config.localUpload.dataDir), "docker-compose.yml"))).toBe(true);
    // …y apuntar al samples default del compose (${DATA_DIR:-./samples}).
    expect(path.basename(config.localUpload.dataDir)).toBe("samples");
  });

  it("DATA_DIR explícito se respeta tal cual (p. ej. absoluto custom)", () => {
    const config = loadConfig({
      UPLOAD_API_KEY: "test-key",
      POSTGRES_PASSWORD: "test",
      DATA_DIR: "C:/datos/bcra",
    });

    expect(config.localUpload.dataDir).toBe("C:/datos/bcra");
    expect(config.localUpload.ingestionSourceRoot).toBe("/data");
  });
});
