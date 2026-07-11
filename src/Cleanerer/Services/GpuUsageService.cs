using System.Diagnostics;

namespace Cleanerer.Services;

/// <summary>
/// Reads overall GPU utilization from the "GPU Engine" performance counters, the same source
/// Task Manager's GPU column uses: per-process/per-engine "Utilization Percentage" instances are
/// summed per engine type, and the busiest engine type (typically 3D) is the headline number.
///
/// Counter creation is expensive (hundreds of instances) and the instance list churns as
/// processes come and go, so counters are (re)built lazily off the UI thread at most every
/// <see cref="InstanceRefreshInterval"/>, and <see cref="TryRead"/> returns null until the first
/// build completes or when the machine has no GPU Engine counters at all (old GPU / broken
/// counter store). Callers hide the stat when null. Never throws.
/// </summary>
public sealed class GpuUsageService
{
    private static readonly TimeSpan InstanceRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private Dictionary<string, List<PerformanceCounter>>? _countersByEngineType;
    private DateTime _lastRefresh;
    private bool _building;
    private bool _unavailable;

    /// <summary>
    /// Current GPU load percent (0-100), or null while counters are still initializing or when
    /// GPU counters don't exist on this machine. Cheap after the first background build.
    /// </summary>
    public int? TryRead()
    {
        if (_unavailable)
        {
            return null;
        }

        Dictionary<string, List<PerformanceCounter>>? counters;
        lock (_gate)
        {
            counters = _countersByEngineType;

            bool stale = DateTime.UtcNow - _lastRefresh > InstanceRefreshInterval;
            if ((counters is null || stale) && !_building)
            {
                _building = true;
                _ = Task.Run(RebuildCounters);
            }
        }

        if (counters is null || counters.Count == 0)
        {
            return null;
        }

        double busiest = 0;
        foreach (List<PerformanceCounter> engine in counters.Values)
        {
            double total = 0;
            foreach (PerformanceCounter counter in engine)
            {
                try
                {
                    total += counter.NextValue();
                }
                catch
                {
                    // Instance disappeared (its process exited); the periodic rebuild prunes it.
                }
            }

            busiest = Math.Max(busiest, total);
        }

        return (int)Math.Clamp(busiest, 0, 100);
    }

    private void RebuildCounters()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _unavailable = true;
                return;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var fresh = new Dictionary<string, List<PerformanceCounter>>(StringComparer.OrdinalIgnoreCase);

            foreach (string instance in category.GetInstanceNames())
            {
                // Instance names end in "..._engtype_3D", "..._engtype_Copy", etc.
                int marker = instance.LastIndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                string engineType = marker >= 0 ? instance.Substring(marker) : "engtype_Unknown";

                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                counter.NextValue(); // prime: first sample of a rate counter is always 0

                if (!fresh.TryGetValue(engineType, out List<PerformanceCounter>? list))
                {
                    fresh[engineType] = list = new List<PerformanceCounter>();
                }

                list.Add(counter);
            }

            lock (_gate)
            {
                Dictionary<string, List<PerformanceCounter>>? old = _countersByEngineType;
                _countersByEngineType = fresh;
                _lastRefresh = DateTime.UtcNow;

                if (old is not null)
                {
                    foreach (List<PerformanceCounter> list in old.Values)
                    {
                        foreach (PerformanceCounter counter in list)
                        {
                            counter.Dispose();
                        }
                    }
                }
            }
        }
        catch
        {
            // Counter store broken or access denied: give up quietly, the UI hides the stat.
            _unavailable = true;
        }
        finally
        {
            lock (_gate)
            {
                _building = false;
            }
        }
    }
}
