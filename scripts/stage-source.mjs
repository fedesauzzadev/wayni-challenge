#!/usr/bin/env node
/**
 * Copia archivos fuente (p. ej. deudoress.txt de ~5.6 GB + Maeent.txt + Nomdeu.txt) al volumen
 * nativo /data-vol del ingestion (source-data), evitando el bind mount de Windows que sirve
 * /data a ~80 MB/s medidos frente a la velocidad de disco del VM — ADR-013.
 *
 *   node scripts/stage-source.mjs <archivo-host> [archivo-host-2 ...]
 *
 * Las referencias (Maeent/Nomdeu) deben acompañar al archivo principal: el ingestion las
 * resuelve en el mismo directorio del source. Al terminar imprime el source container-path
 * listo para POST /imports:
 *   /data-vol/deudoress.txt
 *
 * Solo usa builtins de Node (>= 18). Requiere docker compose up (el ingestion debe existir,
 * aunque puede estar parado: docker cp funciona igual).
 */
import { spawnSync } from "node:child_process";
import { statSync } from "node:fs";
import path from "node:path";

const log = (msg) => console.log(`[stage-source] ${msg}`);
const fail = (msg) => {
  console.error(`[stage-source] ERROR: ${msg}`);
  process.exit(1);
};

const hostFiles = process.argv.slice(2);

if (hostFiles.length === 0) {
  console.log("Uso: node scripts/stage-source.mjs <archivo-host> [archivo-host-2 ...]");
  console.log('Ej:  node scripts/stage-source.mjs "C:/datos/deudoress.txt" "C:/datos/Maeent.txt" "C:/datos/Nomdeu.txt"');
  process.exit(1);
}

const container = process.env.INGESTION_CONTAINER ?? "bcra-ingestion-api";
let firstDestName = null;

for (const hostFile of hostFiles) {
  const destName = path.basename(hostFile);
  const containerPath = `/data-vol/${destName}`;
  firstDestName ??= destName;

  const result = spawnSync("docker", ["cp", hostFile, `${container}:${containerPath}`], {
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  if (result.status !== 0) fail(`docker cp ${hostFile} salió con código ${result.status}`);
  log(`${hostFile} → ${container}:${containerPath}`);
}

log(`Listo. Source para POST /imports: /data-vol/${firstDestName}`);
log(`Size del principal: ${statSync(hostFiles[0]).size} bytes`);
