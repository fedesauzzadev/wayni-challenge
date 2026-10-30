import { CheckCircle2, CloudUpload, Loader2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/utils";
import type { ImportStatus } from "@/api/types";

export type UploadPhase = "uploading" | "processing" | "completed" | "failed";

export interface UploadProgressProps {
  phase: UploadPhase;
  /** 0-100 de la transferencia navegador→BFF (lo único medible de la fase (a)). */
  uploadPercent: number;
  /** Estado actual del job según el ingestion; null mientras no exista job. */
  importStatus: ImportStatus["status"] | null;
  /** loadingProgressPercent del backend durante Loading. */
  processingPercent: number | null;
  fileName: string;
  fileSize: number;
  statusText: string;
  /** Label de la fase (a): depende del modo de entrega (directo o vía S3). */
  uploadLabel: string;
}

const phaseLabels: Record<UploadPhase, string> = {
  uploading: "Entregando archivo",
  processing: "Procesando import",
  completed: "Import completado",
  failed: "El import falló",
};

export function UploadProgress({
  phase,
  uploadPercent,
  importStatus,
  processingPercent,
  fileName,
  fileSize,
  statusText,
  uploadLabel,
}: UploadProgressProps) {
  const uploadingDone = phase !== "uploading";
  // Fase (a): durante la transferencia hay % real; al llegar a 100 el BFF todavía tiene que
  // hashear/copiar y encolar → indeterminado hasta que llega la respuesta.
  const transferIndeterminate = phase === "uploading" && uploadPercent >= 100;

  // Fase (b): solo Loading tiene % medible; Pending/Aggregating son indeterminados.
  let processingIndeterminate = false;
  let processingValue = uploadingDone ? (phase === "completed" ? 100 : 0) : 0;
  if (importStatus === "Pending" || importStatus === "Aggregating") {
    processingIndeterminate = true;
    processingValue = 0;
  } else if (importStatus === "Loading") {
    processingValue = processingPercent ?? 0;
  }

  return (
    <section className="space-y-3 rounded-xl border bg-card p-5" aria-live="polite">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium">
          {phase === "completed" && <CheckCircle2 className="size-4 text-[var(--success)]" />}
          {phase === "failed" && <XCircle className="size-4 text-destructive" />}
          {(phase === "uploading" || phase === "processing") && (
            <Loader2 className="size-4 animate-spin text-primary" />
          )}
          <span>{fileName}</span>
          <span className="text-muted-foreground">({formatBytes(fileSize)})</span>
        </div>
        <Badge
          variant={phase === "completed" ? "success" : phase === "failed" ? "destructive" : "default"}
        >
          {phaseLabels[phase]}
        </Badge>
      </header>

      <PhaseRow
        icon={<CloudUpload className="size-4" />}
        label={uploadLabel}
        percent={transferIndeterminate ? null : uploadPercent}
        done={uploadingDone}
        active={phase === "uploading"}
        failed={phase === "failed"}
      />
      <PhaseRow
        icon={<Loader2 className={cn("size-4", phase === "processing" && "animate-spin")} />}
        label="Procesamiento (staging → agregación → swap)"
        percent={processingIndeterminate ? null : processingValue}
        done={phase === "completed"}
        active={phase === "processing"}
        failed={phase === "failed"}
      />

      <p className="text-xs text-muted-foreground">{statusText}</p>
    </section>
  );
}

function PhaseRow({
  icon,
  label,
  percent,
  done,
  active,
  failed = false,
}: {
  icon: React.ReactNode;
  label: string;
  /** null = indeterminado (fase en curso sin % conocible). */
  percent: number | null;
  done: boolean;
  active: boolean;
  failed?: boolean;
}) {
  const shown = percent === null ? null : done ? 100 : percent;
  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between text-xs">
        <span className={cn("flex items-center gap-2", (done || active) && "text-foreground")}>
          {icon}
          {label}
        </span>
        <span className="tabular-nums text-muted-foreground">
          {shown === null ? "…" : `${Math.round(shown)}%`}
        </span>
      </div>
      <Progress
        value={shown ?? 0}
        indeterminate={shown === null}
        aria-label={label}
        indicatorClassName={cn(
          failed && "bg-destructive",
          done && !failed && "bg-[var(--success)]",
        )}
      />
    </div>
  );
}
