export interface ParticleParams {
  /** Amount of particles emitted per second. */
  particlesPerSecond: number;
  /** Amount of particles emitted per unit the emitter moved. */
  particlesPerUnit: number;
  /** Whether particles are simulated in world space (true) or follow the emitter (false). An anchored emitter is always in world space. */
  world: boolean;
  /** Whether particles disappear when the emitter is killed, or live out their lifetime. */
  despawnOnEnd: boolean;
  /** Whether the emitter is a unit circle (true) instead of a unit square. */
  radial: boolean;
  /** The arc of the circle, in degrees. It starts at the right and goes counter-clockwise. */
  radialCircleArc: number;
  /** How much of the radius particles spawn in, counted from the edge inwards. 1 fills the whole circle, 0 only spawns on its edge. */
  radialCircleRadius: number;
  /** The speed at which particles move away from the circle's center. */
  radialStartSpeed: number;
  /**
   * Whether this is an anchored emitter. Its particles are in world space, but unlike other particles in world space, the shape
   * they spawn in is sized by the scale of the emitter's parents, as it is when particles follow the emitter.
   */
  anchored: boolean;
}
