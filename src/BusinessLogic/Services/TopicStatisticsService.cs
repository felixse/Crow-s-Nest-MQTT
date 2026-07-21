using System.Collections.Concurrent;
using CrowsNestMqtt.BusinessLogic.Models;

namespace CrowsNestMqtt.BusinessLogic.Services;

/// <inheritdoc />
public sealed class TopicStatisticsService : ITopicStatisticsService
{
    private sealed class Entry
    {
        public long Count;
        public long TotalBytes;
        public DateTime First;
        public DateTime Last;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Record(string topic, int payloadBytes, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var size = payloadBytes < 0 ? 0 : payloadBytes;

        var entry = _entries.GetOrAdd(topic, static _ => new Entry());
        lock (entry)
        {
            if (entry.Count == 0)
            {
                entry.First = timestampUtc;
            }
            entry.Count++;
            entry.TotalBytes += size;
            entry.Last = timestampUtc;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TopicStatisticsSnapshot> Snapshot()
    {
        // Materialize under per-entry locks to guarantee internal consistency
        // (Count / First / Last / TotalBytes read together for the same message set).
        var snapshots = new List<TopicStatisticsSnapshot>(_entries.Count);

        foreach (var kvp in _entries)
        {
            var entry = kvp.Value;
            long count;
            long total;
            DateTime first;
            DateTime last;
            lock (entry)
            {
                count = entry.Count;
                total = entry.TotalBytes;
                first = entry.First;
                last = entry.Last;
            }

            if (count <= 0)
            {
                continue;
            }

            var average = (double)total / count;
            TimeSpan? mean = null;
            if (count >= 2)
            {
                var span = last - first;
                // Divide TimeSpan by (count - 1). Guard against negative durations
                // that could theoretically arise from clock skew.
                if (span < TimeSpan.Zero)
                {
                    span = TimeSpan.Zero;
                }
                mean = TimeSpan.FromTicks(span.Ticks / (count - 1));
            }

            snapshots.Add(new TopicStatisticsSnapshot(
                kvp.Key,
                count,
                total,
                average,
                mean,
                first,
                last));
        }

        return snapshots;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _entries.Clear();
    }
}
