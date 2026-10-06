using lychee.attributes;

namespace lychee.components;

[Component]
public partial struct ChildOf
{
    /// <summary>
    /// The parent entity reference.
    /// </summary>
    public EntityRef Parent { get; internal set; }
}
