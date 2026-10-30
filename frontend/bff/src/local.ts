import { copyFile, mkdir } from "node:fs/promises";
import path from "node:path";
import type { AppConfig } from "./config.js";

export interface LocalUploadResult {
  source: string;
}

export type LocalUploader = (filePath: string, size: number, key: string) => Promise<LocalUploadResult>;

/**
 * Copia el archivo temporal de multer al dir compartido con el ingestion (DATA_DIR montado
 * read-only como /data en el container): el pipeline lo lee con LocalFileSource usando el
 * mismo layout de keys que el modo S3 (uploads/<periodo>/...).
 */
export function createLocalUploader(config: AppConfig): LocalUploader {
  return async (filePath, _size, key) => {
    const destination = path.join(config.localUpload.dataDir, ...key.split("/"));
    await mkdir(path.dirname(destination), { recursive: true });
    await copyFile(filePath, destination);
    return { source: `${config.localUpload.ingestionSourceRoot.replace(/\/+$/, "")}/${key}` };
  };
}
