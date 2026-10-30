import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ActivityLog } from "@/components/ActivityLog";

describe("ActivityLog", () => {
  it("muestra los eventos con timestamp y mensaje", () => {
    render(
      <ActivityLog
        entries={[
          { at: Date.parse("2026-10-30T18:00:00Z"), level: "info", message: "Carga iniciada: deudores.txt" },
          { at: Date.parse("2026-10-30T18:00:05Z"), level: "progress", message: "Transfiriendo el archivo al servidor: 25%." },
          { at: Date.parse("2026-10-30T18:00:40Z"), level: "success", message: "Import completado." },
        ]}
      />,
    );

    const log = screen.getByLabelText("Log de actividad");
    expect(log).toHaveTextContent("Carga iniciada: deudores.txt");
    expect(log).toHaveTextContent("25%");
    expect(log).toHaveTextContent("Import completado.");
  });

  it("con lista vacía no rompe (se oculta desde la página)", () => {
    render(<ActivityLog entries={[]} />);
    expect(screen.getByLabelText("Log de actividad")).toBeInTheDocument();
  });
});
