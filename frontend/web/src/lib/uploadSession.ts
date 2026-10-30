import type { ActivityEntry } from "@/components/ActivityLog";

export interface UploadSession {
  /** Id del import una vez encolado; null mientras la transferencia está en vuelo. */
  importId: string | null;
  fileName: string;
  fileSize: number;
  storage: "local" | "s3";
  startedAt: number;
}

const SESSION_KEY = "bcra-upload-session";
const LOG_KEY = "bcra-upload-log";
const MAX_LOG_ENTRIES = 40;

/**
 * El upload y el seguimiento del import sobreviven F5/cambio de pestaña: el import sigue
 * corriendo en el ingestion aunque el browser pierda el estado, así que persistimos el id
 * activo + el log en sessionStorage (por pestaña) para retomar el polling al volver.
 */
export function loadSession(): UploadSession | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as UploadSession;
    if (typeof parsed.fileName !== "string" || typeof parsed.fileSize !== "number") return null;
    return parsed;
  } catch {
    return null;
  }
}

export function saveSession(session: UploadSession): void {
  try {
    sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
  } catch {
    // storage deshabilitado: la UI sigue funcionando, solo sin recuperación
  }
}

export function clearSession(): void {
  try {
    sessionStorage.removeItem(SESSION_KEY);
  } catch {
    // noop
  }
}

export function loadLog(): ActivityEntry[] {
  try {
    const raw = sessionStorage.getItem(LOG_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as ActivityEntry[];
    return Array.isArray(parsed) ? parsed.slice(-MAX_LOG_ENTRIES) : [];
  } catch {
    return [];
  }
}

export function saveLog(entries: ActivityEntry[]): void {
  try {
    sessionStorage.setItem(LOG_KEY, JSON.stringify(entries.slice(-MAX_LOG_ENTRIES)));
  } catch {
    // noop
  }
}
