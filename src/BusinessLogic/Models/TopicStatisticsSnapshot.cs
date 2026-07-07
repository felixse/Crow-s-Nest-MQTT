namespace CrowsNestMqtt.BusinessLogic.Models;

/// <summary>
/// An immutable snapshot of per-topic message statistics maintained by
/// <see cref="Services.ITopicStatisticsService"/>. Values represent lifetime
/// aggregates (i.e., they include messages that have already been evicted
/// from the ring buffer).
/// </summary>
/// <param name="Topic">The MQTT topic these statistics belong to.</param>
/// <param name="MessageCount">Total number of messages recorded for the topic.</param>
/// <param name="TotalPayloadBytes">Sum of payload byte counts across all recorded messages.</param>
/// <param name="AveragePayloadBytes">
/// Mean payload size in bytes. <c>0</c> when <paramref name="MessageCount"/> is zero.
/// </param>
/// <param name="MeanInterval">
/// Mean interval between successive messages, computed as
/// <c>(Last − First) / (MessageCount − 1)</c>. <c>null</c> when fewer than two messages
/// have been recorded (interval is undefined).
/// </param>
/// <param name="FirstTimestampUtc">Timestamp of the first recorded message (UTC).</param>
/// <param name="LastTimestampUtc">Timestamp of the most recent recorded message (UTC).</param>
public sealed record TopicStatisticsSnapshot(
    string Topic,
    long MessageCount,
    long TotalPayloadBytes,
    double AveragePayloadBytes,
    TimeSpan? MeanInterval,
    DateTime FirstTimestampUtc,
    DateTime LastTimestampUtc);
