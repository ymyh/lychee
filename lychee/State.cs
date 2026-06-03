namespace lychee;

/// <summary>
/// Represents a finite state machine state resource.
/// Tracks the current and previous state values, and signals when a transition occurs.
/// </summary>
/// <typeparam name="T">The state type, typically an enum.</typeparam>
public sealed class State<T>(T initial) where T : Enum
{
#region Public Properties

    /// <summary>
    /// The current state value.
    /// </summary>
    public T Current { get; private set; } = initial;

    /// <summary>
    /// The previous state value before the last transition.
    /// </summary>
    public T Previous { get; private set; } = initial;

    /// <summary>
    /// Whether the state has changed since the last cleanup.
    /// </summary>
    public bool Changed { get; private set; } = true;

#endregion

#region Public Methods

    /// <summary>
    /// Transitions to a new state. If the new value equals the current value, no transition occurs.
    /// </summary>
    /// <param name="newState">The new state value.</param>
    public void Set(T newState)
    {
        if (Current.Equals(newState))
        {
            return;
        }

        Previous = Current;
        Current = newState;
        Changed = true;
    }

#endregion

#region Internal Methods

    internal void ClearChanged()
    {
        Changed = false;
    }

#endregion
}
