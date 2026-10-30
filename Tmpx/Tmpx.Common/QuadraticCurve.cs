using System.Numerics;
using System.Runtime.InteropServices;

namespace Tmpx.Common;

[StructLayout(LayoutKind.Sequential)]
public struct QuadraticCurve
{
    public Vector2 P0;
    public Vector2 P1;
    public Vector2 P2;
}