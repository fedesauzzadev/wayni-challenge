import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { UploadProgress } from "@/components/UploadProgress";
import type { ImportStatus } from "@/api/types";

function renderProgress(overrides: Partial<Parameters<typeof UploadProgress>[0]> = {}) {
  const props = {
    phase: "uploading" as const,
    uploadPercent: 42,
    importStatus: null as ImportStatus["status"] | null,
    processingPercent: null as number | null,
    fileName: "deudores.txt",
    fileSize: 1024,
    statusText: "Transfiriendo…",
    uploadLabel: "Entrega directa al ingestion",
    ...overrides,
  };
  render(<UploadProgress {...props} />);
  return props;
}

function row(label: string): HTMLElement {
  const row = screen.getByText(label).closest("div.space-y-1\\.5");
  expect(row).not.toBeNull();
  return row as HTMLElement;
}

describe("UploadProgress", () => {
  it("muestra el % real durante la transferencia y deja la fase de procesamiento en 0", () => {
    renderProgress();

    expect(row("Entrega directa al ingestion")).toHaveTextContent("42%");
    expect(row("Procesamiento (staging → agregación → swap)")).toHaveTextContent("0%");
  });

  it("transferencia al 100% sin respuesta aún: fase (a) indeterminada (…) y no 100% mentiroso", () => {
    renderProgress({ uploadPercent: 100 });

    expect(row("Entrega directa al ingestion")).toHaveTextContent("…");
  });

  it("Aggregating es indeterminado; Loading muestra el % del backend", () => {
    const { rerender } = render(<div />);
    rerender(
      <UploadProgress
        phase="processing"
        uploadPercent={100}
        importStatus="Aggregating"
        processingPercent={null}
        fileName="deudores.txt"
        fileSize={1024}
        statusText="Agregando…"
        uploadLabel="Entrega directa al ingestion"
      />,
    );
    expect(row("Procesamiento (staging → agregación → swap)")).toHaveTextContent("…");

    rerender(
      <UploadProgress
        phase="processing"
        uploadPercent={100}
        importStatus="Loading"
        processingPercent={37}
        fileName="deudores.txt"
        fileSize={1024}
        statusText="Cargando…"
        uploadLabel="Entrega directa al ingestion"
      />,
    );
    expect(row("Procesamiento (staging → agregación → swap)")).toHaveTextContent("37%");
  });

  it("completado: ambas fases en 100%", () => {
    renderProgress({ phase: "completed", uploadPercent: 100, importStatus: "Completed", processingPercent: 100 });

    expect(row("Entrega directa al ingestion")).toHaveTextContent("100%");
    expect(row("Procesamiento (staging → agregación → swap)")).toHaveTextContent("100%");
  });
});
