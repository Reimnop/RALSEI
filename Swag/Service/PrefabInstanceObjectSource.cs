using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Pamx.Keyframes;
using Pamx.Objects;
using Ralsei.Core.Data;
using Swag.Model;
using Ralsei.Core.Text;
using Ralsei.Util;
using BeatmapObject = Swag.Model.BeatmapObject;

namespace Swag.Service;

// Parent handling is delicate: read the intermediate-parent comments below before changing it.
public class PrefabInstanceObjectSource : ObjectSourceBase
{
    public BeatmapPrefab? AttachedPrefab => attachedPrefab;

    public float StartTime
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            UpdateStartTimes();
        }
    }

    public Vector2 Position
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            UpdateIntermediateParentTransforms();
        }
    }

    public Vector2 Scale
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            UpdateIntermediateParentTransforms();
        }
    } = Vector2.One;

    public float Rotation
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            UpdateIntermediateParentTransforms();
        }
    } = 0f;

    private float offset = 0f;

    private float EffectiveStartTime => StartTime - offset;

    protected override float StartTimeOffset => EffectiveStartTime;

    private readonly Dictionary<string, PlaybackObject> intermediateParentByParentId = [];

    private readonly Dictionary<string, string?> internalParentById = []; // maps ids to parents of internal objects
    private readonly Dictionary<string, HashSet<string>> childrenById = []; // maps ids to children of all parents

    private BeatmapPrefab? attachedPrefab;

    private readonly Identifier instanceId;

    public PrefabInstanceObjectSource(Identifier instanceId, TextShaper textShaper, PlaybackObjectContainer playbackObjects, RandomSeedService seedService)
        : base(textShaper, playbackObjects, seedService)
    {
        this.instanceId = instanceId;
    }

    public override void Dispose()
    {
        DetachPrefab();
        base.Dispose();
    }

    public void AttachPrefab(BeatmapPrefab beatmapPrefab)
    {
        if (attachedPrefab is not null)
            throw new InvalidOperationException($"A {nameof(BeatmapPrefab)} is already attached");

        attachedPrefab = beatmapPrefab;
        offset = attachedPrefab.Offset;

        foreach (var beatmapObject in beatmapPrefab.Objects.Values)
            InsertBeatmapObject(beatmapPrefab, beatmapObject);

        beatmapPrefab.Objects.Inserted += OnBeatmapObjectInserted;
        beatmapPrefab.Objects.Removed += OnBeatmapObjectRemoved;
        beatmapPrefab.PropertyChanged += OnPrefabPropertyChanged;
    }

    public void DetachPrefab()
    {
        if (attachedPrefab is null)
            return;

        attachedPrefab.Objects.Inserted -= OnBeatmapObjectInserted;
        attachedPrefab.Objects.Removed -= OnBeatmapObjectRemoved;
        attachedPrefab.PropertyChanged -= OnPrefabPropertyChanged;

        foreach (var intermediateParent in intermediateParentByParentId.Values)
        {
            var intermediateParentIndex = PlaybackObjects.GetIndexForId(intermediateParent.Id);
            PlaybackObjects.Remove(intermediateParentIndex);
            PlaybackObjects.SetParent(intermediateParentIndex, null);
        }

        foreach (var beatmapObject in attachedPrefab.Objects.Values)
        {
            var playbackObjectIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.Id));
            PlaybackObjects.Remove(playbackObjectIndex);
            PlaybackObjects.SetParent(playbackObjectIndex, null);

            DetachBeatmapObjectEvents(beatmapObject);
        }

        intermediateParentByParentId.Clear();
        internalParentById.Clear();
        childrenById.Clear();

        attachedPrefab = null;
    }

    protected override IEnumerable<BeatmapObject> GetBeatmapObjects()
        => attachedPrefab?.Objects.Values ?? Enumerable.Empty<BeatmapObject>();

    protected override Identifier GetPlaybackObjectId(string beatmapObjectId)
        => Identifier.Combine(instanceId, beatmapObjectId);

    private void UpdateIntermediateParentTransforms()
    {
        foreach (var intermediateParent in intermediateParentByParentId.Values)
        {
            intermediateParent.PositionSequence.LoadKeyframes([ new BakedKeyframe<Vector2>(0f, Ease.Linear, Position) ]);
            intermediateParent.ScaleSequence.LoadKeyframes([ new BakedKeyframe<Vector2>(0f, Ease.Linear, Scale) ]);
            intermediateParent.RotationSequence.LoadKeyframes([ new BakedKeyframe<float>(0f, Ease.Linear, Rotation) ]);
        }
    }

    private void OnPrefabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BeatmapPrefab.Offset))
        {
            Debug.Assert(attachedPrefab is not null);
            offset = attachedPrefab.Offset;
            UpdateStartTimes();
        }
    }

    private void OnBeatmapObjectInserted(object? sender, BeatmapObject e)
    {
        Debug.Assert(attachedPrefab is not null);
        InsertBeatmapObject(attachedPrefab, e);
    }

    private void OnBeatmapObjectRemoved(object? sender, BeatmapObject e)
    {
        RemoveBeatmapObject(e);
    }

    private void InsertBeatmapObject(BeatmapPrefab beatmapPrefab, BeatmapObject beatmapObject)
    {
        var playbackObject = CreatePlaybackObject(beatmapObject);

        // before inserting, check if we have any intermediate object of the same ID
        if (intermediateParentByParentId.TryGetValue(beatmapObject.Id, out var intermediateParent))
        {
            // we have an intermediate parent with the same ID as the beatmap object
            // we need to remove the intermediate parent and insert the beatmap object in its place

            var intermediateParentIndex = PlaybackObjects.GetIndexForId(intermediateParent.Id);
            PlaybackObjects.Remove(intermediateParentIndex);
            PlaybackObjects.SetParent(intermediateParentIndex, null);
            intermediateParentByParentId.Remove(beatmapObject.Id);

            // we don't need to reparent children, because both the intermediate parent and this object share the same internal ID
        }

        var index = PlaybackObjects.Insert(playbackObject);

        if (beatmapObject.ParentId is not null) // case: we have a parent
        {
            if (beatmapPrefab.Objects.ContainsKey(beatmapObject.ParentId))
            {
                var parentIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.ParentId));
                PlaybackObjects.SetParent(index, parentIndex);

                var childrenSet = childrenById.GetOrInsert(beatmapObject.ParentId, () => []);
                childrenSet.Add(beatmapObject.Id);
            }
            else // case: parent is external
            {
                GetIntermediateParent(beatmapObject.ParentId, out var intermediateParentIndex);

                PlaybackObjects.SetParent(index, intermediateParentIndex);

                var childrenSet = childrenById.GetOrInsert(beatmapObject.ParentId, () => []);
                childrenSet.Add(beatmapObject.Id);
            }
        }
        else // case: we don't have a parent
        {
            GetIntermediateParent(string.Empty, out var intermediateParentIndex);

            PlaybackObjects.SetParent(index, intermediateParentIndex);

            var childrenSet = childrenById.GetOrInsert(string.Empty, () => []);
            childrenSet.Add(beatmapObject.Id);
        }

        internalParentById.Add(beatmapObject.Id, beatmapObject.ParentId);

        AttachBeatmapObjectEvents(beatmapObject);
    }

    private void RemoveBeatmapObject(BeatmapObject beatmapObject)
    {
        var playbackObjectIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.Id));
        PlaybackObjects.Remove(playbackObjectIndex);
        internalParentById.Remove(beatmapObject.Id);

        // remove this object from its parent's children set
        if (beatmapObject.ParentId is not null)
        {
            if (childrenById.TryGetValue(beatmapObject.ParentId, out var childrenSet1))
            {
                childrenSet1.Remove(beatmapObject.Id);
                if (childrenSet1.Count == 0)
                {
                    childrenById.Remove(beatmapObject.ParentId);

                    // if the parent was also an intermediate parent, and there are no more children, remove it
                    if (intermediateParentByParentId.TryGetValue(beatmapObject.ParentId, out var intermediateParent))
                    {
                        var intermediateParentIndex = PlaybackObjects.GetIndexForId(intermediateParent.Id);
                        PlaybackObjects.Remove(intermediateParentIndex);
                        PlaybackObjects.SetParent(intermediateParentIndex, null);
                        intermediateParentByParentId.Remove(beatmapObject.ParentId);
                    }
                }
            }
        }

        // ensure there is an intermediate parent in this one's place
        // if there are any children
        if (childrenById.TryGetValue(beatmapObject.Id, out var childrenSet2) && childrenSet2.Count > 0)
            GetIntermediateParent(beatmapObject.Id, out _);

        DetachBeatmapObjectEvents(beatmapObject);
    }

    private PlaybackObject GetIntermediateParent(string parentId, out int index)
    {
        // check if we already have an intermediate parent for this ID
        if (intermediateParentByParentId.TryGetValue(parentId, out var playbackObject))
        {
            index = PlaybackObjects.GetIndexForId(playbackObject.Id);
            return playbackObject;
        }

        // if we don't, create new intermediate parent
        playbackObject = new PlaybackObject(GetPlaybackObjectId(parentId))
        {
            StartTime = 0f,
            EndTime = float.PositiveInfinity,
            ParentType = ParentType.All,
            Type = PlaybackObjectType.PrefabIntermediate
        };

        playbackObject.PositionSequence.LoadKeyframes([ new BakedKeyframe<Vector2>(0f, Ease.Linear, Position) ]);
        playbackObject.ScaleSequence.LoadKeyframes([ new BakedKeyframe<Vector2>(0f, Ease.Linear, Scale) ]);
        playbackObject.RotationSequence.LoadKeyframes([ new BakedKeyframe<float>(0f, Ease.Linear, Rotation) ]);

        index = PlaybackObjects.Insert(playbackObject);

        // parent this intermediate parent to the external parent
        if (parentId != string.Empty)
        {
            // since we're getting the EXTERNAl parent, we don't use GetPlaybackObjectId here
            var parentIndex = PlaybackObjects.GetIndexForId(parentId);
            PlaybackObjects.SetParent(index, parentIndex);
        }

        intermediateParentByParentId.Add(parentId, playbackObject);
        return playbackObject;
    }

    private void UpdateStartTimes()
    {
        if (attachedPrefab is null)
            return;

        foreach (var beatmapObject in attachedPrefab.Objects.Values)
        {
            if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
                continue;

            UpdateStartTime(beatmapObject, playbackObject);
        }
    }

    protected override void OnParentIdChanged(BeatmapObject beatmapObject, int index)
    {
        // get old parent
        if (internalParentById.TryGetValue(beatmapObject.Id, out var internalParent))
        {
            // remove from old parent's children set
            if (internalParent is not null)
            {
                if (childrenById.TryGetValue(internalParent, out var oldChildrenSet))
                {
                    oldChildrenSet.Remove(beatmapObject.Id);
                    if (oldChildrenSet.Count == 0)
                    {
                        childrenById.Remove(internalParent);

                        // if the old parent was also an intermediate parent, and there are no more children, remove it
                        if (intermediateParentByParentId.TryGetValue(internalParent, out var intermediateParent))
                        {
                            var intermediateParentIndex = PlaybackObjects.GetIndexForId(intermediateParent.Id);
                            PlaybackObjects.Remove(intermediateParentIndex);
                            PlaybackObjects.SetParent(intermediateParentIndex, null);
                            intermediateParentByParentId.Remove(internalParent);
                        }
                    }
                }
            }
        }

        Debug.Assert(attachedPrefab is not null);
        if (beatmapObject.ParentId is not null) // case: we have a parent
        {
            if (attachedPrefab.Objects.ContainsKey(beatmapObject.ParentId))
            {
                var parentIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.ParentId));
                PlaybackObjects.SetParent(index, parentIndex);

                var childrenSet = childrenById.GetOrInsert(beatmapObject.ParentId, () => []);
                childrenSet.Add(beatmapObject.Id);
            }
            else // case: parent is external
            {
                GetIntermediateParent(beatmapObject.ParentId, out var intermediateParentIndex);

                PlaybackObjects.SetParent(index, intermediateParentIndex);

                var childrenSet = childrenById.GetOrInsert(beatmapObject.ParentId, () => []);
                childrenSet.Add(beatmapObject.Id);
            }
        }
        else // case: we don't have a parent
        {
            GetIntermediateParent(string.Empty, out var intermediateParentIndex);

            PlaybackObjects.SetParent(index, intermediateParentIndex);

            var childrenSet = childrenById.GetOrInsert(string.Empty, () => []);
            childrenSet.Add(beatmapObject.Id);
        }

        internalParentById[beatmapObject.Id] = beatmapObject.ParentId;
    }
}
