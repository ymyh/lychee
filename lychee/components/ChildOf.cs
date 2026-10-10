using System.Runtime.InteropServices;
using lychee.attributes;

namespace lychee.components;

/// <summary>
/// The built-in parent relationship. It lives on the child and points at the parent, and is the only
/// authoritative copy of the hierarchy. The parent's
/// <see cref="RelationshipTarget{TRelationship}"/> of <see cref="ChildOf"/> is maintained by the framework.
/// </summary>
[Component]
public partial struct ChildOf : IRelationship
{
#region Internal Fields

    // Kept internal rather than private so ChildOfCloneSupport can rewrite it through a raw pointer while
    // cloning. No other code should mutate it: change the parent through ReplaceComponent or an out slot.
    internal EntityRef parent;

#endregion

#region Constructors

    /// <summary>
    /// Creates a relationship to the given parent.
    /// </summary>
    /// <param name="parent">The parent entity.</param>
    public ChildOf(EntityRef parent) => this.parent = parent;

#endregion

#region Public Properties

    /// <summary>
    /// Gets the parent entity. This value is read-only; changing the parent requires
    /// <c>ReplaceComponent</c> or an <c>out</c> system slot so the relationship hooks run.
    /// </summary>
    public readonly EntityRef Parent => parent;

#endregion

#region IRelationship Implementation

    readonly EntityRef IRelationship.Target => parent;

#endregion
}

/// <summary>
/// Rewrites the parent of a <see cref="ChildOf"/> through a raw pointer, for linked cloning only.
/// </summary>
internal static unsafe class ChildOfCloneSupport
{
    /// <summary>
    /// Sets a new parent on a relationship component addressed by <c>void*</c>.
    /// </summary>
    /// <param name="relationship">A pointer to a <see cref="ChildOf"/>.</param>
    /// <param name="newTarget">The parent to write.</param>
    [UnmanagedCallersOnly]
    public static void RemapTarget(void* relationship, EntityRef newTarget)
    {
        ((ChildOf*)relationship)->parent = newTarget;
    }
}
