using System.Numerics;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Ralsei.Core.Service;

/// <summary>
/// A run of consecutive particles of a stream.
/// </summary>
public readonly record struct ParticleRange(long First, int Count)
{
    public static ParticleRange Empty => default;
}

/// <summary>
/// Closed-form particle emission, modelled on a conventional capped particle system.
/// <para>
/// Particles are never simulated. Particle <c>k</c> of a stream has a fixed spawn time and fixed random values,
/// so which particles are alive, and where they are, is a pure function of the current time.
/// This keeps seeking, reverse playback and offline rendering exact.
/// </para>
/// <para>
/// An emitter has up to two streams, which are emitted independently:
/// one that emits over time, and one that emits over the distance the emitter has travelled.
/// The first particle of a stream comes one interval after the start, so particles are numbered from 1.
/// </para>
/// </summary>
public static class ParticleEmission
{
    public const int TimeStream = 0;
    public const int DistanceStream = 1;

    /// <summary>
    /// The most particles that can be alive at once.
    /// Once it is reached new particles are refused until old ones die, old ones are never removed.
    /// Every stream gets the full limit.
    /// </summary>
    public const int MaxParticles = 5000;

    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// Gets the particles of the time stream that are alive at <paramref name="time"/>.
    /// Usually that is one run of particles, but when the emitter hits <see cref="MaxParticles"/> it
    /// refuses particles in a repeating pattern, which leaves a gap and so two runs.
    /// </summary>
    public static void GetTimeStreamRanges(PlaybackObject emitter, float time, out ParticleRange older, out ParticleRange newer)
    {
        older = ParticleRange.Empty;
        newer = ParticleRange.Empty;

        var parameters = emitter.ParticleParams;
        if (parameters is null)
            return;

        var rate = parameters.ParticlesPerSecond;
        var lifetime = emitter.ParticleLifetime;
        if (!(rate > 0f) || !(lifetime > 0f))
            return;

        double start = emitter.StartTime;
        var interval = 1.0 / rate;

        // particles are only emitted while the emitter is alive
        var latest = Math.Min(time, (double)emitter.EndTime);
        if (latest < start)
            return;

        // the last candidate that has spawned, and the first one that hasn't lived out its lifetime
        var last = Math.Floor((latest - start) / interval);
        var oldest = Math.Max(Math.Floor((time - (double)lifetime - start) / interval) + 1, 1);
        if (!(oldest <= last) || last >= long.MaxValue)
            return;

        // How many candidates fit in the lifetime of a particle. Candidates come at a fixed rate and a particle
        // frees its place after exactly that many, so when there are more than the limit, it
        // lets the first MaxParticles of every such period through, and refuses the rest.
        var period = Math.Ceiling(rate * (double)lifetime);
        if (period <= MaxParticles)
        {
            newer = CreateRange(oldest, last);
            return;
        }

        var periodStart = Math.Floor((last - 1) / period) * period + 1;
        newer = CreateRange(Math.Max(periodStart, oldest), Math.Min(periodStart + MaxParticles - 1, last));
        older = CreateRange(Math.Max(periodStart - period, oldest), Math.Min(periodStart - period + MaxParticles - 1, last));
    }

    /// <summary>
    /// Gets the particles of the distance stream that are alive at <paramref name="time"/>.
    /// The table must already cover the time the emitter was last alive, see <see cref="ParticleDistanceTable.Prepare"/>.
    /// </summary>
    public static ParticleRange GetDistanceStreamRange(PlaybackObject emitter, ParticleDistanceTable table, float time)
    {
        var parameters = emitter.ParticleParams;
        if (parameters is null)
            return ParticleRange.Empty;

        var rate = parameters.ParticlesPerUnit;
        var lifetime = emitter.ParticleLifetime;
        if (!(rate > 0f) || !(lifetime > 0f))
            return ParticleRange.Empty;

        double start = emitter.StartTime;

        // particles are only emitted while the emitter is alive
        var latest = Math.Min(time, (double)emitter.EndTime);
        if (latest < start)
            return ParticleRange.Empty;

        // the particle at index j spawns once the emitter has travelled j / rate units (j starts at 1)
        var last = Math.Floor(table.GetDistanceAt((float)latest) * (double)rate);
        var oldestTime = Math.Min(Math.Max(time - (double)lifetime, start), latest);
        var oldest = Math.Max(Math.Floor(table.GetDistanceAt((float)oldestTime) * (double)rate) + 1, 1);
        if (!(oldest <= last) || last >= long.MaxValue)
            return ParticleRange.Empty;

        // When there are too many, the old particles are kept and the new ones refused.
        // Candidates are not evenly spaced here, so unlike the time stream this only approximates which ones get through.
        return CreateRange(oldest, Math.Min(last, oldest + MaxParticles - 1));
    }

    public static double GetTimeStreamSpawnTime(PlaybackObject emitter, long index)
    {
        return emitter.StartTime + index / (double)emitter.ParticleParams!.ParticlesPerSecond;
    }

    public static double GetDistanceStreamSpawnTime(PlaybackObject emitter, ParticleDistanceTable table, long index)
    {
        return table.GetTimeAtDistance((float)(index / (double)emitter.ParticleParams!.ParticlesPerUnit));
    }

    /// <summary>
    /// Picks the spot where a particle spawns inside the emitter's unit shape.
    /// The shape is a unit square, or a unit circle (radius 1) when the emitter is radial.
    /// </summary>
    /// <remarks>
    /// A circle emits its particles away from its center, a square emits along its forward axis, which points into
    /// the screen. So a square has no start direction in the plane, and for a circle it follows the point once the
    /// shape has been stretched, which is why the direction is not part of the result.
    /// </remarks>
    /// <param name="parameters">The emitter's settings.</param>
    /// <param name="r1">A random value in [0, 1).</param>
    /// <param name="r2">A random value in [0, 1).</param>
    /// <param name="r3">A random value in [0, 1).</param>
    /// <param name="r4">A random value in [0, 1).</param>
    /// <returns>The spawn point in the unit shape.</returns>
    public static Vector2 SampleShape(ParticleEmitterParams parameters, float r1, float r2, float r3, float r4)
    {
        if (!parameters.Radial)
            return new Vector2(r1 - 0.5f, r2 - 0.5f);

        // the arc starts at the right and goes counter-clockwise
        var arc = Math.Clamp(parameters.RadialCircleArc, 0f, 360f);
        var (sin, cos) = MathF.SinCos(r3 * MathUtil.DegreesToRadians(arc));

        // The thickness is the part of the radius that particles spawn in, measured from the edge inwards.
        // 1 fills the whole circle, 0 only spawns on its edge. Spawning is even across the area.
        var innerRadius = 1f - Math.Clamp(parameters.RadialCircleRadius, 0f, 1f);
        return new Vector2(cos, sin) * MathF.Sqrt(float.Lerp(innerRadius * innerRadius, 1f, r4));
    }

    /// <summary>
    /// Gets four random values in [0, 1) that belong to a single particle.
    /// </summary>
    public static void GetRandomValues(ulong seed, int stream, long index, out float r1, out float r2, out float r3, out float r4)
    {
        var state = NumberUtil.Mix(NumberUtil.Mix(seed, (ulong)stream), (ulong)index);

        unchecked
        {
            state += Golden;
            r1 = NumberUtil.UlongToFloat01(NumberUtil.SplitMix64(state));
            state += Golden;
            r2 = NumberUtil.UlongToFloat01(NumberUtil.SplitMix64(state));
            state += Golden;
            r3 = NumberUtil.UlongToFloat01(NumberUtil.SplitMix64(state));
            state += Golden;
            r4 = NumberUtil.UlongToFloat01(NumberUtil.SplitMix64(state));
        }
    }

    private static ParticleRange CreateRange(double first, double last)
        => first <= last ? new ParticleRange((long)first, (int)(last - first + 1)) : ParticleRange.Empty;
}
