using System.Runtime.InteropServices;
using lychee.attributes;

namespace lychee_game.components;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
[Component]
public partial struct MeshRef
{
    public int Index;
}
