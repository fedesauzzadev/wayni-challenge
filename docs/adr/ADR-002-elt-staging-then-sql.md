# ADR-002: ELT (staging crudo + agregación en la base)

**Estado:** Aceptado
**Contexto:** Archivo de ancho fijo de ~6 GB / ~35M registros. Opciones: (a) agregar en memoria del worker y escribir el resultado, (b) cargar crudo a staging y agregar con SQL.
**Decisión:** ELT. Fase 1: streaming + parseo por offsets → `COPY` binario a `debt_staging` en lotes de ~200k líneas. Fase 2: agregación por `INSERT ... SELECT ... GROUP BY` contra el staging, índices al final, swap atómico, cleanup.
**Motivos:**
- Memoria del worker acotada: nunca se acumulan agregados en RAM.
- La agregación es idempotente y re-ejecutable: si falla, se borra el parcial del `import_id` y se re-corre; nunca hay sumas acumuladas.
- Reanudación simple: el checkpoint (`bytes_committed`) solo depende de bytes de entrada confirmados, no de estado intermedio en memoria.
- El motor de BD optimiza mejor sets completos que loops de aplicación.
**Consecuencias:** Se escribe ~2.5 GB extra en disco (staging) por corrida; se limpia tras el swap. Staging LOGGED y sin índices para maximizar throughput de carga (decisión consciente; WAL amplio documentado).
