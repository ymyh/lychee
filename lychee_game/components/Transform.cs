using System.Numerics;
using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee_game.components;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Position : IComponent
{
    public Vector3 Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Rotation : IComponent
{
    public Vector3 Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Scale : IComponent
{
    public Vector3 Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 64)]
public struct Transform : IComponent
{
    public Matrix4x4 Matrix;

    public Position Position;

    public Rotation Rotation;

    public Scale Scale;
}
