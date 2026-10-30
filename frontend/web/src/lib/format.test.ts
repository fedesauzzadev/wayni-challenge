import { describe, expect, it } from "vitest";
import {
  formatBytes,
  formatCuit,
  formatDuration,
  formatMoney,
  formatPeriodo,
} from "./format";

describe("formatMoney", () => {
  it("formatea ARS sin decimales", () => {
    expect(formatMoney(1234)).toMatch(/1\.234/);
    expect(formatMoney(1234)).toMatch(/\$/);
  });

  it("null/undefined muestran placeholder", () => {
    expect(formatMoney(null)).toBe("—");
    expect(formatMoney(undefined)).toBe("—");
  });
});

describe("formatCuit", () => {
  it("agrega los guiones", () => {
    expect(formatCuit("20123456784")).toBe("20-12345678-4");
  });

  it("deja pasar otros largos", () => {
    expect(formatCuit("123")).toBe("123");
  });
});

describe("formatDuration", () => {
  it("segundos", () => {
    expect(formatDuration(45_000)).toBe("45 s");
  });

  it("minutos y segundos", () => {
    expect(formatDuration(90_000)).toBe("1 min 30 s");
  });

  it("minutos exactos", () => {
    expect(formatDuration(120_000)).toBe("2 min");
  });

  it("null", () => {
    expect(formatDuration(null)).toBe("—");
  });
});

describe("formatBytes", () => {
  it("escala unidades", () => {
    expect(formatBytes(512)).toBe("512.0 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatBytes(5 * 1024 * 1024 * 1024)).toBe("5.0 GB");
  });
});

describe("formatPeriodo", () => {
  it("yyyyMM → MM/yyyy", () => {
    expect(formatPeriodo(202610)).toBe("10/2026");
  });

  it("null → placeholder", () => {
    expect(formatPeriodo(null)).toBe("—");
  });
});
