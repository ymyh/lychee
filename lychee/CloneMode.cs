namespace lychee;

/// <summary>
/// How much of an entity a copy includes. Configured at the call site of
/// <see cref="Commands.CopyEntity(in Entity, CloneMode)"/> or <see cref="Entity.Copy(CloneMode)"/>.
/// </summary>
public enum CloneMode
{
    /// <summary>
    /// Copies the entity alone. A copied <c>ChildOf</c> still points at the original parent, so the copy
    /// becomes a sibling of the source, and the copy itself has no children.
    /// </summary>
    Shallow,

    /// <summary>
    /// Recursively copies the entity and, for every relationship whose target is marked linked, its whole
    /// subtree. A copied relationship component is repointed at the copied parent.
    /// </summary>
    Linked,
}
