using lychee.components;

namespace lychee.attributes;

/// <summary>
/// Specifies component type filters for selecting which archetypes a system should process.
/// </summary>
/// <remarks>
/// <para>Filters are evaluated as: (All components present) AND (at least one Any component present) AND (no None components present).</para>
///
/// <para><b>Implicit Disabled filtering:</b> The <c>None</c> filter automatically includes <see cref="components.Disabled"/> to skip disabled entities,
/// unless <c>Disabled</c> is already present in <c>All</c> or <c>Any</c>.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SystemFilter : Attribute
{
    /// <summary>
    /// Component types that must all be present for an archetype to match.
    /// </summary>
    public Type[] All { get; init; } = [];

    /// <summary>
    /// Component types of which at least one must be present for an archetype to match.
    /// </summary>
    public Type[] Any { get; init; } = [];

    /// <summary>
    /// Component types that must not be present for an archetype to match.
    /// </summary>
    /// <remarks>
    /// Automatically includes <see cref="Disabled"/> unless it is already present in <see cref="All"/> or <see cref="Any"/>.
    /// This allows systems to process only enabled entities by default.
    /// </remarks>
    public Type[] None { get; init; } = [];
}
