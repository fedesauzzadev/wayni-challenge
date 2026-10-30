import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "@/api/client";
import type { Recipient } from "@/api/types";
import { RecipientesEmail } from "./RecipientesEmail";

vi.mock("@/api/client", () => ({
  api: {
    recipients: vi.fn(),
    addRecipient: vi.fn(),
    removeRecipient: vi.fn(),
  },
}));

const mockedApi = vi.mocked(api);

function renderWithProviders() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <RecipientesEmail />
    </QueryClientProvider>,
  );
}

const sampleRecipients: Recipient[] = [
  { id: 1, email: "ops@example.com", createdAt: "2026-10-01T00:00:00Z" },
  { id: 2, email: "fede@example.com", createdAt: "2026-10-02T00:00:00Z" },
];

describe("RecipientesEmail", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockedApi.recipients.mockResolvedValue({ recipients: sampleRecipients });
  });

  it("lista los destinatarios del BFF", async () => {
    renderWithProviders();

    await waitFor(() => {
      expect(screen.getByTestId("recipients-list")).toBeInTheDocument();
    });
    expect(screen.getByText("ops@example.com")).toBeInTheDocument();
    expect(screen.getByText("fede@example.com")).toBeInTheDocument();
  });

  it("alta: envía el email al BFF", async () => {
    const user = userEvent.setup();
    mockedApi.addRecipient.mockResolvedValue({
      recipient: { id: 3, email: "nuevo@example.com", createdAt: "2026-10-30T00:00:00Z" },
      alreadyExisted: false,
    });

    renderWithProviders();
    await screen.findByTestId("recipients-list");

    await user.type(screen.getByRole("textbox", { name: /email destinatario/i }), "nuevo@example.com");
    await user.click(screen.getByRole("button", { name: /alta/i }));

    await waitFor(() => {
      expect(mockedApi.addRecipient).toHaveBeenCalledWith("nuevo@example.com");
    });
  });

  it("baja: elimina por id", async () => {
    const user = userEvent.setup();
    mockedApi.removeRecipient.mockResolvedValue(undefined as never);

    renderWithProviders();
    await screen.findByTestId("recipients-list");

    await user.click(screen.getByRole("button", { name: "Eliminar ops@example.com" }));

    await waitFor(() => {
      expect(mockedApi.removeRecipient).toHaveBeenCalledWith(1);
    });
  });

  it("lista vacía muestra el empty state con el fallback", async () => {
    mockedApi.recipients.mockResolvedValue({ recipients: [] });
    renderWithProviders();

    await waitFor(() => {
      expect(screen.getByText(/Sin destinatarios registrados/i)).toBeInTheDocument();
    });
  });

  it("error de BD muestra mensaje", async () => {
    mockedApi.recipients.mockRejectedValue(new Error("relation does not exist"));
    renderWithProviders();

    await waitFor(() => {
      expect(screen.getByText(/notification_recipient/)).toBeInTheDocument();
    });
  });
});
