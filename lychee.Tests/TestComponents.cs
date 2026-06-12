using lychee.attributes;
using lychee.interfaces;

namespace lychee.Tests;

[Component]
internal partial struct TestPosition
{
    public float X;
    public float Y;
}

[Component]
internal partial struct TestHealth
{
    public float Value;
}

[Component]
internal partial struct TestVelocity
{
    public float DX;
    public float DY;
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
