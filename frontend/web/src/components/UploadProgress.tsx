import { CheckCircle2, CloudUpload, Loader2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/utils";

export type UploadPhase = "uploading" | "processing" | "completed" | "failed";

export interface UploadProgressProps {
  phase: UploadPhase;
  /** 0-100 de la fase (a): subida del archivo al BFF/S3. */
  uploadPercent: number;
  /** 0-100 de la fase (b): progreso del import (loadingProgressPercent del backend). */
  processingPercent: number | null;
  fileName: string;
  fileSize: number;
  statusText: string;
}

const phaseLabels: Record<UploadPhase, string> = {
  uploading: "Subiendo archivo a S3",
  processing: "Procesando import",
  completed: "Import completado",
  failed: "El import falló",
};

export function UploadProgress({
  phase,
  uploadPercent,
  processingPercent,
  fileName,
  fileSize,
  statusText,
}: UploadProgressProps) {
  const uploadingDone = phase !== "uploading";
  const globalProcessing = processingPercent ?? 0;

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
        label="Subida a S3 (LocalStack)"
        done={uploadingDone}
        active={phase === "uploading"}
        percent={uploadPercent}
      />
      <PhaseRow
        icon={<Loader2 className={cn("size-4", phase === "processing" && "animate-spin")} />}
        label="Procesamiento (staging → agregación → swap)"
        done={phase === "completed"}
        active={phase === "processing"}
        percent={uploadingDone ? globalProcessing : 0}
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
  percent: number;
  done: boolean;
  active: boolean;
  failed?: boolean;
}) {
  const shown = done ? 100 : percent;
  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between text-xs">
        <span className={cn("flex items-center gap-2", (done || active) && "text-foreground")}>
          {icon}
          {label}
        </span>
        <span className="tabular-nums text-muted-foreground">{Math.round(shown)}%</span>
      </div>
      <Progress
        value={shown}
        aria-label={label}
        indicatorClassName={cn(
          failed && "bg-destructive",
          done && !failed && "bg-[var(--success)]",
        )}
      />
    </div>
  );
}
