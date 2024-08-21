namespace Ralsei.Core.Data;

public enum PlaybackObjectType
{
    Visible,
    Invisible,
    PrefabIntermediate,
    Camera,
    Particles
}

public static class PlaybackObjectTypeExtension
{
    /// <summary>
    /// Whether objects of this type take part in playback (timeline, sorting and drawing).
    /// </summary>
    public static bool IsDrawable(this PlaybackObjectType type)
        => type is PlaybackObjectType.Visible or PlaybackObjectType.Particles;
}
