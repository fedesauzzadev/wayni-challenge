import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export interface AppConfig {
  port: number;
  ingestionApiUrl: string;
  queryApiUrl: string;
  uploadApiKey: string;
  s3: {
    endpoint: string;
    region: string;
    bucket: string;
    accessKeyId: string;
    secretAccessKey: string;
  };
  localUpload: {
    /** Dir del HOST compartido con el ingestion vía volumen (DATA_DIR del compose). */
    dataDir: string;
    /** Path del mismo dir visto DENTRO del container del ingestion (AllowedSourceDirs). */
    ingestionSourceRoot: string;
  };
  postgres: {
    host: string;
    port: number;
    database: string;
    user: string;
    password: string;
  };
}

function required(env: Record<string, string | undefined>, key: string): string {
  const value = env[key];
  if (!value || value.trim() === "") {
    throw new Error(`Missing required environment variable ${key}`);
  }
  return value;
}

/**
 * Camina hacia arriba desde este archivo hasta encontrar el docker-compose.yml: el default
 * de DATA_DIR tiene que resolver al <root>/samples que el compose monta como /data en el
 * ingestion, sin importar desde qué CWD se arranque el BFF.
 */
function defaultDataDir(): string {
  let dir = path.dirname(fileURLToPath(import.meta.url));
  while (true) {
    if (existsSync(path.join(dir, "docker-compose.yml"))) return path.join(dir, "samples");
    const parent = path.dirname(dir);
    if (parent === dir) return path.resolve("./samples");
    dir = parent;
  }
}

/** Toda la config vive en el BFF (nunca en el front): API keys, credenciales AWS y DB. */
export function loadConfig(env: Record<string, string | undefined> = process.env): AppConfig {
  return {
    port: Number(env.BFF_PORT ?? 4000),
    ingestionApiUrl: env.INGESTION_API_URL ?? "http://localhost:18080",
    queryApiUrl: env.QUERY_API_URL ?? "http://localhost:18081",
    uploadApiKey: env.UPLOAD_API_KEY ?? required(env, "UPLOAD_API_KEY"),
    s3: {
      endpoint: env.S3_ENDPOINT ?? `http://localhost:${env.LOCALSTACK_PORT ?? 4566}`,
      region: env.AWS_REGION ?? "us-east-1",
      bucket: env.S3_BUCKET ?? "bcra-local",
      accessKeyId: env.AWS_ACCESS_KEY_ID ?? "test",
      secretAccessKey: env.AWS_SECRET_ACCESS_KEY ?? "test",
    },
    localUpload: {
      dataDir: env.DATA_DIR ?? defaultDataDir(),
      ingestionSourceRoot: env.INGESTION_SOURCE_ROOT ?? "/data",
    },
    postgres: {
      host: env.POSTGRES_HOST ?? "localhost",
      port: Number(env.POSTGRES_PORT ?? 5544),
      database: env.POSTGRES_DB ?? "bcra",
      user: env.POSTGRES_USER ?? "bcra",
      password: required(env, "POSTGRES_PASSWORD"),
    },
  };
}
