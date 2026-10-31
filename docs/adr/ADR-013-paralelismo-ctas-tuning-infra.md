# ADR-013: Paralelismo del GROUP BY vía CTAS, índices concurrentes y tuning de infraestructura

- **Estado**: Aceptado y medido — se cierra la optimización en este punto
- **Contexto**: ADR-012 cerró la optimización del import en ~689 s (~11.5 min: staging 251.5 s, agregación 298.9 s, índices 137.4 s) al rechazar pre-agregación en RAM, staging UNLOGGED y más memoria. Se reabrió con la restricción de **no tocar arquitectura, contratos ni protocolos**, a partir de cuatro mediciones sobre el entorno real:
  1. **La agregación corría serial**: `EXPLAIN` confirma que `INSERT INTO ... SELECT ... GROUP BY` nunca genera plan paralelo en PostgreSQL (el nodo ModifyTable lo prohíbe) — 298.9 s del pipeline en 1 de los 8 cores de la VM. El mismo GROUP BY como `CREATE TABLE AS` sí paraleliza.
  2. **Benchmark a mitad de escala** (17.2M filas / 10M grupos, misma forma que el archivo real): serial `INSERT...SELECT` 211.9 s vs `CREATE UNLOGGED TABLE AS` con scan paralelo 4 workers 71.9 s + append 58.6 s = **131.3 s (−38 %)**. Nota: con grupos ≈ filas el planner elige serial salvo con `parallel_setup_cost=0; parallel_tuple_cost=0.001` session-scoped.
  3. **El bind mount de Windows sirve el archivo a 80.9 MB/s** (5.6 GB ≈ 69 s solo de lectura); LocalStack S3 tampoco es salida: PUT 1 GB ≈ 54 MB/s, GET ≈ 94 MB/s medidos.
  4. `maintenance_work_mem` de 512 MB quedaba corto para el sort del índice ranking (~20M filas arrastrando `numeric`): el ADR-012 ya lo marcaba como el único nodo sensible.
- **Decisión** (todo session-scoped o de infra; cero cambios de arquitectura/protocolo):
  - **`DapperImportJobRepository.AggregateImportAsync`**: el GROUP BY pasa por `CREATE UNLOGGED TABLE agg_tmp_{hex} AS SELECT ... GROUP BY` + `INSERT ... SELECT` desde la temporaria + `DROP`, con `SET max_parallel_workers_per_gather=4; SET parallel_setup_cost=0; SET parallel_tuple_cost=0.001` en la sesión. Todo dentro de la misma transacción READ COMMITTED de siempre.
  - **`CreateImportIndexesAsync`**: los 3 `CREATE INDEX` en 3 conexiones concurrentes (`Task.WhenAll`) y `maintenance_work_mem` 512 MB → 2 GB (session-scoped como antes).
  - **`docker-compose.yml`**: `postgres:17.5-alpine` (streaming I/O de seq scans, automático en 17 sin GUC — `io_method` es de PG18 y se validó que 17.5 lo rechaza), `wal_compression=lz4`, `max_wal_size` 4→8 GB + `checkpoint_timeout` 15→30 min, `synchronous_commit=off` (dev).
  - **Volumen nativo `source-data` montado en `/data-vol`** del ingestion (allow-listado) + `scripts/stage-source.mjs` que copia el archivo grande con `docker cp` (cruza el puente Windows→VM una sola vez).
- **Por qué no rompe garantías**: la transacción, la idempotencia (`DELETE` por `import_id` primero) y el swap quedan idénticos; las temporarias UNLOGGED son transaccionales (un crash las desaparece junto con su contenido, no hay ventana de agregados parciales publicados — a diferencia del staging UNLOGGED rechazado en ADR-012, que rompía el checkpoint atómico lote+confirmación); `synchronous_commit=off` es seguro para el protocolo de lote porque lote y checkpoint se pierden **juntos** (misma transacción): el takeover retoma del checkpoint anterior y re-copia el lote, sin sumas parciales. Es dev-only: un crash puede perder hasta ~3×`wal_writer_delay` de commits de cualquier sesión.
- **Medición final** (dataset real, 34.4M de líneas, dos corridas consecutivas subiendo por la web con entrega "Directo" — bind mount):

  | Fase | ADR-011 (base) | ADR-013 | Delta |
  |---|---|---|---|
  | Staging | 251.5 s | 255.8 s | +4.3 s (siguió leyendo del bind mount a ~80 MB/s) |
  | Agregación | 298.9 s | 230.4 s | **−68.5 s** (CTAS paralelo verificado: 4 workers activos en PG17) |
  | Índices | 137.4 s | 146.0 s | +8.6 s (la concurrencia no paga en la VM compartida) |
  | **Job end-to-end** | **~689 s** | **624 s (10:24)** | **−65 s (−9.4 %)** |

  La primera corrida tras el reprovisionado a PG17 dio 880 s (fría: cluster recién inicializado, page cache vacío, primera escritura de 34M de filas) — no comparable. El benchmark sintético (−38 %) sobreestimó la ganancia real: en producción el `HashAggregate` del leader spillea a disco (techo efectivo `work_mem` 2 GB × `hash_mem_multiplier` 2 = 4 GB, insuficiente para ~20M de grupos) y el append convive con los ~20M de filas del import anterior hasta el swap.
- **Cierre — palancas restantes identificadas y rechazadas por no justificar el costo** (ganancia total ~1 min más, decimosión del usuario: no vale la pena):
  - `hash_mem_multiplier=4` en la sesión de agregación: eliminaría el spill del hash (~10-30 s) al costo de un pico de ~8 GB de RAM en una VM de 15.5 GB compartida con otros stacks.
  - Revertir los índices concurrentes: +8.6 s está dentro del ruido de una VM que comparte 8 cores con otros proyectos.
  - Entregar el archivo por `/data-vol` (docker cp) en vez del upload web: ahorraría ~40-60 s de staging, pero rompe el flujo 100 % web y LocalStack S3 (~94 MB/s GET) tampoco es salida rápida.
- **Consecuencias**: el end-to-end queda en **~10.5 min** (624 s medidos en la segunda corrida). El piso de **tuning** en este hardware (VM compartida, mono-worker por diseño ADR-006) es ~9-9.5 min; bajar de ahí requiere las palancas de arquitectura de ADR-011 (particionar `deudor_agg`/`entidad_agg` por `LIST(import_id)`, staging paralelo por rangos de bytes), que siguen condicionadas a los gatillos de cadencia/SLA/crecimiento. Los gatillos de ADR-011 siguen vigentes.
