import axios from "axios";
import type {
  DeudoresPage,
  EntidadesPage,
  ImportStatus,
  LatestImport,
  Recipient,
  Totales,
  UploadResult,
} from "./types";

const http = axios.create({ baseURL: "/api" });

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

function problemMessage(body: unknown): string {
  if (body && typeof body === "object") {
    const problem = body as { title?: string; detail?: string; error?: string };
    if (problem.detail) return `${problem.title ?? "Error"}: ${problem.detail}`;
    if (problem.title) return problem.title;
    if (problem.error) return problem.error;
  }
  return "Error inesperado";
}

async function unwrap<T>(promise: Promise<{ data: T }>): Promise<T> {
  try {
    const response = await promise;
    return response.data;
  } catch (error) {
    if (axios.isAxiosError(error)) {
      throw new ApiError(error.response?.status ?? 0, problemMessage(error.response?.data));
    }
    throw error;
  }
}

export const api = {
  latestImport: () => unwrap(http.get<{ latest: LatestImport | null }>("/imports/latest")),

  importStatus: (id: string) => unwrap(http.get<ImportStatus>(`/imports/${id}`)),

  uploadFile: (
    file: File,
    periodo: number,
    onUploadProgress: (percent: number) => void,
    references: { maeent?: File | null; nomdeu?: File | null } = {},
    storage: "local" | "s3" = "local",
  ): Promise<UploadResult> =>
    unwrap<UploadResult>(
      http.postForm<UploadResult>(
        "/imports/upload",
        {
          file,
          periodo: String(periodo),
          storage,
          ...(references.maeent ? { maeent: references.maeent } : {}),
          ...(references.nomdeu ? { nomdeu: references.nomdeu } : {}),
        },
        {
          onUploadProgress: (event) => {
            // Fase (a): subida del archivo al BFF/S3. axios no calcula progress en bytes totales
            // desconocidos; el content-length lo setea el FormData del browser.
            onUploadProgress(event.total ? Math.round((event.loaded / event.total) * 100) : 0);
          },
        },
      ),
    ),

  deudores: (offset: number, limit: number, situacionMaxima?: number) =>
    unwrap(
      http.get<DeudoresPage>("/deudores", {
        params: { offset, limit, ...(situacionMaxima ? { situacionMaxima } : {}) },
      }),
    ),

  entidades: (codigo?: string) =>
    unwrap(http.get<EntidadesPage>("/entidades", { params: codigo ? { codigo } : {} })),

  totales: () => unwrap(http.get<Totales>("/totales")),

  recipients: () => unwrap(http.get<{ recipients: Recipient[] }>("/notification-recipients")),

  addRecipient: (email: string) =>
    unwrap(http.post<{ recipient: Recipient; alreadyExisted: boolean }>("/notification-recipients", { email })),

  removeRecipient: (id: number) => unwrap(http.delete<void>(`/notification-recipients/${id}`)),
};
