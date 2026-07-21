using ReactiveUI;

namespace CrowsNestMqtt.UI.ViewModels;

/// <summary>
/// A single row in the statistics window's DataGrid. Values are recomputed
/// in place when a new snapshot arrives so the DataGrid's sort/selection
/// state is preserved.
/// </summary>
public class TopicStatsRowViewModel : ReactiveObject
{
    private long _messageCount;
    private long _totalPayloadBytes;
    private double _averagePayloadBytes;
    private TimeSpan? _meanInterval;

    public TopicStatsRowViewModel(string topic)
    {
        Topic = topic;
    }

    /// <summary>The MQTT topic these statistics belong to.</summary>
    public string Topic { get; }

    /// <summary>Total number of messages recorded for the topic.</summary>
    public long MessageCount
    {
        get => _messageCount;
        set => this.RaiseAndSetIfChanged(ref _messageCount, value);
    }

    /// <summary>Sum of payload byte counts across all recorded messages.</summary>
    public long TotalPayloadBytes
    {
        get => _totalPayloadBytes;
        set
        {
            if (_totalPayloadBytes != value)
            {
                this.RaiseAndSetIfChanged(ref _totalPayloadBytes, value);
                this.RaisePropertyChanged(nameof(TotalPayloadDisplay));
            }
        }
    }

    /// <summary>Mean payload size in bytes.</summary>
    public double AveragePayloadBytes
    {
        get => _averagePayloadBytes;
        set
        {
            if (Math.Abs(_averagePayloadBytes - value) > double.Epsilon)
            {
                this.RaiseAndSetIfChanged(ref _averagePayloadBytes, value);
                this.RaisePropertyChanged(nameof(AveragePayloadDisplay));
            }
        }
    }

    /// <summary>Mean time between successive messages; null when count &lt; 2.</summary>
    public TimeSpan? MeanInterval
    {
        get => _meanInterval;
        set
        {
            if (_meanInterval != value)
            {
                this.RaiseAndSetIfChanged(ref _meanInterval, value);
                this.RaisePropertyChanged(nameof(MeanIntervalDisplay));
                this.RaisePropertyChanged(nameof(MeanIntervalSortKey));
            }
        }
    }

    /// <summary>Formatted total-size string for display (e.g. "1.23 MB").</summary>
    public string TotalPayloadDisplay => StatsFormatting.FormatBytes(TotalPayloadBytes);

    /// <summary>Formatted average-size string for display.</summary>
    public string AveragePayloadDisplay => StatsFormatting.FormatBytes(AveragePayloadBytes);

    /// <summary>Formatted mean-interval string for display (e.g. "250 ms"), or "N/A".</summary>
    public string MeanIntervalDisplay => StatsFormatting.FormatInterval(MeanInterval);

    /// <summary>
    /// Sort key for the mean-interval column so DataGrid can order rows
    /// even when some entries are <c>N/A</c>. Missing intervals sort to the end.
    /// </summary>
    public double MeanIntervalSortKey => MeanInterval?.TotalMilliseconds ?? double.MaxValue;
}
