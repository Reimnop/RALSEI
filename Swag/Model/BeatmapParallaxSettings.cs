namespace Swag.Model;

/// <summary>The beatmap's settings for what is in the background: how far away each of its layers is, and the depth of field that blurs it.</summary>
public class BeatmapParallaxSettings
{
    /// <summary>How many layers there are. An object in the background layer names the one it is in by its render depth.</summary>
    public const int LayerCount = 20;

    public const float DefaultDepthOfField = 40.0f;

    /// <summary>Raised when the depth of field changes, which the player events resolve their own against.</summary>
    public event EventHandler? DepthOfFieldChanged;

    private readonly int[] layerDepths = new int[LayerCount];

    /// <summary>Whether the depth of field blurs the background, unless the player events say otherwise.</summary>
    public bool IsDepthOfFieldEnabled
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            DepthOfFieldChanged?.Invoke(this, EventArgs.Empty);
        }
    } = true;

    /// <summary>How far in front of the first layer the depth of field is focused, unless the player events say otherwise.</summary>
    public float DepthOfField
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            DepthOfFieldChanged?.Invoke(this, EventArgs.Empty);
        }
    } = DefaultDepthOfField;

    public BeatmapParallaxSettings()
    {
        Reset();
    }

    /// <summary>The distance of a layer from the background's root, which the layers are in front of one another by.</summary>
    public int GetLayerDepth(int layerIndex)
        => layerDepths[Math.Clamp(layerIndex, 0, LayerCount - 1)];

    public void SetLayerDepth(int layerIndex, int depth)
        => layerDepths[layerIndex] = depth;

    public void Reset()
    {
        IsDepthOfFieldEnabled = true;
        DepthOfField = DefaultDepthOfField;
        for (var i = 0; i < LayerCount; i++)
            layerDepths[i] = GetDefaultLayerDepth(i);
    }

    public static int GetDefaultLayerDepth(int layerIndex)
        => Math.Min((layerIndex + 1) * 100, 2000);
}
