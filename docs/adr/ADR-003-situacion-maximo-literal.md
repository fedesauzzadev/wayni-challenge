# ADR-003: situacion_maxima = máximo literal del campo 6 (estrategia por defecto)

**Estado:** Aceptado
**Contexto:** El enunciado pide `situacion_maxima = máximo literal del campo 6`. Los códigos BCRA no ordenan por valor numérico de forma lineal con la severidad (1 < 21 < 23 < 3 < 4 < 5, y 11 es un caso especial con garantías).
**Decisión:** Strategy pattern (`ISituacionAggregationStrategy`) que genera la expresión SQL de agregación.
- `max` (default): `MAX(situacion)` — fiel al enunciado ("máximo literal").
- `peor_situacion`: `MAX` sobre ranking de severidad (1→21→23→3→4→5→11) con traducción inversa al código original.
Se selecciona por config (`Ingestion:SituacionStrategy`), sin recompilar.
**Motivos:** El enunciado fija el default; la alternativa de ranking queda disponible para casos donde "peor" no coincida con "máximo literal". El Strategy encapsula la diferencia sin tocar el pipeline de agregación (OCP).
**Consecuencias:** La estrategia es parte del contrato del job; cambiarla entre corridas no reescribe historia (cada `import_id` guarda su propio resultado).
