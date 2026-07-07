using CrowsNestMqtt.BusinessLogic.Models;

namespace CrowsNestMqtt.BusinessLogic.Services;

/// <summary>
/// Tracks per-topic lifetime message statistics for the <c>:stats</c> command.
/// Aggregates are maintained independently of the ring buffer so ring-buffer
/// eviction does not distort counts, totals, or timing.
/// </summary>
public interface ITopicStatisticsService
{
    /// <summary>
    /// Records a single message for the given topic. Safe to call from any thread.
    /// </summary>
    /// <param name="topic">The topic the message was received on. Ignored when null or whitespace.</param>
    /// <param name="payloadBytes">Payload size in bytes. Negative values are treated as zero.</param>
    /// <param name="timestampUtc">Timestamp of the message in UTC.</param>
    void Record(string topic, int payloadBytes, DateTime timestampUtc);

    /// <summary>
    /// Returns an atomic snapshot of the current statistics for every recorded topic.
    /// The returned list is a copy — mutations after the call do not affect it.
    /// </summary>
    IReadOnlyList<TopicStatisticsSnapshot> Snapshot();

    /// <summary>
    /// Clears every tracked topic. Called when the user clears the message history.
    /// </summary>
    void Reset();
}
