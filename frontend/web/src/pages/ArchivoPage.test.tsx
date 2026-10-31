import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "@/api/client";
import type { LatestImport } from "@/api/types";
import { ArchivoPage } from "./ArchivoPage";

vi.mock("@/api/client", () => ({
  api: {
    latestImport: vi.fn(),
    importStatus: vi.fn(),
    uploadFile: vi.fn(),
  },
}));

const mockedApi = vi.mocked(api);

function renderWithProviders() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ArchivoPage />
    </QueryClientProvider>,
  );
}

const latest: LatestImport = {
  id: "0d9ee5ac-1111-4b33-a073-18be1e3ee0e8",
  source: "/data/uploads/202610/deudores-abc123.txt",
  status: "Completed",
  periodo: 202610,
  size: 1024,
  bytesCommitted: 1024,
  linesProcessed: 10,
  linesRejected: 0,
  linesSkippedControl: 2,
  loadingProgressPercent: 100,
  durationMs: 1500,
  createdAtUtc: "2026-10-30T00:00:00Z",
  startedAtUtc: "2026-10-30T00:00:01Z",
  finishedAtUtc: "2026-10-30T00:00:02Z",
};

function setPeriodo(value: string) {
  const input = screen.getByRole("textbox", { name: /periodo \(yyyyMM\)/i });
  fireEvent.change(input, { target: { value } });
}

function attachReference(fileName: "Maeent.txt" | "Nomdeu.txt") {
  const inputs = document.querySelectorAll<HTMLInputElement>('input[type="file"][accept=".txt"]');
  const input = fileName === "Maeent.txt" ? inputs[0] : inputs[1];
  if (!input) throw new Error(`No se encontró el input de ${fileName}`);
  fireEvent.change(input, {
    target: { files: [new File(["maestro"], fileName, { type: "text/plain" })] },
  });
}

describe("ArchivoPage — warning de referencias por periodo", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    mockedApi.latestImport.mockResolvedValue({ latest });
  });

  it("no adviene con el periodo del último import (las referencias ya existen)", async () => {
    renderWithProviders();
    // El hint de la StatCard solo muestra el periodo cuando latest ya cargó.
    await screen.findByText(/Periodo 10\/2026/);

    setPeriodo("202610");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("advierte al cambiar de periodo sin adjuntar las referencias", async () => {
    renderWithProviders();
    await screen.findByText(/Detalle de la última carga/i);

    setPeriodo("202611");

    await waitFor(() => {
      expect(screen.getByRole("status")).toHaveTextContent(/Maeent\.txt y Nomdeu\.txt/);
      expect(screen.getByRole("status")).toHaveTextContent(/11\/2026/);
    });
  });

  it("el warning desaparece al adjuntar las referencias faltantes", async () => {
    renderWithProviders();
    await screen.findByText(/Detalle de la última carga/i);

    setPeriodo("202611");
    await waitFor(() => {
      expect(screen.getByRole("status")).toBeInTheDocument();
    });

    attachReference("Maeent.txt");
    await waitFor(() => {
      expect(screen.getByRole("status")).toHaveTextContent(/Nomdeu\.txt/);
      expect(screen.getByRole("status")).not.toHaveTextContent(/Maeent\.txt y/);
    });

    attachReference("Nomdeu.txt");
    await waitFor(() => {
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });
  });

  it("advierte en la primera carga de la historia (sin imports previos)", async () => {
    mockedApi.latestImport.mockResolvedValue({ latest: null });
    renderWithProviders();
    await screen.findByText(/Todavía no hay cargas del archivo de deudores/i);

    await waitFor(() => {
      expect(screen.getByRole("status")).toHaveTextContent(/no tiene referencias guardadas/i);
    });
  });
});
