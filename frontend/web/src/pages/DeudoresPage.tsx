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

export function DeudoresPage() {
  const [offset, setOffset] = useState(0);
  const [limit, setLimit] = useState(25);

  const pageQuery = useQuery({
    queryKey: ["deudores", offset, limit],
    queryFn: () => api.deudores(offset, limit),
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
          hint="Del import activo"
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
        <CardContent>
          {isErrorNoImport ? (
            <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
              Todavía no hay un import activo publicado: cargá el archivo en la pestaña Archivo.
            </p>
          ) : (
            <DataGrid
              columns={columns}
              data={page?.items ?? []}
              loading={pageQuery.isLoading}
              emptyMessage="Sin deudores en el import activo."
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
