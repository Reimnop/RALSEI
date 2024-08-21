using Ralsei.Core.Data;
using Ralsei.Core.Model;
using Ralsei.Core.Text;
using BeatmapObject = Ralsei.Core.Model.BeatmapObject;

namespace Ralsei.Core.Service;

public class MainObjectSource : ObjectSourceBase
{
    private BeatmapData? attachedBeatmapData;

    public MainObjectSource(TextShaper textShaper, PlaybackObjectContainer playbackObjects, RandomSeedService seedService)
        : base(textShaper, playbackObjects, seedService)
    {
    }

    public override void Dispose()
    {
        DetachBeatmapData();
        base.Dispose();
    }

    public void AttachBeatmapData(BeatmapData beatmapData)
    {
        if (attachedBeatmapData is not null)
            throw new InvalidOperationException($"A {nameof(BeatmapData)} is already attached");

        attachedBeatmapData = beatmapData;

        // add existing objects
        foreach (var beatmapObject in beatmapData.Objects.Values)
            InsertBeatmapObject(beatmapObject);

        // attach events
        beatmapData.Objects.Inserted += OnBeatmapObjectInserted;
        beatmapData.Objects.Removed += OnBeatmapObjectRemoved;
    }

    public void DetachBeatmapData()
    {
        if (attachedBeatmapData is null)
            return;

        // remove existing objects
        foreach (var beatmapObject in attachedBeatmapData.Objects.Values)
        {
            // remove from container
            var playbackObjectIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.Id));
            PlaybackObjects.Remove(playbackObjectIndex);
            PlaybackObjects.SetParent(playbackObjectIndex, null);

            DetachBeatmapObjectEvents(beatmapObject);
        }

        // detach events
        attachedBeatmapData.Objects.Inserted -= OnBeatmapObjectInserted;
        attachedBeatmapData.Objects.Removed -= OnBeatmapObjectRemoved;

        attachedBeatmapData = null;
    }

    protected override IEnumerable<BeatmapObject> GetBeatmapObjects()
        => attachedBeatmapData?.Objects.Values ?? Enumerable.Empty<BeatmapObject>();

    protected override Identifier GetPlaybackObjectId(string beatmapObjectId)
        => beatmapObjectId;

    protected override void OnParentIdChanged(BeatmapObject beatmapObject, int playbackObjectIndex)
    {
        if (beatmapObject.ParentId is not null)
        {
            var parentIndex = PlaybackObjects.GetIndexForId(beatmapObject.ParentId);
            PlaybackObjects.SetParent(playbackObjectIndex, parentIndex);
        }
        else
        {
            PlaybackObjects.SetParent(playbackObjectIndex, null);
        }
    }

    private void OnBeatmapObjectInserted(object? sender, BeatmapObject e)
    {
        InsertBeatmapObject(e);
    }

    private void OnBeatmapObjectRemoved(object? sender, BeatmapObject e)
    {
        RemoveBeatmapObject(e);
    }

    private void InsertBeatmapObject(BeatmapObject beatmapObject)
    {
        // create playback object
        var playbackObject = CreatePlaybackObject(beatmapObject);

        // insert into container
        var index = PlaybackObjects.Insert(playbackObject);

        // set parent
        if (beatmapObject.ParentId is not null)
        {
            var parentIndex = PlaybackObjects.GetIndexForId(beatmapObject.ParentId);
            PlaybackObjects.SetParent(index, parentIndex);
        }
        else
        {
            PlaybackObjects.SetParent(index, null);
        }

        AttachBeatmapObjectEvents(beatmapObject);
    }

    private void RemoveBeatmapObject(BeatmapObject beatmapObject)
    {
        // remove from container
        var playbackObjectIndex = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.Id));
        PlaybackObjects.Remove(playbackObjectIndex);

        DetachBeatmapObjectEvents(beatmapObject);
    }
}
