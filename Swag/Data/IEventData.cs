using System.Numerics;

namespace Swag.Data;

public interface IEventData
{
    Vector2 CameraPosition { get; }
    float CameraScale { get; }
    float CameraRotation { get; }
}