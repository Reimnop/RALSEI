using System.Diagnostics;
using System.Numerics;
using Pamx.Objects;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Swag.Service;

public class AnimationPipeline(Timeline timeline, PlaybackObjectContainer playbackObjects, PlaybackObjectSortingService sortingService)
{
    private struct DrawWorkItem
    {
        public int ObjectIndex;
        public int FirstSlot; // first slot in the draw item cache this work item writes to
        public int Count; // amount of draw items this work item produces
        public int Stream; // RegularObject for a normal object, or the particle stream
        public long FirstParticleIndex;
    }

    private const int RegularObject = -1;

    // Particles are processed in chunks so one big emitter can be spread over multiple threads
    private const int ParticleChunkSize = 256;

    private static readonly Comparison<ObjectDrawItem> drawItemComparison = static (x, y) =>
    {
        var rankComparison = x.SortRank.CompareTo(y.SortRank);
        return rankComparison != 0 ? rankComparison : x.SubRank.CompareTo(y.SubRank);
    };

    private const int InitialCacheCapacity = 1000;

    private ObjectDrawItem[] drawItemCache = new ObjectDrawItem[InitialCacheCapacity];
    private DrawWorkItem[] workItems = new DrawWorkItem[InitialCacheCapacity];
    private int workItemCount;
    private uint[] objectIndexToSortRank = [];

    private float currentTime;
    private ThemeColorState? currentThemeColorState;

    public Span<ObjectDrawItem> ComputeDrawItems(float time, ThemeColorState themeColorState)
    {
        currentTime = time;
        currentThemeColorState = themeColorState;

        var aliveObjects = timeline.ComputeAliveObjects(time);

        objectIndexToSortRank = sortingService.GetObjectIndexToSortRankMapping();

        // work out what to draw. An object normally produces one draw item, but a particle emitter produces many
        var count = BuildWorkItems(aliveObjects);

        EnsureCacheCapacity(count);

        Parallel.For(0, workItemCount, ProcessWorkItem);

        var drawItemSpan = drawItemCache.AsSpan(0, count);
        drawItemSpan.Sort(drawItemComparison);
        return drawItemSpan;
    }

    private int BuildWorkItems(IReadOnlyCollection<int> aliveObjects)
    {
        workItemCount = 0;
        var slotCount = 0;

        foreach (var objectIndex in aliveObjects)
        {
            if (!playbackObjects.TryGetItem(objectIndex, out var playbackObject))
                continue;

            if (playbackObject.Type == PlaybackObjectType.Particles)
            {
                slotCount += AddParticleWorkItems(objectIndex, playbackObject, slotCount);
                continue;
            }

            AddWorkItem(new DrawWorkItem
            {
                ObjectIndex = objectIndex,
                FirstSlot = slotCount,
                Count = 1,
                Stream = RegularObject
            });
            slotCount++;
        }

        return slotCount;
    }

    private int AddParticleWorkItems(int objectIndex, PlaybackObject emitter, int firstSlot)
    {
        var parameters = emitter.ParticleParams;
        if (parameters is null)
            return 0;

        var slotCount = 0;

        if (parameters.ParticlesPerSecond > 0f)
        {
            ParticleEmission.GetTimeStreamRanges(emitter, currentTime, out var older, out var newer);
            slotCount += AddParticleChunks(objectIndex, ParticleEmission.TimeStream, older, firstSlot + slotCount);
            slotCount += AddParticleChunks(objectIndex, ParticleEmission.TimeStream, newer, firstSlot + slotCount);
        }

        if (parameters.ParticlesPerUnit > 0f)
        {
            var table = emitter.ParticleDistanceTable ??= new ParticleDistanceTable();

            // the table has to cover everything up to the current time before particles can be placed on it
            var until = Math.Min(currentTime, emitter.EndTime);
            if (until >= emitter.StartTime)
            {
                var signature = ComputeEmitterPathSignature(objectIndex);
                if (!table.IsUpToDate(signature, emitter.StartTime, until))
                    table.Prepare(signature, emitter.StartTime, until, t => GetEmitterWorldPosition(objectIndex, t));
            }

            var range = ParticleEmission.GetDistanceStreamRange(emitter, table, currentTime);
            slotCount += AddParticleChunks(objectIndex, ParticleEmission.DistanceStream, range, firstSlot + slotCount);
        }

        return slotCount;
    }

    private int AddParticleChunks(int objectIndex, int stream, ParticleRange range, int firstSlot)
    {
        for (var offset = 0; offset < range.Count; offset += ParticleChunkSize)
        {
            var chunkCount = Math.Min(ParticleChunkSize, range.Count - offset);
            AddWorkItem(new DrawWorkItem
            {
                ObjectIndex = objectIndex,
                FirstSlot = firstSlot + offset,
                Count = chunkCount,
                Stream = stream,
                FirstParticleIndex = range.First + offset
            });
        }

        return range.Count;
    }

    private void AddWorkItem(DrawWorkItem workItem)
    {
        if (workItemCount == workItems.Length)
            Array.Resize(ref workItems, workItems.Length * 2);
        workItems[workItemCount++] = workItem;
    }

    private void ProcessWorkItem(int workItemIndex)
    {
        ref var workItem = ref workItems[workItemIndex];
        if (workItem.Stream == RegularObject)
            ProcessPlaybackObject(workItem.ObjectIndex, workItem.FirstSlot);
        else
            ProcessParticles(ref workItem);
    }

    private void ProcessPlaybackObject(int objectIndex, int slot)
    {
        ref var drawItem = ref drawItemCache[slot];

        if (!playbackObjects.TryGetItem(objectIndex, out var playbackObject))
        {
            drawItem = default;
            return;
        }

        var transform = CalculateObjectTransform(
            objectIndex,
            ParentType.All, ParentOffset.Zero,
            currentTime);

        var originMatrix = playbackObject.Shape == ObjectShape.Text
            ? Matrix3x2.Identity
            : Matrix3x2.CreateTranslation(playbackObject.Origin);

        Debug.Assert(currentThemeColorState is not null);
        var color = playbackObject.ColorSequence.ComputeValueAt(currentTime - playbackObject.StartTime, currentThemeColorState);

        drawItem.SortRank = objectIndexToSortRank[objectIndex];
        drawItem.SubRank = 0;
        drawItem.Transform = originMatrix * transform;
        drawItem.Color1 = color.Color1;
        drawItem.Color2 = color.Color2;
        drawItem.Opacity = color.Opacity;
        drawItem.ObjectIndex = objectIndex;
    }

    // TODO: Each particle becomes its own draw item, and then its own mesh draw command.
    // This should be replaced with an instanced draw per emitter, where the GPU gets the emitter and
    // computes the particle transforms itself.
    //
    // This follows the emission model of ParticleEmission, including what it leaves to the engine:
    //  - The emitter's scale only sizes the emission shape. It never scales the emitter or the particles.
    //  - In local space, particles live in the emitter's own axes, so they follow its position and rotation.
    //    The shape is sized by the scale of the emitter's parents as well, but only when the particle spawns.
    //  - In world space, particles are put into the world when they spawn, and the parents' scale does not size the shape.
    //  - An anchored emitter is in world space, but its shape is sized by the scale of its parents like it is in local space.
    //  - The particle shape is aligned to the emitter's rotation in local space, and to the world in world space.
    //
    // It evaluates positions directly instead of handing velocities to a particle engine, which has these consequences:
    //  - The position track is a position, so the value of the first keyframe counts and a particle can jump.
    //    A velocity-driven engine would only see the change of it.
    //  - A particle in world space keeps the path it had when it spawned. A velocity-driven engine would turn
    //    the path with the emitter for as long as the emitter turns, which bends the paths of all its particles.
    //  - A shape that is stretched by the emitter's parents is skewed, like any other object would be.
    //    A velocity-driven engine would only keep the stretch along the emitter's own axes.
    private void ProcessParticles(ref DrawWorkItem workItem)
    {
        var drawItems = drawItemCache.AsSpan(workItem.FirstSlot, workItem.Count);

        if (!playbackObjects.TryGetItem(workItem.ObjectIndex, out var emitter) || emitter.ParticleParams is null)
        {
            drawItems.Clear();
            return;
        }

        var parameters = emitter.ParticleParams;
        var table = emitter.ParticleDistanceTable;
        var startSpeed = Math.Max(0f, parameters.RadialStartSpeed);

        Debug.Assert(currentThemeColorState is not null);

        var sortRank = objectIndexToSortRank[workItem.ObjectIndex];

        var inWorldSpace = parameters.InWorldSpace;

        // where the emitter is right now, which is where particles in local space are
        var currentPose = default(EmitterPose);
        if (!inWorldSpace)
            currentPose = CalculateEmitterPose(workItem.ObjectIndex, currentTime);

        for (var i = 0; i < drawItems.Length; i++)
        {
            var particleIndex = workItem.FirstParticleIndex + i;

            var spawnTime = workItem.Stream == ParticleEmission.TimeStream
                ? ParticleEmission.GetTimeStreamSpawnTime(emitter, particleIndex)
                : ParticleEmission.GetDistanceStreamSpawnTime(emitter, table!, particleIndex);
            var age = (float)Math.Clamp(currentTime - spawnTime, 0.0, emitter.ParticleLifetime);

            var spawnPose = CalculateEmitterPose(workItem.ObjectIndex, (float)spawnTime);

            // The emission shape is a unit square (or unit circle) sized by the scale animated on the emitter.
            ParticleEmission.GetRandomValues(emitter.ParticleSeed, workItem.Stream, particleIndex,
                out var random1, out var random2, out var random3, out var random4);
            var area = ParticleEmission.SampleShape(parameters, random1, random2, random3, random4)
                       * emitter.ScaleSequence.ComputeValueAt((float)spawnTime - emitter.StartTime);

            // The shape is also stretched by what the emitter's parents do to it, in local space and for an anchored emitter.
            // Other emitters in world space are only sized by their own scale.
            if (parameters.ParentsSizeShape)
                area = Vector2.TransformNormal(area, spawnPose.Stretch);

            // Everything from here is in the emitter's own axes. A circle emits away from its center,
            // a square emits along its forward axis, so it does not move in the plane.
            var offset = area + emitter.ParticlePositionSequence.ComputeValueAt(age);
            if (parameters.Radial)
            {
                var lengthSquared = area.LengthSquared();
                if (lengthSquared > 0f)
                    offset += area * (startSpeed * age / MathF.Sqrt(lengthSquared));
            }

            var scale = emitter.ParticleScaleSequence.ComputeValueAt(age);
            var rotation = emitter.ParticleRotationSequence.ComputeValueAt(age);
            var color = emitter.ColorSequence.ComputeValueAt(age, currentThemeColorState);

            Vector2 point;
            float shapeRotation;
            if (inWorldSpace)
            {
                // the particle stays where it spawned, facing the way the emitter did then
                point = spawnPose.Position + Rotate(offset, spawnPose.Rotation);

                // shapes are aligned to the world
                shapeRotation = rotation;
            }
            else
            {
                point = currentPose.Position + Rotate(offset, currentPose.Rotation);

                // shapes are aligned to the emitter
                shapeRotation = currentPose.Rotation + rotation;
            }

            ref var drawItem = ref drawItems[i];
            drawItem.SortRank = sortRank;
            drawItem.SubRank = unchecked((uint)(particleIndex * 2 + workItem.Stream));

            // The shape is drawn at the particle's point with only its own size and rotation.
            // Particle shapes are not offset by the object's origin either.
            drawItem.Transform = FastMatrix.GetTranslationRotationScaleMatrix(point.X, point.Y, scale.X, scale.Y, shapeRotation);
            drawItem.Color1 = color.Color1;
            drawItem.Color2 = color.Color2;
            drawItem.Opacity = color.Opacity;
            drawItem.ObjectIndex = workItem.ObjectIndex;
        }
    }

    private static Vector2 Rotate(Vector2 vector, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    /// <summary>Where an emitter is in the world, as a scene graph would report it for the object that particles live in.</summary>
    private struct EmitterPose
    {
        /// <summary>The emitter's position, including what its parents do to it.</summary>
        public Vector2 Position;

        /// <summary>The emitter's rotation, which is the sum of its own rotation and the rotations it inherits.</summary>
        public float Rotation;

        /// <summary>
        /// What the emitter's parents do to a shape that sits in the emitter's own axes, which is its scale, and the skew
        /// that comes with it when the parents scale unevenly under a rotated emitter. The emitter's own scale is not part
        /// of this, since that one only sizes the emission shape. Only the 2x2 part is used.
        /// </summary>
        public Matrix3x2 Stretch;
    }

    private EmitterPose CalculateEmitterPose(int emitterIndex, float time)
    {
        if (!playbackObjects.TryGetItem(emitterIndex, out var playbackObject))
            return new EmitterPose { Stretch = Matrix3x2.Identity };

        var matrix = Matrix3x2.Identity;
        var rotationSum = 0f;

        // the emitter always uses its own position and rotation, but never its own scale
        var parentType = ParentType.Position | ParentType.Rotation;
        var parentOffset = ParentOffset.Zero;

        while (true)
        {
            if (playbackObject.Type is PlaybackObjectType.PrefabIntermediate or PlaybackObjectType.Camera)
                parentType = ParentType.All;

            var localTime = time - playbackObject.StartTime;

            var position = Vector2.Zero;
            var rotation = 0f;
            var scale = Vector2.One;

            if ((parentType & ParentType.Position) != 0)
                position = playbackObject.PositionSequence.ComputeValueAt(localTime - parentOffset.Position);
            if ((parentType & ParentType.Rotation) != 0)
                rotation = playbackObject.RotationSequence.ComputeValueAt(localTime - parentOffset.Rotation);
            if ((parentType & ParentType.Scale) != 0)
                scale = playbackObject.ScaleSequence.ComputeValueAt(localTime - parentOffset.Scale);

            matrix *= FastMatrix.GetTranslationRotationScaleMatrix(position.X, position.Y, scale.X, scale.Y, rotation);
            rotationSum += rotation;

            parentType = playbackObject.ParentType;
            parentOffset = playbackObject.ParentOffset;

            if (!playbackObjects.TryGetParentIndex(emitterIndex, out emitterIndex))
                break;

            if (!playbackObjects.TryGetItem(emitterIndex, out playbackObject))
                break;
        }

        // The rows of the matrix are the emitter's own axes as they are in the world. Taking the rotation out of them
        // leaves the stretch: its diagonal is the scale along each axis, and what is off the diagonal is skew.
        var (sin, cos) = MathF.SinCos(rotationSum);
        return new EmitterPose
        {
            Position = new Vector2(matrix.M31, matrix.M32),
            Rotation = rotationSum,
            Stretch = new Matrix3x2(
                matrix.M11 * cos + matrix.M12 * sin, matrix.M12 * cos - matrix.M11 * sin,
                matrix.M21 * cos + matrix.M22 * sin, matrix.M22 * cos - matrix.M21 * sin,
                0f, 0f)
        };
    }

    private Vector2 GetEmitterWorldPosition(int emitterIndex, float time)
    {
        var transform = CalculateObjectTransform(emitterIndex, ParentType.All, ParentOffset.Zero, time);
        return new Vector2(transform.M31, transform.M32);
    }

    /// <summary>
    /// A hash of everything that decides where an emitter is over time.
    /// Changes whenever the cached distance travelled by the emitter becomes stale.
    /// </summary>
    private ulong ComputeEmitterPathSignature(int emitterIndex)
    {
        var hash = 0UL;

        while (playbackObjects.TryGetItem(emitterIndex, out var playbackObject))
        {
            var parentOffset = playbackObject.ParentOffset;

            hash = NumberUtil.Mix(
                hash,
                playbackObject.Id.Value,
                BitConverter.SingleToUInt32Bits(playbackObject.StartTime),
                (ulong)playbackObject.ParentType,
                BitConverter.SingleToUInt32Bits(parentOffset.Position),
                BitConverter.SingleToUInt32Bits(parentOffset.Scale),
                BitConverter.SingleToUInt32Bits(parentOffset.Rotation),
                (ulong)playbackObject.PositionSequence.Version,
                (ulong)playbackObject.ScaleSequence.Version,
                (ulong)playbackObject.RotationSequence.Version);

            if (!playbackObjects.TryGetParentIndex(emitterIndex, out emitterIndex))
                break;
        }

        return hash;
    }

    private Matrix3x2 CalculateObjectTransform(
        int playbackObjectIndex,
        ParentType parentType, ParentOffset parentOffset,
        float time)
    {
        var matrix = Matrix3x2.Identity;

        if (!playbackObjects.TryGetItem(playbackObjectIndex, out var playbackObject))
            return matrix;

        while (true)
        {
            if (playbackObject.Type is PlaybackObjectType.PrefabIntermediate or PlaybackObjectType.Camera)
                parentType = ParentType.All;

            switch (parentType)
            {
                case ParentType.Position:
                {
                    var position = playbackObject.PositionSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Position);
                    matrix *= FastMatrix.GetTranslationMatrix(position.X, position.Y);
                    break;
                }
                case ParentType.Scale:
                {
                    var scale = playbackObject.ScaleSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Scale);
                    matrix *= FastMatrix.GetScaleMatrix(scale.X, scale.Y);
                    break;
                }
                case ParentType.Rotation:
                {
                    var rotation = playbackObject.RotationSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Rotation);
                    matrix *= FastMatrix.GetRotationMatrix(rotation);
                    break;
                }
                case ParentType.Position | ParentType.Rotation:
                {
                    var position = playbackObject.PositionSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Position);
                    var rotation = playbackObject.RotationSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Rotation);
                    matrix *= FastMatrix.GetTranslationRotationMatrix(position.X, position.Y, rotation);
                    break;
                }
                case ParentType.Position | ParentType.Scale:
                {
                    var position = playbackObject.PositionSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Position);
                    var scale = playbackObject.ScaleSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Scale);
                    matrix *= FastMatrix.GetTranslationScaleMatrix(position.X, position.Y, scale.X, scale.Y);
                    break;
                }
                case ParentType.Rotation | ParentType.Scale:
                {
                    var rotation = playbackObject.RotationSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Rotation);
                    var scale = playbackObject.ScaleSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Scale);
                    matrix *= FastMatrix.GetRotationScaleMatrix(scale.X, scale.Y, rotation);
                    break;
                }
                case ParentType.All:
                {
                    var position = playbackObject.PositionSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Position);
                    var rotation = playbackObject.RotationSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Rotation);
                    var scale = playbackObject.ScaleSequence.ComputeValueAt(time - playbackObject.StartTime - parentOffset.Scale);
                    matrix *= FastMatrix.GetTranslationRotationScaleMatrix(
                        position.X, position.Y,
                        scale.X, scale.Y,
                        rotation);
                    break;
                }
            }

            parentType = playbackObject.ParentType;
            parentOffset = playbackObject.ParentOffset;

            if (!playbackObjects.TryGetParentIndex(playbackObjectIndex, out playbackObjectIndex))
                break;

            if (!playbackObjects.TryGetItem(playbackObjectIndex, out playbackObject))
                break;
        }

        return matrix;
    }

    private void EnsureCacheCapacity(int capacity)
    {
        if (drawItemCache.Length < capacity)
        {
            var newCapacity = Math.Max(drawItemCache.Length * 2, capacity);
            Array.Resize(ref drawItemCache, newCapacity);
        }
    }
}
