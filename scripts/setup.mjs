#!/usr/bin/env node
/**
 * Setup único del entorno de desarrollo del challenge (spec docs/plan: admin-ui, paso 6).
 *
 * 1. Valida/copia `.env` (desde `.env.example` si no existe).
 * 2. `docker compose up -d --build`: postgres, localstack, ingestion, query, notifier,
 *    mailpit, prometheus y grafana.
 * 3. Espera los healthchecks (`/health/live` de ingestion/query/notifier) con timeout.
 * 4. `npm install` en `frontend/bff` y `frontend/web`.
 * 5. Arranca BFF (:4000) y web Vite (:5173) en paralelo y abre el browser en la web.
 *
 * Solo usa builtins de Node (>= 18): sin dependencias que instalar en la raíz.
 */
import { spawn, spawnSync } from "node:child_process";
import { existsSync, copyFileSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const log = (msg) => console.log(`[setup] ${msg}`);
const fail = (msg) => {
  console.error(`[setup] ERROR: ${msg}`);
  process.exit(1);
};

function parseEnv(file) {
  const vars = {};
  if (!existsSync(file)) return vars;
  for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
    const match = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/);
    if (match) vars[match[1]] = match[2];
  }
  return vars;
}

function run(cmd, args, opts = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, { stdio: "inherit", shell: process.platform === "win32", ...opts });
    child.on("exit", (code) => (code === 0 ? resolve() : reject(new Error(`${cmd} ${args.join(" ")} salió con código ${code}`))));
    child.on("error", reject);
  });
}

async function waitForHttp(name, url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  process.stdout.write(`[setup] Esperando ${name} en ${url} `);
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        console.log("OK");
        return;
      }
    } catch {
      // todavía no levanta
    }
    process.stdout.write(".");
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  fail(`timeout esperando ${name} (${url}). Revisá "docker compose ps" y "docker compose logs".`);
}

function openBrowser(url) {
  try {
    if (process.platform === "win32") {
      spawn("cmd", ["/c", "start", "", url], { detached: true, stdio: "ignore" }).unref();
    } else if (process.platform === "darwin") {
      spawn("open", [url], { detached: true, stdio: "ignore" }).unref();
    } else {
      spawn("xdg-open", [url], { detached: true, stdio: "ignore" }).unref();
    }
  } catch {
    log(`No pude abrir el browser: navegá a ${url}`);
  }
}

const envFile = path.join(root, ".env");
if (!existsSync(envFile)) {
  copyFileSync(path.join(root, ".env.example"), envFile);
  log("No había .env: se creó desde .env.example (ajustá secretos si querés).");
} else {
  log(".env encontrado.");
}

const env = { ...parseEnv(envFile) };
const ingestionPort = env.INGESTION_API_PORT ?? "18080";
const queryPort = env.QUERY_API_PORT ?? "18081";
const notifierPort = env.NOTIFIER_API_PORT ?? "18082";
const bffPort = env.BFF_PORT ?? "4000";
const webPort = env.WEB_PORT ?? "5173";

log("Levantando el stack docker (build incluido; la primera vez tarda unos minutos)…");
await run("docker", ["compose", "up", "-d", "--build"], { cwd: root });

await waitForHttp("ingestion", `http://localhost:${ingestionPort}/health/live`, 180_000);
await waitForHttp("query", `http://localhost:${queryPort}/health/live`, 120_000);
await waitForHttp("notifier", `http://localhost:${notifierPort}/health/live`, 120_000);

log("Instalando dependencias del BFF y la web…");
await run("npm", ["install"], { cwd: path.join(root, "frontend", "bff") });
await run("npm", ["install"], { cwd: path.join(root, "frontend", "web") });

log(`Arrancando BFF (:${bffPort}) y web (:${webPort})…`);
// El .env ya tiene lo que el BFF requiere en runtime (UPLOAD_API_KEY, POSTGRES_PASSWORD…):
// se pasa a los hijos junto con DATA_DIR resuelta, así setup funciona desde cualquier shell
// sin exports manuales. process.env gana por si el usuario sobreescribe algo ad-hoc.
const childEnv = {
  ...env,
  ...process.env,
  BFF_PORT: bffPort,
  WEB_PORT: webPort,
  DATA_DIR: path.resolve(root, env.DATA_DIR ?? "samples"),
};
const children = [
  spawn("npm", ["run", "dev"], {
    cwd: path.join(root, "frontend", "bff"),
    stdio: ["ignore", "pipe", "pipe"],
    shell: process.platform === "win32",
    env: childEnv,
  }),
  spawn("npm", ["run", "dev"], {
    cwd: path.join(root, "frontend", "web"),
    stdio: ["ignore", "pipe", "pipe"],
    shell: process.platform === "win32",
    env: childEnv,
  }),
];
for (const [index, child] of children.entries()) {
  const tag = index === 0 ? "bff" : "web";
  const pipe = (stream) =>
    stream.on("data", (chunk) =>
      process.stdout.write(String(chunk).split(/\r?\n/).filter(Boolean).map((line) => `[${tag}] ${line}\n`).join("")),
    );
  pipe(child.stdout);
  pipe(child.stderr);
}

process.on("SIGINT", () => {
  for (const child of children) child.kill("SIGINT");
  process.exit(0);
});

await waitForHttp("BFF", `http://localhost:${bffPort}/health`, 60_000);
log("Todo listo. Abriendo la admin UI…");
openBrowser(`http://localhost:${webPort}`);
console.log(`
[setup] Admin UI:   http://localhost:${webPort}
[setup] BFF:        http://localhost:${bffPort}
[setup] Mailpit:    http://localhost:${env.MAILPIT_UI_PORT ?? "8025"}
[setup] Grafana:    http://localhost:${env.GRAFANA_PORT ?? "3000"}
[setup] Ctrl+C detiene BFF y web (el stack docker queda corriendo: "docker compose down" para pararlo).
`);
