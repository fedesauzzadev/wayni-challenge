import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import App from "./App";

function renderApp() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>,
  );
}

describe("App", () => {
  it("renderiza las tres pestañas y navega entre ellas", async () => {
    const user = userEvent.setup();
    renderApp();

    expect(screen.getByRole("tab", { name: /archivo/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /deudores/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /entidades/i })).toBeInTheDocument();

    // Los paneles quedan montados (el progreso de un upload sobrevive el cambio de pestaña),
    // así que se identifica el activo por su nombre accesible.
    expect(screen.getByRole("tabpanel", { name: /archivo/i })).toHaveTextContent(/Cargar archivo de deudores/i);

    await user.click(screen.getByRole("tab", { name: /deudores/i }));
    expect(screen.getByRole("tabpanel", { name: /deudores/i })).toHaveTextContent(/Grilla de deudores/i);

    await user.click(screen.getByRole("tab", { name: /entidades/i }));
    expect(screen.getByRole("tabpanel", { name: /entidades/i })).toHaveTextContent(/Grilla de entidades/i);
  });
});
