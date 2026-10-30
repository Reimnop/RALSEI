namespace Ralsei.Platform.Window;

/// <summary>The picture the pointer is shown as over a window.</summary>
public enum CursorShape
{
    Arrow,

    /// <summary>Over text that can be edited.</summary>
    IBeam,

    Crosshair,

    /// <summary>Over something that can be pressed.</summary>
    PointingHand,

    /// <summary>Over something that is stretched to the left and right.</summary>
    ResizeHorizontal,

    /// <summary>Over something that is stretched up and down.</summary>
    ResizeVertical,

    /// <summary>Over something that is moved in any direction.</summary>
    Move,

    ResizeNorthWestSouthEast,
    ResizeNorthEastSouthWest,

    /// <summary>Over what can not be done.</summary>
    NotAllowed
}
