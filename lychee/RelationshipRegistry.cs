namespace lychee;

/// <summary>
/// The erased description of a registered relationship, used by cloning to walk and remap relationships
/// without knowing their concrete types. The framework fills one in per registered relationship and stores
/// it under both the source and the target component type id.
/// </summary>
internal sealed unsafe class RelationshipInfo
{
    /// <summary>The component type id of the relationship component, such as <c>ChildOf</c>.</summary>
    public int RelationshipTypeId;

    /// <summary>The component type id of the reverse collection component.</summary>
    public int TargetTypeId;

    /// <summary>
    /// Whether removing the target cascades to the related entities, and whether a linked clone copies the
    /// whole subtree.
    /// </summary>
    public bool LinkedSpawn;

    /// <summary>Reads the related count from a target component pointed at by <c>void*</c>.</summary>
    public RelatedCountInvoker RelatedCount = null!;

    /// <summary>Reads a related entity from a target component pointed at by <c>void*</c>.</summary>
    public GetRelatedInvoker GetRelated = null!;

    /// <summary>Reads the target of a relationship component pointed at by <c>void*</c>.</summary>
    public GetTargetInvoker GetTarget = null!;

    /// <summary>Rewrites the target of a relationship component pointed at by <c>void*</c>.</summary>
    public delegate* unmanaged<void*, EntityRef, void> RemapTarget;
}

/// <summary>Reads the related count from a target component.</summary>
internal unsafe delegate int RelatedCountInvoker(void* target);

/// <summary>Reads a related entity from a target component.</summary>
internal unsafe delegate EntityRef GetRelatedInvoker(void* target, int index);

/// <summary>Reads the target of a relationship component.</summary>
internal unsafe delegate EntityRef GetTargetInvoker(void* relationship);

/// <summary>
/// Maps component type ids to the relationship they participate in. Both the source component type id and
/// the target component type id resolve to the same <see cref="RelationshipInfo"/>.
/// </summary>
internal sealed class RelationshipRegistry
{
#region Private Fields

    private readonly Dictionary<int, RelationshipInfo> infoDict = [];

#endregion

#region Internal Methods

    /// <summary>
    /// Stores a relationship description under both of its component type ids.
    /// </summary>
    /// <param name="info">The description to store.</param>
    internal void Register(RelationshipInfo info)
    {
        infoDict[info.RelationshipTypeId] = info;
        infoDict[info.TargetTypeId] = info;
    }

    /// <summary>
    /// Tries to resolve the relationship a component type id belongs to, whatever side it is on.
    /// </summary>
    /// <param name="typeId">The component type id to look up.</param>
    /// <param name="info">When this method returns, the relationship description if found; otherwise, null.</param>
    /// <returns>True if the type participates in a relationship; otherwise, false.</returns>
    internal bool TryGet(int typeId, out RelationshipInfo info)
    {
        return infoDict.TryGetValue(typeId, out info!);
    }

    /// <summary>
    /// Checks whether a component type id is the reverse collection side of a relationship.
    /// </summary>
    /// <param name="typeId">The component type id to check.</param>
    /// <returns>True if the type is a relationship target; otherwise, false.</returns>
    internal bool IsTargetType(int typeId)
    {
        return infoDict.TryGetValue(typeId, out var info) && info.TargetTypeId == typeId;
    }

    /// <summary>
    /// Tries to resolve a relationship from its reverse collection side.
    /// </summary>
    /// <param name="typeId">The target component type id to look up.</param>
    /// <param name="info">When this method returns, the relationship description if found; otherwise, null.</param>
    /// <returns>True if the type is a relationship target with a registered description; otherwise, false.</returns>
    internal bool TryGetTarget(int typeId, out RelationshipInfo info)
    {
        if (infoDict.TryGetValue(typeId, out info!) && info.TargetTypeId == typeId)
        {
            return true;
        }

        info = null!;

        return false;
    }

#endregion
}
