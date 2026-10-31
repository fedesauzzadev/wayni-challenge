namespace Bcra.Ingestion.Domain.ImportJobs;

/// <summary>
/// Totales precomputados de un import (escritos por la agregación en <c>import_job</c>):
/// lectura de una sola fila, usable justo tras completar sin recount de millones de filas.
/// </summary>
public sealed record ImportJobTotals(long DeudoresCount, long EntidadesCount, decimal SumaTotal);
