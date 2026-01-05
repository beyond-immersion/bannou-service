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

    public bool ShouldDrop(Random random) => random.NextDouble() < DropProbability;

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
