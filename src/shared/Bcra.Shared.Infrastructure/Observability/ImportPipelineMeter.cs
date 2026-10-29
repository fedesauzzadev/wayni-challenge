using System.Diagnostics.Metrics;

namespace Bcra.Shared.Infrastructure.Observability;

/// <summary>
/// Métricas custom del pipeline de import (meter <see cref="MeterName"/>), exportadas a Prometheus
/// con el prefijo <c>bcra_import_*</c>: líneas COPY-eadas y rechazadas, lotes confirmados,
/// duración por fase (staging / agregación / índices / swap / total) y jobs por estado y resultado.
/// </summary>
public sealed class ImportPipelineMeter : IDisposable
{
    public const string MeterName = "Bcra.ImportPipeline";

    public const string PhaseStaging = "staging";
    public const string PhaseAggregation = "aggregation";
    public const string PhaseIndexes = "indexes";
    public const string PhaseSwap = "swap";
    public const string PhaseTotal = "total";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _linesCopied;
    private readonly Counter<long> _linesRejected;
    private readonly Counter<long> _batchesCommitted;
    private readonly Counter<long> _jobsByOutcome;
    private readonly Histogram<double> _phaseDuration;
    private readonly ObservableGauge<int> _jobsByStatus;
    private readonly object _jobsByStatusLock = new();
    private Dictionary<string, int> _jobsByStatusSnapshot = [];

    public ImportPipelineMeter()
    {
        _linesCopied = _meter.CreateCounter<long>("bcra.import.lines_copied", unit: "{lines}", description: "Líneas válidas COPY-eadas a staging");
        _linesRejected = _meter.CreateCounter<long>("bcra.import.lines_rejected", unit: "{lines}", description: "Líneas rechazadas por el parser");
        _batchesCommitted = _meter.CreateCounter<long>("bcra.import.batches_committed", unit: "{batches}", description: "Lotes confirmados a staging (COPY + checkpoint atómicos)");
        _jobsByOutcome = _meter.CreateCounter<long>("bcra.import.jobs", unit: "{jobs}", description: "Imports finalizados por resultado (completed/failed)");
        _phaseDuration = _meter.CreateHistogram<double>("bcra.import.phase_duration", unit: "s", description: "Duración de cada fase del import");
        _jobsByStatus = _meter.CreateObservableGauge<int>(
            "bcra.import.jobs_by_status",
            ObserveJobsByStatus,
            unit: "{jobs}",
            description: "Jobs de import por estado actual (snapshot refrescado por el worker)");
    }

    public void AddLinesCopied(long lines) => _linesCopied.Add(lines);

    public void AddLinesRejected(long lines) => _linesRejected.Add(lines);

    public void AddBatchCommitted() => _batchesCommitted.Add(1);

    public void AddJobOutcome(string outcome) => _jobsByOutcome.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordPhaseDuration(string phase, TimeSpan duration) =>
        _phaseDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("phase", phase));

    /// <summary>Reemplaza el snapshot de jobs por estado que publica el gauge observable.</summary>
    public void UpdateJobsByStatus(IReadOnlyDictionary<string, int> countsByStatus)
    {
        lock (_jobsByStatusLock)
        {
            _jobsByStatusSnapshot = new Dictionary<string, int>(countsByStatus, StringComparer.Ordinal);
        }
    }

    private IEnumerable<Measurement<int>> ObserveJobsByStatus()
    {
        lock (_jobsByStatusLock)
        {
            return _jobsByStatusSnapshot.Count == 0
                ? []
                : _jobsByStatusSnapshot.Select(kv => new Measurement<int>(kv.Value, new KeyValuePair<string, object?>("status", kv.Key))).ToArray();
        }
    }

    public void Dispose() => _meter.Dispose();
}
