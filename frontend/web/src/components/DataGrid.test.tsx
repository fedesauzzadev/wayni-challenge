import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ColumnDef } from "@tanstack/react-table";
import { describe, expect, it, vi } from "vitest";
import { DataGrid } from "./DataGrid";

interface Row {
  id: number;
  name: string;
}

const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: "id", header: "ID" },
  { accessorKey: "name", header: "Nombre" },
];

const rows: Row[] = Array.from({ length: 5 }, (_, i) => ({ id: i + 1, name: `Fila ${i + 1}` }));

describe("DataGrid", () => {
  it("renderiza filas y cabeceras", () => {
    render(<DataGrid columns={columns} data={rows} />);

    expect(screen.getByRole("columnheader", { name: "ID" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Nombre" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Fila 3" })).toBeInTheDocument();
  });

  it("sin datos muestra empty state", () => {
    render(<DataGrid columns={columns} data={[]} emptyMessage="Sin resultados." />);

    expect(screen.getByText("Sin resultados.")).toBeInTheDocument();
  });

  it("con total muestra paginación y navega con los botones", async () => {
    const user = userEvent.setup();
    const onOffsetChange = vi.fn();
    render(
      <DataGrid
        columns={columns}
        data={rows}
        total={120}
        offset={0}
        limit={25}
        onOffsetChange={onOffsetChange}
      />,
    );

    expect(
      screen.getByText((_, element) => element?.textContent === "1–5 de 120"),
    ).toBeInTheDocument();
    expect(
      screen.getByText((_, element) => element?.textContent === "Página 1 / 5"),
    ).toBeInTheDocument();

    const next = screen.getByRole("button", { name: "Página siguiente" });
    const prev = screen.getByRole("button", { name: "Página anterior" });
    expect(prev).toBeDisabled();

    await user.click(next);
    expect(onOffsetChange).toHaveBeenCalledWith(25);
  });

  it("cambia el tamaño de página", async () => {
    const user = userEvent.setup();
    const onLimitChange = vi.fn();
    render(
      <DataGrid
        columns={columns}
        data={rows}
        total={120}
        offset={0}
        limit={25}
        onLimitChange={onLimitChange}
      />,
    );

    await user.selectOptions(screen.getByRole("combobox"), "50");
    expect(onLimitChange).toHaveBeenCalledWith(50);
  });
});
