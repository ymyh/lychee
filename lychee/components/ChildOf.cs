using lychee.attributes;

namespace lychee.components;

/// <summary>
/// Marks this entity as a child of <see cref="Parent"/>. This component is the single source of
/// truth for the parent-child hierarchy; the reverse index (<see cref="World.Hierarchy"/>) is
/// maintained automatically when the component is added, replaced, or removed through Commands
/// (including <c>Commands.AddChild</c> / <c>Commands.RemoveChild</c>).
/// </summary>
/// <remarks>
/// Do not mutate this component in place through a component ref — the hierarchy index will not
/// be updated. Cycles (including self-parenting) are not supported.
/// </remarks>
[Component]
public partial struct ChildOf
{
    /// <summary>
    /// The parent entity reference.
    /// </summary>
    public EntityRef Parent;
}
