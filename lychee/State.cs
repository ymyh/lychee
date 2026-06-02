namespace lychee;

/// <summary>
/// Represents a finite state machine state resource.
/// Tracks the current and previous state values, and signals when a transition occurs.
/// </summary>
/// <typeparam name="T">The state type, typically an enum.</typeparam>
public sealed class State<T> where T : Enum
{
    /// <summary>
    /// The current state value.
    /// </summary>
    public T Current { get; private set; }

    /// <summary>
    /// The previous state value before the last transition.
    /// </summary>
    public T Previous { get; private set; }

    /// <summary>
    /// Whether the state has changed since the last cleanup.
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Creates a new state with the specified initial value.
    /// </summary>
    /// <param name="initial">The initial state value.</param>
    public State(T initial)
    {
        Current = initial;
        Previous = initial;
        Changed = true;
    }

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

    internal void ClearChanged()
    {
        Changed = false;
    }
}
