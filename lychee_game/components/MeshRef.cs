using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee_game.components;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MeshRef : IComponent
{
    public int Index;
}
