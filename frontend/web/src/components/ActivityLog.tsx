import { useEffect, useRef } from "react";
import { CheckCircle2, CircleAlert, Info, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";

export type ActivityLevel = "info" | "progress" | "success" | "error";

export interface ActivityEntry {
  at: number;
  level: ActivityLevel;
  message: string;
}

const levelStyles: Record<ActivityLevel, string> = {
  info: "text-muted-foreground",
  progress: "text-primary",
  success: "text-[var(--success)]",
  error: "text-destructive",
};

const levelIcons: Record<ActivityLevel, React.ReactNode> = {
  info: <Info className="size-3.5" />,
  progress: <Loader2 className="size-3.5 animate-spin" />,
  success: <CheckCircle2 className="size-3.5" />,
  error: <CircleAlert className="size-3.5" />,
};

function formatTime(at: number): string {
  return new Date(at).toLocaleTimeString("es-AR", { hour12: false });
}

/**
 * Log de actividad del upload/import: eventos reales (transferencia, entrega, estados del
 * job) con timestamp. Auto-scrolla al último evento y sobrevive F5 (las entries viven en
 * sessionStorage, ver pages/ArchivoPage).
 */
export function ActivityLog({ entries }: { entries: ActivityEntry[] }) {
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // jsdom no implementa scrollIntoView: llamada opcional para no romper tests.
    endRef.current?.scrollIntoView?.({ block: "nearest" });
  }, [entries.length]);

  return (
    <section
      aria-label="Log de actividad"
      className="max-h-52 overflow-y-auto rounded-lg border bg-muted/30 p-3 font-mono text-xs leading-relaxed"
    >
      <ol className="space-y-1.5">
        {entries.map((entry, index) => (
          <li key={`${entry.at}-${index}`} className={cn("flex items-start gap-2", levelStyles[entry.level])}>
            <span className="mt-0.5 shrink-0">{levelIcons[entry.level]}</span>
            <span className="shrink-0 tabular-nums opacity-70">{formatTime(entry.at)}</span>
            <span className="min-w-0 break-words">{entry.message}</span>
          </li>
        ))}
      </ol>
      <div ref={endRef} />
    </section>
  );
}
