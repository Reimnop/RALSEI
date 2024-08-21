using System.Numerics;

namespace Ralsei.Core.Service;

/// <summary>
/// The distance an emitter has travelled over time, sampled at a fixed step.
/// Turns "particles per unit moved" into spawn times, which keeps emission a pure function of time.
/// </summary>
public sealed class ParticleDistanceTable
{
    public const float SampleStep = 1f / 60f;

    // Don't sample more than an hour of motion
    private const int MaxSamples = 60 * 3600;

    private readonly List<float> cumulativeDistances = [];
    private Vector2 lastPosition;
    private ulong signature;
    private float startTime = float.NaN;

    /// <summary>
    /// Whether the table is valid for the given path and already covers <paramref name="untilTime"/>.
    /// </summary>
    public bool IsUpToDate(ulong pathSignature, float pathStartTime, float untilTime)
        => cumulativeDistances.Count > 0
           && signature == pathSignature
           && startTime == pathStartTime
           && cumulativeDistances.Count >= GetSampleCount(pathStartTime, untilTime);

    /// <summary>
    /// Extends the table so it covers <paramref name="untilTime"/>. The table is rebuilt from scratch
    /// if the path described by <paramref name="pathSignature"/> or its start time changed.
    /// </summary>
    public void Prepare(ulong pathSignature, float pathStartTime, float untilTime, Func<float, Vector2> getPosition)
    {
        if (cumulativeDistances.Count == 0 || signature != pathSignature || startTime != pathStartTime)
        {
            signature = pathSignature;
            startTime = pathStartTime;
            cumulativeDistances.Clear();
            cumulativeDistances.Add(0f);
            lastPosition = getPosition(pathStartTime);
        }

        var sampleCount = GetSampleCount(pathStartTime, untilTime);
        while (cumulativeDistances.Count < sampleCount)
        {
            var position = getPosition((float)(pathStartTime + cumulativeDistances.Count * (double)SampleStep));
            cumulativeDistances.Add(cumulativeDistances[^1] + Vector2.Distance(position, lastPosition));
            lastPosition = position;
        }
    }

    /// <summary>
    /// The distance travelled between the start of the path and <paramref name="time"/>.
    /// </summary>
    public float GetDistanceAt(float time)
    {
        if (cumulativeDistances.Count == 0)
            return 0f;

        var position = (time - startTime) / SampleStep;
        if (!(position > 0f))
            return 0f;

        var last = cumulativeDistances.Count - 1;
        if (position >= last)
            return cumulativeDistances[last];

        var index = (int)position;
        return float.Lerp(cumulativeDistances[index], cumulativeDistances[index + 1], position - index);
    }

    /// <summary>
    /// The earliest time at which the emitter has travelled <paramref name="distance"/>.
    /// </summary>
    public double GetTimeAtDistance(float distance)
    {
        if (cumulativeDistances.Count == 0 || distance <= 0f)
            return startTime;

        // find the first sample which reached the distance
        var low = 0;
        var high = cumulativeDistances.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (cumulativeDistances[mid] < distance)
                low = mid + 1;
            else
                high = mid;
        }

        // not reached yet, so this is the end of what we know
        if (low >= cumulativeDistances.Count)
            return startTime + (cumulativeDistances.Count - 1) * (double)SampleStep;

        var previous = cumulativeDistances[low - 1];
        var next = cumulativeDistances[low];
        var fraction = next > previous ? (distance - previous) / (next - previous) : 1f;
        return startTime + (low - 1 + (double)fraction) * SampleStep;
    }

    private static int GetSampleCount(float pathStartTime, float untilTime)
    {
        var steps = Math.Ceiling(((double)untilTime - pathStartTime) / SampleStep);
        return (int)Math.Clamp(steps, 0, MaxSamples) + 1;
    }
}
