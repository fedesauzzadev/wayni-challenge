#!/usr/bin/env node
/**
 * Reconstruye y reinicia servicios del stack docker con un solo comando:
 *
 *   npm run rebuild              # ingestion-api, query-api y notifier-api (los 3 .NET)
 *   npm run rebuild ingestion    # solo ingestion-api
 *   npm run rebuild notifier query
 *
 * A diferencia de "docker compose up -d --build" (que rebuilda TODO, incluida la imagen
 * base), esto solo reconstruye los servicios pedidos, los reinicia sin tocar sus
 * dependencias (--no-deps) y espera el healthcheck antes de devolver el control.
 * Pensado para el loop: cambiás código .NET → npm run rebuild → probás.
 */
import { spawn } from "node:child_process";
import { existsSync, copyFileSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const log = (msg) => console.log(`[rebuild] ${msg}`);
const fail = (msg) => {
  console.error(`[rebuild] ERROR: ${msg}`);
  process.exit(1);
};

/** Servicios .NET reconstruibles → puerto del healthcheck (del .env si está). */
const rebuildable = {
  ingestion: { service: "ingestion-api", portVar: "INGESTION_API_PORT", defaultPort: 18080 },
  query: { service: "query-api", portVar: "QUERY_API_PORT", defaultPort: 18081 },
  notifier: { service: "notifier-api", portVar: "NOTIFIER_API_PORT", defaultPort: 18082 },
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

function run(cmd, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, { stdio: "inherit", shell: process.platform === "win32", cwd: root });
    child.on("exit", (code) => (code === 0 ? resolve() : reject(new Error(`${cmd} ${args.join(" ")} salió con código ${code}`))));
    child.on("error", reject);
  });
}

async function waitForHttp(name, url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  process.stdout.write(`[rebuild] Esperando ${name} en ${url} `);
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
  fail(`timeout esperando ${name} (${url}). Revisá "docker compose logs ${name}".`);
}

const envFile = path.join(root, ".env");
if (!existsSync(envFile)) {
  copyFileSync(path.join(root, ".env.example"), envFile);
  log("No había .env: se creó desde .env.example.");
}
const env = parseEnv(envFile);

const requested = process.argv.slice(2);
if (requested.length === 0) {
  Object.keys(rebuildable).forEach((key) => requested.push(key));
}

const targets = [];
for (const key of requested) {
  const target = rebuildable[key];
  if (!target) fail(`Servicio desconocido "${key}". Válidos: ${Object.keys(rebuildable).join(", ")}.`);
  targets.push(target);
}

const serviceNames = targets.map((target) => target.service).join(", ");
log(`Reconstruyendo ${serviceNames}…`);
await run("docker", ["compose", "build", ...targets.map((target) => target.service)]);
await run("docker", ["compose", "up", "-d", "--no-deps", ...targets.map((target) => target.service)]);

for (const target of targets) {
  const port = env[target.portVar] ?? target.defaultPort;
  await waitForHttp(target.service, `http://localhost:${port}/health/live`, 180_000);
}

log(`Listo: ${serviceNames} reconstruidos y saludables.`);
