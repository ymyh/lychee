using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee.Tests;

[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct TestPosition : IComponent
{
    public float X;
    public float Y;

    public readonly ComponentMeta GetComponentMeta() => new(8);
}

[StructLayout(LayoutKind.Sequential, Size = 4)]
internal struct TestHealth : IComponent
{
    public float Value;

    public readonly ComponentMeta GetComponentMeta() => new(4);
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct TestVelocity : IComponent
{
    public float DX;
    public float DY;

    public readonly ComponentMeta GetComponentMeta() => new(8);
}

internal struct TestMovement : IComponentBundle
{
    public TestPosition Position;
    public TestVelocity Velocity;
}

internal class TestResource
{
    public int Value;
}
