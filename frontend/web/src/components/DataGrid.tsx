import {
  flexRender,
  getCoreRowModel,
  useReactTable,
  type ColumnDef,
} from "@tanstack/react-table";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { cn } from "@/lib/utils";

export interface DataGridProps<TData> {
  columns: ColumnDef<TData, unknown>[];
  data: TData[];
  loading?: boolean;
  emptyMessage?: string;
  /** Paginación manual del backend. Sin `total` la grilla no muestra controles. */
  total?: number;
  offset?: number;
  limit?: number;
  onOffsetChange?: (offset: number) => void;
  onLimitChange?: (limit: number) => void;
}

export function DataGrid<TData>({
  columns,
  data,
  loading = false,
  emptyMessage = "Sin resultados.",
  total,
  offset = 0,
  limit = 25,
  onOffsetChange,
  onLimitChange,
}: DataGridProps<TData>) {
  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
    manualPagination: true,
  });

  const totalItems = total ?? 0;
  const pageCount = limit > 0 ? Math.ceil(totalItems / limit) : 1;
  const currentPage = Math.floor(offset / Math.max(limit, 1)) + 1;
  const canPrev = offset > 0;
  const canNext = offset + limit < totalItems;
  const from = totalItems === 0 ? 0 : offset + 1;
  const to = Math.min(offset + data.length, totalItems);

  return (
    <div className="space-y-3">
      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              {table.getHeaderGroups()[0]?.headers.map((header) => (
                <TableHead key={header.id}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              Array.from({ length: 5 }).map((_, rowIndex) => (
                <TableRow key={rowIndex}>
                  {columns.map((_column, cellIndex) => (
                    <TableCell key={cellIndex}>
                      <Skeleton className="h-4 w-full max-w-24" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : data.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={columns.length} className="h-28 text-center text-muted-foreground">
                  {emptyMessage}
                </TableCell>
              </TableRow>
            ) : (
              table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {total !== undefined && (
        <div className="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
          <span>
            {from}–{to} de <strong className="text-foreground">{totalItems}</strong>
          </span>
          <div className="flex items-center gap-2">
            <select
              aria-label="Filas por página"
              className="h-8 rounded-md border border-input bg-transparent px-2 text-xs"
              value={limit}
              onChange={(event) => onLimitChange?.(Number(event.target.value))}
            >
              {[25, 50, 100].map((size) => (
                <option key={size} value={size} className="bg-card">
                  {size} filas
                </option>
              ))}
            </select>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={!canPrev || loading}
              onClick={() => onOffsetChange?.(Math.max(offset - limit, 0))}
              aria-label="Página anterior"
            >
              <ChevronLeft />
            </Button>
            <span className={cn("min-w-16 text-center")}>
              Página {currentPage} / {Math.max(pageCount, 1)}
            </span>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={!canNext || loading}
              onClick={() => onOffsetChange?.(offset + limit)}
              aria-label="Página siguiente"
            >
              <ChevronRight />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
