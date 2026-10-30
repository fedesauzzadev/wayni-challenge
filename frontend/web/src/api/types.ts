export interface ImportStatus {
  id: string;
  source: string;
  status: "Pending" | "Loading" | "Aggregating" | "Completed" | "Failed";
  periodo: number | null;
  size: number;
  bytesCommitted: number;
  linesProcessed: number;
  linesRejected: number;
  loadingProgressPercent: number | null;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
}

export interface LatestImport {
  id: string;
  source: string;
  status: ImportStatus["status"];
  periodo: number | null;
  size: number;
  bytesCommitted: number;
  linesProcessed: number;
  linesRejected: number;
  loadingProgressPercent: number | null;
  durationMs: number | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
}

export interface UploadResult {
  importId: string | null;
  status: string | null;
  alreadyExisted: boolean;
  source: string;
}

export interface DeudorItem {
  cuit: string;
  situacionMaxima: number;
  sumaTotalPrestamos: number;
  nombre: string | null;
}

export interface DeudoresPage {
  total: number;
  offset: number;
  limit: number;
  items: DeudorItem[];
}

export interface EntidadItem {
  codigo: number;
  nombre: string | null;
  sumaTotalPrestamos: number;
}

export interface EntidadesPage {
  total: number;
  items: EntidadItem[];
}

export interface Totales {
  cantidadDeudores: number;
  cantidadEntidades: number;
  sumaTotalPrestamos: number;
}

export interface Recipient {
  id: number;
  email: string;
  createdAt: string;
}

export const TERMINAL_STATUSES: ImportStatus["status"][] = ["Completed", "Failed"];
