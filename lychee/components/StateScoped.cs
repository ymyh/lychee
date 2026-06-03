using lychee.attributes;

namespace lychee.components;

/// <summary>
/// Marks an entity as scoped to a specific state value.
/// When the state changes, entities scoped to the previous state are automatically despawned.
/// </summary>
/// <typeparam name="T">The state type, typically an enum.</typeparam>
[Component]
public partial struct StateScoped<T>(T value) : IEquatable<StateScoped<T>>
    where T : unmanaged, Enum
{
#region Public Fields

    /// <summary>
    /// The state value this entity is scoped to.
    /// </summary>
    public T Value = value;

#endregion

#region Public Methods

    public bool Equals(StateScoped<T> other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is StateScoped<T> scoped && Equals(scoped);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(StateScoped<T> left, StateScoped<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StateScoped<T> left, StateScoped<T> right)
    {
        return !(left == right);
    }

#endregion
}
