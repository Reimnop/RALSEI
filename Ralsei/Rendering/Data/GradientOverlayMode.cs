namespace Ralsei.Rendering.Data;

/// <summary>How the colours of a gradient overlay are put over the picture.</summary>
public enum GradientOverlayMode
{
    /// <summary>The gradient replaces what it covers, by its intensity.</summary>
    Linear,

    /// <summary>The gradient is added to the picture.</summary>
    Additive,

    /// <summary>The gradient is multiplied with the picture.</summary>
    Multiply,

    /// <summary>The gradient is laid over the picture the way a screen is.</summary>
    Screen
}
