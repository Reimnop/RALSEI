namespace Ralsei.Core.Data;

/// <summary>
/// The emitter settings of a particle object.
/// </summary>
/// <param name="ParticlesPerSecond">Amount of particles emitted per second.</param>
/// <param name="ParticlesPerUnit">Amount of particles emitted per unit the emitter moved.</param>
/// <param name="World">
/// Whether particles are simulated in world space (true) or follow the emitter (false).
/// An anchored emitter is always in world space, whatever this says, see <see cref="InWorldSpace"/>.
/// </param>
/// <param name="DespawnOnEnd">Whether particles disappear when the emitter is killed, or live out their lifetime.</param>
/// <param name="Radial">Whether the emitter is a unit circle (true) instead of a unit square.</param>
/// <param name="RadialCircleArc">The arc of the circle, in degrees. The arc starts at the right and goes counter-clockwise.</param>
/// <param name="RadialCircleRadius">
/// How much of the circle's radius particles spawn in, counted from the edge inwards.
/// 1 fills the whole circle and 0 only spawns on its edge. This is the thickness of the spawn ring.
/// Note that the file format documentation describes this the other way around, but playback uses it like this.
/// </param>
/// <param name="RadialStartSpeed">The speed at which particles move away from the circle's center.</param>
/// <param name="Anchored">
/// Whether this is an anchored emitter. Its particles are in world space, but unlike those of other emitters in world space,
/// the shape they spawn in is sized by the scale of the emitter's parents, as it is when particles follow the emitter.
/// </param>
public sealed record ParticleEmitterParams(
    float ParticlesPerSecond,
    float ParticlesPerUnit,
    bool World,
    bool DespawnOnEnd,
    bool Radial,
    float RadialCircleArc,
    float RadialCircleRadius,
    float RadialStartSpeed,
    bool Anchored)
{
    /// <summary>
    /// Whether particles stay where they spawned instead of following the emitter.
    /// That is what <see cref="World"/> asks for, and what an anchored emitter always does.
    /// </summary>
    public bool InWorldSpace => World || Anchored;

    /// <summary>
    /// Whether the scale of the emitter's parents sizes the shape that particles spawn in, in addition to the emitter's own scale.
    /// This applies to particles that follow the emitter and to anchored emitters, but not to other particles in world space.
    /// </summary>
    public bool ParentsSizeShape => !World || Anchored;
}
