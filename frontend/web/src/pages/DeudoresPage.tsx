import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Banknote, Users } from "lucide-react";
import { useState } from "react";
import { api } from "@/api/client";
import type { DeudorItem } from "@/api/types";
import { DataGrid } from "@/components/DataGrid";
import { StatCard } from "@/components/StatCard";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { formatCuit, formatMoney, formatNumber } from "@/lib/format";

const columns: ColumnDef<DeudorItem, unknown>[] = [
  {
    accessorKey: "cuit",
    header: "CUIT",
    cell: ({ row }) => <span className="font-mono">{formatCuit(row.original.cuit)}</span>,
  },
  {
    accessorKey: "nombre",
    header: "Deudor",
    cell: ({ row }) => row.original.nombre ?? <span className="text-muted-foreground">Sin nombre</span>,
  },
  {
    accessorKey: "situacionMaxima",
    header: "Situación",
    cell: ({ row }) => {
      const situacion = row.original.situacionMaxima;
      return (
        <Badge variant={situacion >= 5 ? "destructive" : situacion >= 3 ? "warning" : "default"}>
          {situacion}
        </Badge>
      );
    },
  },
  {
    accessorKey: "sumaTotalPrestamos",
    header: "Suma total préstamos",
    cell: ({ row }) => (
      <span className="tabular-nums">{formatMoney(row.original.sumaTotalPrestamos)}</span>
    ),
  },
];

/** Situaciones BCRA ordenadas por severidad (misma tabla que el backend usa para peor_situacion). */
const Situaciones: Array<{ value: number; label: string }> = [
  { value: 1, label: "1 · Normal" },
  { value: 21, label: "21 · Riesgo bajo" },
  { value: 23, label: "23 · Tratamiento especial" },
  { value: 3, label: "3 · Riesgo medio" },
  { value: 4, label: "4 · Riesgo alto" },
  { value: 5, label: "5 · Irrecuperable" },
  { value: 11, label: "11 · Asistencias cubiertas por garantías" },
];

export function DeudoresPage() {
  const [offset, setOffset] = useState(0);
  const [limit, setLimit] = useState(25);
  const [situacion, setSituacion] = useState<number | undefined>(undefined);

  const pageQuery = useQuery({
    queryKey: ["deudores", offset, limit, situacion],
    queryFn: () => api.deudores(offset, limit, situacion),
  });

  const page = pageQuery.data;
  const isErrorNoImport =
    pageQuery.isError && (pageQuery.error as Error).message.includes("NoActiveImport");

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2">
        <StatCard
          icon={Users}
          label="Total deudores"
          value={page ? formatNumber(page.total) : "—"}
          hint={situacion ? `Con situación máxima ${situacion}` : "Del import activo"}
          loading={pageQuery.isLoading}
        />
        <StatCard
          icon={Banknote}
          label="Suma de la página"
          value={
            page
              ? formatMoney(page.items.reduce((sum, deudor) => sum + deudor.sumaTotalPrestamos, 0))
              : "—"
          }
          hint={`Top ${page?.limit ?? limit} por suma total`}
          loading={pageQuery.isLoading}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Grilla de deudores</CardTitle>
          <CardDescription>
            Paginado por el backend (<code className="text-xs">GET /deudores</code> con offset/limit;
            el límite máximo lo fija <code className="text-xs">Query:TopMax</code>).
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center gap-2">
            <label
              htmlFor="situacion-filter"
              className="shrink-0 text-sm text-muted-foreground"
            >
              Situación máxima
            </label>
            <select
              id="situacion-filter"
              className="h-9 max-w-64 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              value={situacion ?? ""}
              onChange={(event) => {
                setSituacion(event.target.value === "" ? undefined : Number(event.target.value));
                setOffset(0);
              }}
            >
              <option value="">Todas</option>
              {Situaciones.map((opcion) => (
                <option key={opcion.value} value={opcion.value}>
                  {opcion.label}
                </option>
              ))}
            </select>
          </div>
          {isErrorNoImport ? (
            <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
              Todavía no hay un import activo publicado: cargá el archivo en la pestaña Archivo.
            </p>
          ) : (
            <DataGrid
              columns={columns}
              data={page?.items ?? []}
              loading={pageQuery.isLoading}
              emptyMessage={
                situacion ? "Sin deudores con esa situación máxima." : "Sin deudores en el import activo."
              }
              total={page?.total}
              offset={offset}
              limit={limit}
              onOffsetChange={(next) => setOffset(next)}
              onLimitChange={(next) => {
                setLimit(next);
                setOffset(0);
              }}
            />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
