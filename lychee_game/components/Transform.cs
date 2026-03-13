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

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Transform : IComponent
{
    public Position Position;

    public Rotation Rotation;

    public Scale Scale;

    public void CalculateTransform(out Matrix4x4 result)
    {
        result = Matrix4x4.CreateScale(Scale.Value) * Matrix4x4.CreateFromYawPitchRoll(Rotation.Value.Y, Rotation.Value.X, Rotation.Value.Z) * Matrix4x4.CreateTranslation(Position.Value);
    }
}
