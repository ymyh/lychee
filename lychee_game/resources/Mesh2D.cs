using System.Numerics;

namespace lychee_game.resources;

public sealed class Mesh2D
{
    public string Name;

    public Vector3[] Positions;

    public Vector3[] Normals;

    public Vector2[] UVs;

    public Vector4[] Colors;

    public object[] Textures;

    public int[] Indices;

    public int VertexCount;

    public int IndexCount;

    public Vector2 BoundsMin;

    public Vector2 BoundsMax;
}
