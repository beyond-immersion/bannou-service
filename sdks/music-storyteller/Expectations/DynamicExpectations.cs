namespace BeyondImmersion.Bannou.MusicStoryteller.Expectations;

/// <summary>
/// Dynamic expectations based on very recent patterns.
/// Represents short-term memory - the last 10-25 events.
/// Source: Huron, D. (2006). Sweet Anticipation, Chapter 12.
/// </summary>
public sealed class DynamicExpectations
{
    /// <summary>
    /// Maximum number of events to track in short-term memory.
    /// </summary>
    public const int MaxEvents = 25;

    /// <summary>
    /// Recent melodic intervals.
    /// </summary>
    public List<int> RecentIntervals { get; } = new(MaxEvents);

    /// <summary>
    /// Recent scale degrees.
    /// </summary>
    public List<int> RecentDegrees { get; } = new(MaxEvents);

    /// <summary>
    /// Recent rhythmic durations.
    /// </summary>
    public List<double> RecentRhythms { get; } = new(MaxEvents);

    /// <summary>
    /// Recent chord degrees.
    /// </summary>
    public List<int> RecentChords { get; } = new(MaxEvents);

    /// <summary>
    /// Whether a repeating pattern has been detected.
    /// </summary>
    public bool PatternDetected { get; private set; }

    /// <summary>
    /// The detected pattern length (if any).
    /// </summary>
    public int PatternLength { get; private set; }

    /// <summary>
    /// The detected pattern content (if any).
    /// </summary>
    public List<int> DetectedPattern { get; } = [];

    /// <summary>
    /// Current position within the detected pattern.
    /// </summary>
    public int PatternPosition { get; private set; }

    /// <summary>
    /// Confidence in the detected pattern (0-1).
    /// </summary>
    public double PatternConfidence { get; private set; }

    /// <summary>
    /// Adds a melodic interval to the recent history.
    /// </summary>
    /// <param name="semitones">The interval in semitones.</param>
    public void AddInterval(int semitones)
    {
        AddToHistory(RecentIntervals, semitones);
        DetectPattern(RecentIntervals);
    }

    /// <summary>
    /// Adds a scale degree to the recent history.
    /// </summary>
    /// <param name="degree">The scale degree (1-7).</param>
    public void AddDegree(int degree)
    {
        AddToHistory(RecentDegrees, degree);
    }

    /// <summary>
    /// Adds a rhythmic duration to the recent history.
    /// </summary>
    /// <param name="duration">The duration in beats.</param>
    public void AddRhythm(double duration)
    {
        AddToHistory(RecentRhythms, duration);
    }

    /// <summary>
    /// Adds a chord degree to the recent history.
    /// </summary>
    /// <param name="degree">The chord degree (1-7).</param>
    public void AddChord(int degree)
    {
        AddToHistory(RecentChords, degree);
    }

    /// <summary>
    /// Gets the expected next interval based on recent patterns.
    /// </summary>
    /// <returns>Expected interval and confidence, or null if no pattern.</returns>
    public (int interval, double confidence)? GetExpectedInterval()
    {
        if (!PatternDetected || DetectedPattern.Count == 0)
        {
            return null;
        }

        var nextIndex = PatternPosition % DetectedPattern.Count;
        return (DetectedPattern[nextIndex], PatternConfidence);
    }

    /// <summary>
    /// Gets transition probabilities based on recent history.
    /// </summary>
    /// <param name="current">Current value.</param>
    /// <returns>Dictionary of possible next values and their probabilities.</returns>
    public Dictionary<int, double> GetIntervalTransitionProbabilities(int current)
    {
        return CalculateTransitionProbabilities(RecentIntervals, current);
    }

    /// <summary>
    /// Gets the most likely next interval based on recent transitions.
    /// </summary>
    /// <param name="currentInterval">Current interval.</param>
    /// <returns>Most likely next interval and probability.</returns>
    public (int interval, double probability)? GetMostLikelyNextInterval(int currentInterval)
    {
        var probs = GetIntervalTransitionProbabilities(currentInterval);
        if (probs.Count == 0) return null;

        var best = probs.MaxBy(kvp => kvp.Value);
        return (best.Key, best.Value);
    }

    /// <summary>
    /// Calculates the surprise for an event given recent history.
    /// </summary>
    /// <param name="interval">The interval that occurred.</param>
    /// <returns>Surprise level (0-1).</returns>
    public double CalculateSurprise(int interval)
    {
        if (RecentIntervals.Count < 5)
        {
            return 0.5; // Not enough history
        }

        // Check if pattern was detected and violated
        if (PatternDetected && DetectedPattern.Count > 0)
        {
            var expected = DetectedPattern[PatternPosition % DetectedPattern.Count];
            if (interval != expected)
            {
                // Pattern violation - high surprise
                return 0.8 + PatternConfidence * 0.2;
            }
            else
            {
                // Pattern confirmed - low surprise
                return 0.2 - PatternConfidence * 0.15;
            }
        }

        // Calculate based on transition probability
        var probs = GetIntervalTransitionProbabilities(RecentIntervals.Last());
        var prob = probs.GetValueOrDefault(interval, 0.05);

        // Convert probability to surprise (inverse relationship)
        return 1.0 - prob;
    }

    /// <summary>
    /// Advances the pattern position after processing an event.
    /// </summary>
    public void AdvancePattern()
    {
        if (PatternDetected)
        {
            PatternPosition = (PatternPosition + 1) % PatternLength;
        }
    }

    /// <summary>
    /// Resets pattern detection (call when pattern is broken).
    /// </summary>
    public void ResetPattern()
    {
        PatternDetected = false;
        PatternLength = 0;
        PatternPosition = 0;
        PatternConfidence = 0;
        DetectedPattern.Clear();
    }

    /// <summary>
    /// Clears all history.
    /// </summary>
    public void Clear()
    {
        RecentIntervals.Clear();
        RecentDegrees.Clear();
        RecentRhythms.Clear();
        RecentChords.Clear();
        ResetPattern();
    }

    private static void AddToHistory<T>(List<T> history, T value)
    {
        history.Add(value);
        if (history.Count > MaxEvents)
        {
            history.RemoveAt(0);
        }
    }

    private void DetectPattern(List<int> sequence)
    {
        if (sequence.Count < 6)
        {
            PatternDetected = false;
            return;
        }

        // Look for repeating patterns of length 2-6
        for (var len = 2; len <= Math.Min(6, sequence.Count / 2); len++)
        {
            if (CheckRepeatingPattern(sequence, len))
            {
                PatternDetected = true;
                PatternLength = len;
                PatternConfidence = CalculatePatternConfidence(sequence, len);

                DetectedPattern.Clear();
                for (var i = sequence.Count - len; i < sequence.Count; i++)
                {
                    DetectedPattern.Add(sequence[i]);
                }

                // Set position to where we are in the pattern
                PatternPosition = 0;
                return;
            }
        }

        // If pattern was detected before but now broken
        if (PatternDetected)
        {
            PatternConfidence *= 0.7; // Reduce confidence
            if (PatternConfidence < 0.3)
            {
                ResetPattern();
            }
        }
    }

    private static bool CheckRepeatingPattern(List<int> sequence, int length)
    {
        if (sequence.Count < length * 2)
        {
            return false;
        }

        // Check last two cycles
        var startA = sequence.Count - length * 2;
        var startB = sequence.Count - length;

        for (var i = 0; i < length; i++)
        {
            if (sequence[startA + i] != sequence[startB + i])
            {
                return false;
            }
        }

        return true;
    }

    private static double CalculatePatternConfidence(List<int> sequence, int length)
    {
        // More repetitions = higher confidence
        var repetitions = 0;
        var pattern = sequence.Skip(sequence.Count - length).ToArray();

        for (var start = sequence.Count - length * 2; start >= 0; start -= length)
        {
            var match = true;
            for (var i = 0; i < length && start + i < sequence.Count; i++)
            {
                if (sequence[start + i] != pattern[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                repetitions++;
            }
            else
            {
                break; // Pattern breaks
            }
        }

        // 2 reps = 0.5, 3 reps = 0.7, 4+ reps = 0.9
        return Math.Min(0.9, 0.3 + repetitions * 0.2);
    }

    private static Dictionary<int, double> CalculateTransitionProbabilities(List<int> history, int current)
    {
        var counts = new Dictionary<int, int>();
        var total = 0;

        for (var i = 0; i < history.Count - 1; i++)
        {
            if (history[i] == current)
            {
                var next = history[i + 1];
                counts[next] = counts.GetValueOrDefault(next, 0) + 1;
                total++;
            }
        }

        var probs = new Dictionary<int, double>();
        foreach (var (key, count) in counts)
        {
            probs[key] = (double)count / total;
        }

        return probs;
    }
}
