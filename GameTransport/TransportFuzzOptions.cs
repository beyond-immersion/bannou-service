using System;

namespace BeyondImmersion.Bannou.GameTransport;

/// <summary>
/// Fuzz settings for testing packet loss/reordering/delay.
/// </summary>
public sealed class TransportFuzzOptions
{
    /// <summary>Drop probability (0-1).</summary>
    public double DropProbability { get; set; } = 0.0;

    /// <summary>Delay probability (0-1).</summary>
    public double DelayProbability { get; set; } = 0.0;

    /// <summary>Maximum artificial delay in milliseconds.</summary>
    public int MaxDelayMs { get; set; } = 0;

    /// <summary>
    /// Determines whether to drop a packet based on configured probability.
    /// </summary>
    /// <param name="random">Random number generator instance.</param>
    /// <returns>True if the packet should be dropped.</returns>
    public bool ShouldDrop(Random random) => random.NextDouble() < DropProbability;

    /// <summary>
    /// Determines whether to delay a packet and calculates the delay duration.
    /// </summary>
    /// <param name="delayMs">Output: delay duration in milliseconds if delayed, otherwise 0.</param>
    /// <param name="random">Random number generator instance.</param>
    /// <returns>True if the packet should be delayed.</returns>
    public bool ShouldDelay(out int delayMs, Random random)
    {
        if (random.NextDouble() < DelayProbability && MaxDelayMs > 0)
        {
            delayMs = random.Next(1, MaxDelayMs + 1);
            return true;
        }

        delayMs = 0;
        return false;
    }
}
