import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileClock, FileUp, HardDriveUpload } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { api } from "@/api/client";
import { ActivityLog, type ActivityEntry, type ActivityLevel } from "@/components/ActivityLog";
import { RecipientesEmail } from "@/components/RecipientesEmail";
import { StatCard } from "@/components/StatCard";
import { UploadProgress, type UploadPhase } from "@/components/UploadProgress";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  formatBytes,
  formatDateTime,
  formatDuration,
  formatNumber,
  formatPeriodo,
} from "@/lib/format";
import { clearSession, loadLog, loadSession, saveLog, saveSession } from "@/lib/uploadSession";
import { TERMINAL_STATUSES, type ImportStatus } from "@/api/types";

const POLL_INTERVAL_MS = 2000;
/** Ventana para adoptar un import no-terminal de /imports/latest al cargar la página. */
const ADOPT_FRESHNESS_MS = 6 * 60 * 60 * 1000;
const TRANSFER_MILESTONES = [10, 25, 50, 75, 90, 100];
const LOADING_MILESTONE_STEP = 25;

type StorageMode = "local" | "s3";

const storageDescriptions: Record<StorageMode, string> = {
  local: "El archivo se entrega directo al ingestion (dir compartido, sin pasar por S3).",
  s3: "El archivo se sube al bucket bcra-local de S3 (LocalStack) y el ingestion lo lee desde ahí.",
};

const statusBadge: Record<string, "success" | "destructive" | "warning" | "default"> = {
  Completed: "success",
  Failed: "destructive",
  Loading: "warning",
  Aggregating: "warning",
  Pending: "default",
};

export function ArchivoPage() {
  const queryClient = useQueryClient();
  const latestQuery = useQuery({ queryKey: ["imports", "latest"], queryFn: api.latestImport });

  const [file, setFile] = useState<File | null>(null);
  const [maeent, setMaeent] = useState<File | null>(null);
  const [nomdeu, setNomdeu] = useState<File | null>(null);
  const [periodo, setPeriodo] = useState(() => defaultPeriodo());
  const [storage, setStorage] = useState<StorageMode>("local");
  const [phase, setPhase] = useState<UploadPhase | null>(null);
  const [uploadPercent, setUploadPercent] = useState(0);
  const [processingPercent, setProcessingPercent] = useState<number | null>(null);
  const [statusText, setStatusText] = useState("");
  const [activeImportId, setActiveImportId] = useState<string | null>(null);
  // Nombre/tamaño para la UI aunque el File en sí no se pueda restaurar tras un F5.
  const [fileMeta, setFileMeta] = useState<{ name: string; size: number } | null>(() => {
    const session = loadSession();
    return session ? { name: session.fileName, size: session.fileSize } : null;
  });
  const [log, setLog] = useState<ActivityEntry[]>(() => loadLog());
  const fileInputRef = useRef<HTMLInputElement>(null);
  const maeentInputRef = useRef<HTMLInputElement>(null);
  const nomdeuInputRef = useRef<HTMLInputElement>(null);
  const nextMilestoneRef = useRef(0);
  const lastLoadingMilestoneRef = useRef(0);
  const prevStatusRef = useRef<ImportStatus["status"] | null>(null);
  const adoptedRef = useRef(false);

  const appendLog = useCallback((level: ActivityLevel, message: string) => {
    setLog((prev) => {
      const next = [...prev, { at: Date.now(), level, message }];
      saveLog(next);
      return next;
    });
  }, []);

  // Fase (b): polling del estado del import hasta estado terminal.
  const statusQuery = useQuery({
    queryKey: ["imports", "status", activeImportId],
    queryFn: () => api.importStatus(activeImportId as string),
    enabled: activeImportId !== null && !TERMINAL_STATUSES.includes(phase as ImportStatus["status"]),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && TERMINAL_STATUSES.includes(status) ? false : POLL_INTERVAL_MS;
    },
  });

  // Recuperación tras F5: si hay un import activo en sesión, se retoma el seguimiento.
  useEffect(() => {
    const session = loadSession();
    if (session?.importId) {
      setActiveImportId(session.importId);
      setPhase("processing");
      appendLog(
        "info",
        `Sesión restaurada tras recargar la página: reanudando el seguimiento del import ${shortId(session.importId)}.`,
      );
    } else if (session && Date.now() - session.startedAt < 60 * 60 * 1000) {
      // La transferencia murió con la página y el import nunca se encoló del lado del server.
      clearSession();
      appendLog(
        "error",
        `La transferencia de ${session.fileName} se interrumpió al recargar la página: el import nunca llegó a encolarse. Subilo de nuevo.`,
      );
    }
  }, [appendLog]);

  // Recuperación sin sesión: si el último import está corriendo (empezado en otra pestaña o
  // terminal), se adopta para mostrar su progreso.
  useEffect(() => {
    if (adoptedRef.current || activeImportId) return;
    const latest = latestQuery.data?.latest;
    if (!latest || TERMINAL_STATUSES.includes(latest.status)) return;
    if (Date.now() - Date.parse(latest.createdAtUtc) > ADOPT_FRESHNESS_MS) return;

    adoptedRef.current = true;
    setActiveImportId(latest.id);
    setPhase("processing");
    setFileMeta({ name: sourceFileName(latest.source), size: latest.size });
    appendLog(
      "info",
      `Import en curso detectado (${latest.status}, ${shortId(latest.id)}): reanudando el seguimiento.`,
    );
  }, [latestQuery.data, activeImportId, appendLog]);

  useEffect(() => {
    const status = statusQuery.data;
    if (!status || !activeImportId) return;

    setProcessingPercent(status.loadingProgressPercent ?? (status.status === "Completed" ? 100 : 0));
    setStatusText(describeStatus(status));

    if (prevStatusRef.current !== status.status) {
      prevStatusRef.current = status.status;
      appendLog(statusLevel(status.status), describeStatus(status));
    } else if (status.status === "Loading") {
      const percent = status.loadingProgressPercent ?? 0;
      if (percent >= lastLoadingMilestoneRef.current + LOADING_MILESTONE_STEP) {
        lastLoadingMilestoneRef.current = percent - (percent % LOADING_MILESTONE_STEP);
        appendLog("progress", describeStatus(status));
      }
    }

    if (status.status === "Completed") {
      setPhase("completed");
      appendLog(
        "success",
        `Import completado en ${formatDuration(status.finishedAtUtc && status.startedAtUtc ? Date.parse(status.finishedAtUtc) - Date.parse(status.startedAtUtc) : null)}. Revisá Mailpit para ver la notificación.`,
      );
      toast.success("Import completado. Revisá Mailpit para ver la notificación.");
      setActiveImportId(null);
      clearSession();
      queryClient.invalidateQueries({ queryKey: ["imports", "latest"] });
    } else if (status.status === "Failed") {
      setPhase("failed");
      appendLog("error", "El import terminó en estado Failed. Revisá los logs del ingestion.");
      toast.error("El import falló. Revisá el detalle en la pestaña o los logs del ingestion.");
      setActiveImportId(null);
      clearSession();
      queryClient.invalidateQueries({ queryKey: ["imports", "latest"] });
    }
  }, [statusQuery.data, activeImportId, appendLog, queryClient]);

  const handleUploadProgress = useCallback(
    (percent: number) => {
      setUploadPercent(percent);
      const milestone = TRANSFER_MILESTONES[nextMilestoneRef.current];
      if (milestone !== undefined && percent >= milestone) {
        nextMilestoneRef.current += 1;
        appendLog("progress", `Transfiriendo el archivo al servidor: ${percent}%.`);
      }
    },
    [appendLog],
  );

  const uploadMutation = useMutation({
    mutationFn: async () => {
      if (!file) throw new Error("Elegí un archivo primero.");
      setPhase("uploading");
      setUploadPercent(0);
      setProcessingPercent(null);
      nextMilestoneRef.current = 0;
      setStatusText(
        storage === "s3"
          ? "Subiendo el archivo al bucket de S3 (LocalStack)…"
          : "Transfiriendo el archivo al ingestion (directo, sin S3)…",
      );
      appendLog(
        "info",
        `Carga iniciada: ${file.name} (${formatBytes(file.size)}, periodo ${periodo}, ${storage === "s3" ? "vía S3" : "entrega directa"}).`,
      );
      return api.uploadFile(file, Number(periodo), handleUploadProgress, { maeent, nomdeu }, storage);
    },
    onSuccess: (result) => {
      setPhase("processing");
      setUploadPercent(100);
      setStatusText(
        result.alreadyExisted
          ? "El archivo ya estaba importado (idempotencia source+etag+periodo); se consulta su estado…"
          : "Archivo entregado. Import encolado en el ingestion; se consulta el progreso…",
      );
      if (file) {
        setFileMeta({ name: file.name, size: file.size });
        saveSession({
          importId: result.importId,
          fileName: file.name,
          fileSize: file.size,
          storage,
          startedAt: Date.now(),
        });
      }
      appendLog("success", `Archivo entregado: ${result.source}.`);
      appendLog(
        result.alreadyExisted ? "info" : "progress",
        result.alreadyExisted
          ? `Ya existía un import idéntico (source+etag+periodo): se consulta el estado de ${shortId(result.importId ?? "")}.`
          : `Import encolado en el ingestion: ${shortId(result.importId ?? "")}.`,
      );
      prevStatusRef.current = null;
      lastLoadingMilestoneRef.current = 0;
      setActiveImportId(result.importId);
    },
    onError: (error: Error) => {
      setPhase("failed");
      setStatusText(error.message);
      appendLog("error", `Carga fallida: ${error.message}`);
      toast.error(`Upload fallido: ${error.message}`);
    },
  });

  const latest = latestQuery.data?.latest ?? null;

  return (
    <div className="space-y-6">
      <div className="grid gap-4 md:grid-cols-3">
        <StatCard
          icon={FileClock}
          label="Última carga"
          value={latest ? formatDateTime(latest.createdAtUtc) : "Sin cargas"}
          hint={latest ? `Periodo ${formatPeriodo(latest.periodo)}` : "Todavía no hay imports"}
          loading={latestQuery.isLoading}
        />
        <StatCard
          icon={HardDriveUpload}
          label="Líneas procesadas"
          value={latest ? formatNumber(latest.linesProcessed) : "—"}
          hint={
            latest
              ? `${formatNumber(latest.linesRejected)} rechazadas · ${formatNumber(latest.linesSkippedControl)} de control (98/99)`
              : undefined
          }
          loading={latestQuery.isLoading}
        />
        <StatCard
          icon={FileClock}
          label="Duración"
          value={latest ? formatDuration(latest.durationMs) : "—"}
          hint={latest ? formatBytes(latest.size) : undefined}
          loading={latestQuery.isLoading}
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileUp className="size-4 text-primary" />
            Cargar archivo de deudores
          </CardTitle>
          <CardDescription>
            Elegí cómo entregar el archivo: <strong>directo</strong> (sin S3, alcanza con
            seleccionarlo de tu PC) o <strong>vía S3</strong> (LocalStack). En ambos casos el
            ingestion dispara el pipeline: staging → agregación → swap atómico. Incluí
            <code className="text-xs"> Maeent.txt</code> y <code className="text-xs">Nomdeu.txt</code> la
            primera vez (o cuando cambien): el pipeline los busca junto al archivo de deudores.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <form
            className="flex flex-wrap items-center gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              uploadMutation.mutate();
            }}
          >
            <input
              ref={fileInputRef}
              type="file"
              className="hidden"
              onChange={(event) => {
                setFile(event.target.files?.[0] ?? null);
                if (event.target.files?.[0]) {
                  setFileMeta({ name: event.target.files[0].name, size: event.target.files[0].size });
                }
              }}
            />
            <input
              ref={maeentInputRef}
              type="file"
              accept=".txt"
              className="hidden"
              onChange={(event) => setMaeent(event.target.files?.[0] ?? null)}
            />
            <input
              ref={nomdeuInputRef}
              type="file"
              accept=".txt"
              className="hidden"
              onChange={(event) => setNomdeu(event.target.files?.[0] ?? null)}
            />
            <Button
              type="button"
              variant="outline"
              onClick={() => fileInputRef.current?.click()}
              disabled={uploadMutation.isPending || phase === "uploading" || phase === "processing"}
            >
              <FileUp />
              {file ? file.name : "Elegir archivo…"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              className="text-muted-foreground"
              onClick={() => maeentInputRef.current?.click()}
              disabled={uploadMutation.isPending || phase === "uploading" || phase === "processing"}
            >
              {maeent ? `Maeent: ${maeent.name}` : "Maeent.txt (opcional)"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              className="text-muted-foreground"
              onClick={() => nomdeuInputRef.current?.click()}
              disabled={uploadMutation.isPending || phase === "uploading" || phase === "processing"}
            >
              {nomdeu ? `Nomdeu: ${nomdeu.name}` : "Nomdeu.txt (opcional)"}
            </Button>
            <div
              className="flex overflow-hidden rounded-md border"
              role="group"
              aria-label="Modo de entrega del archivo"
            >
              {(["local", "s3"] as const).map((mode) => (
                <Button
                  key={mode}
                  type="button"
                  size="sm"
                  variant={storage === mode ? "default" : "ghost"}
                  className="rounded-none"
                  onClick={() => setStorage(mode)}
                  disabled={uploadMutation.isPending || phase === "uploading" || phase === "processing"}
                >
                  {mode === "local" ? "Directo (sin S3)" : "Vía S3 (LocalStack)"}
                </Button>
              ))}
            </div>
            <label className="flex items-center gap-2 text-sm text-muted-foreground">
              Periodo
              <Input
                className="w-28"
                inputMode="numeric"
                pattern="\d{6}"
                value={periodo}
                onChange={(event) => setPeriodo(event.target.value.replace(/\D/g, "").slice(0, 6))}
                aria-label="Periodo (yyyyMM)"
              />
            </label>
            <Button
              type="submit"
              disabled={!file || periodo.length !== 6 || uploadMutation.isPending || phase === "uploading" || phase === "processing"}
            >
              Subir y procesar
            </Button>
          </form>

          <p className="text-xs text-muted-foreground">{storageDescriptions[storage]}</p>

          {fileMeta && phase && (
            <UploadProgress
              phase={phase}
              uploadPercent={uploadPercent}
              importStatus={statusQuery.data?.status ?? (phase === "processing" ? "Pending" : null)}
              processingPercent={processingPercent}
              fileName={fileMeta.name}
              fileSize={fileMeta.size}
              statusText={statusText}
              uploadLabel={storage === "s3" ? "Subida a S3 (LocalStack)" : "Entrega directa al ingestion"}
            />
          )}

          {log.length > 0 && <ActivityLog entries={log} />}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Detalle de la última carga</CardTitle>
          <CardDescription>
            Datos del último import registrado en el ingestion-service.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {latestQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Cargando…</p>
          ) : latestQuery.isError ? (
            <p className="text-sm text-destructive">{(latestQuery.error as Error).message}</p>
          ) : !latest ? (
            <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
              Todavía no hay cargas del archivo de deudores. Subí el primero con el formulario de arriba.
            </p>
          ) : (
            <dl className="grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
              <Detail label="Estado">
                <Badge variant={statusBadge[latest.status] ?? "default"}>{latest.status}</Badge>
              </Detail>
              <Detail label="Job">
                <code className="text-xs">{latest.id}</code>
              </Detail>
              <Detail label="Periodo">{formatPeriodo(latest.periodo)}</Detail>
              <Detail label="Source">
                <code className="break-all text-xs">{latest.source}</code>
              </Detail>
              <Detail label="Tamaño">{formatBytes(latest.size)}</Detail>
              <Detail label="Líneas">{formatNumber(latest.linesProcessed)}</Detail>
              <Detail label="Rechazadas (malformadas)">{formatNumber(latest.linesRejected)}</Detail>
              <Detail label="De control (98/99, se descartan)">{formatNumber(latest.linesSkippedControl)}</Detail>
              <Detail label="Inicio">{formatDateTime(latest.startedAtUtc)}</Detail>
              <Detail label="Fin">{formatDateTime(latest.finishedAtUtc)}</Detail>
            </dl>
          )}
        </CardContent>
      </Card>

      <RecipientesEmail />
    </div>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 border-b pb-2 sm:justify-start">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium sm:text-left">{children}</dd>
    </div>
  );
}

function defaultPeriodo(now = new Date()): string {
  return String(now.getUTCFullYear() * 100 + now.getUTCMonth() + 1);
}

function shortId(id: string): string {
  return id ? `${id.slice(0, 8)}…` : "?";
}

function sourceFileName(source: string): string {
  const segments = source.split("/");
  return segments[segments.length - 1] || source;
}

function statusLevel(status: ImportStatus["status"]): ActivityLevel {
  switch (status) {
    case "Completed":
      return "success";
    case "Failed":
      return "error";
    case "Loading":
    case "Aggregating":
    case "Pending":
      return "progress";
  }
}

function describeStatus(status: ImportStatus): string {
  switch (status.status) {
    case "Pending":
      return "Import encolado esperando un worker libre…";
    case "Loading":
      return `Cargando a staging: ${formatBytes(status.bytesCommitted)} de ${formatBytes(status.size)} (${status.loadingProgressPercent ?? 0}%)`;
    case "Aggregating":
      return "Agregando (GROUP BY por CUIT y entidad) y creando índices…";
    case "Completed":
      return `Completado en ${formatDuration(status.finishedAtUtc && status.startedAtUtc ? Date.parse(status.finishedAtUtc) - Date.parse(status.startedAtUtc) : null)}`;
    case "Failed":
      return "El import terminó en estado Failed.";
  }
}
