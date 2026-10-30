namespace Swag.Avalonia.Integration;

/// <summary>Whether the OpenGL bindings were loaded in this process. They are global, and whoever draws first loads them.</summary>
public static class GlBindingsState
{
    public static volatile bool IsLoaded;
}
