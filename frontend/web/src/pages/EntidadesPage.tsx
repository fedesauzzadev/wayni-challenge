import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Building2, PiggyBank, Search, Users2 } from "lucide-react";
import { useState } from "react";
import { api } from "@/api/client";
import type { EntidadItem } from "@/api/types";
import { DataGrid } from "@/components/DataGrid";
import { StatCard } from "@/components/StatCard";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { formatMoney, formatNumber } from "@/lib/format";

const columns: ColumnDef<EntidadItem, unknown>[] = [
  {
    accessorKey: "codigo",
    header: "Código",
    cell: ({ row }) => <span className="font-mono">{row.original.codigo}</span>,
  },
  {
    accessorKey: "nombre",
    header: "Entidad",
    cell: ({ row }) => row.original.nombre ?? <span className="text-muted-foreground">Sin nombre</span>,
  },
  {
    accessorKey: "sumaTotalPrestamos",
    header: "Suma total préstamos",
    cell: ({ row }) => (
      <span className="tabular-nums">{formatMoney(row.original.sumaTotalPrestamos)}</span>
    ),
  },
];

export function EntidadesPage() {
  const [draftFilter, setDraftFilter] = useState("");
  const [filter, setFilter] = useState("");

  const entidadesQuery = useQuery({
    queryKey: ["entidades", filter],
    queryFn: () => api.entidades(filter || undefined),
  });
  const totalesQuery = useQuery({ queryKey: ["totales"], queryFn: api.totales });

  const entidades = entidadesQuery.data;
  const totales = totalesQuery.data;
  const isErrorNoImport =
    entidadesQuery.isError && (entidadesQuery.error as Error).message.includes("NoActiveImport");

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-3">
        <StatCard
          icon={Users2}
          label="Cantidad de deudores"
          value={totales ? formatNumber(totales.cantidadDeudores) : "—"}
          loading={totalesQuery.isLoading}
        />
        <StatCard
          icon={Building2}
          label="Cantidad de entidades"
          value={totales ? formatNumber(totales.cantidadEntidades) : "—"}
          loading={totalesQuery.isLoading}
        />
        <StatCard
          icon={PiggyBank}
          label="Suma total préstamos"
          value={totales ? formatMoney(totales.sumaTotalPrestamos) : "—"}
          loading={totalesQuery.isLoading}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Grilla de entidades</CardTitle>
          <CardDescription>
            Entidades del import activo con su suma total de préstamos. Filtro por código exacto o
            prefijo (se aplica en el backend).
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <form
            className="flex gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              setFilter(draftFilter.trim());
            }}
          >
            <Input
              className="max-w-48"
              inputMode="numeric"
              placeholder="Código (ej. 266)"
              value={draftFilter}
              onChange={(event) => setDraftFilter(event.target.value.replace(/\D/g, "").slice(0, 5))}
              aria-label="Filtrar por código de entidad"
            />
            <Button type="submit" variant="secondary" className="shrink-0">
              <Search />
              Filtrar
            </Button>
            {filter && (
              <Button
                type="button"
                variant="ghost"
                className="shrink-0"
                onClick={() => {
                  setFilter("");
                  setDraftFilter("");
                }}
              >
                Limpiar
              </Button>
            )}
          </form>

          {isErrorNoImport ? (
            <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
              Todavía no hay un import activo publicado: cargá el archivo en la pestaña Archivo.
            </p>
          ) : totalesQuery.isError && !totales ? (
            <p className="text-sm text-destructive">{(totalesQuery.error as Error).message}</p>
          ) : (
            <DataGrid
              columns={columns}
              data={entidades?.items ?? []}
              loading={entidadesQuery.isLoading}
              emptyMessage={
                filter ? `Sin entidades cuyo código empiece con ${filter}.` : "Sin entidades."
              }
              total={entidades?.total}
            />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
