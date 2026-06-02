using lychee.attributes;
using lychee.components;

namespace lychee.systems;

/// <summary>
/// Automatically despawns entities whose <see cref="StateScoped{T}"/> value no longer matches the current state.
/// One instance is registered per state type via <see cref="App.AddState{T}"/>.
/// </summary>
/// <typeparam name="T">The state type.</typeparam>
[AutoImplSystem]
public sealed partial class StateCleanupSystem<T> where T : unmanaged, Enum
{
    private static bool changed;

    private static void Execute(Commands commands, [Resource] State<T> state, StateScoped<T> stateScoped, in Entity entity)
    {
        if (!stateScoped.Value.Equals(state.Current))
        {
            commands.RemoveEntity(in entity);
            changed = true;
        }
    }

    private static void AfterExecute()
    {
        if (changed)
        {
            ResourceDataAG.state.ClearChanged();
        }
    }

    public bool Predicate(ResourcePool pool)
    {
        if (!pool.HasResource<State<T>>())
        {
            return false;
        }

        return pool.GetResource<State<T>>().Changed;
    }
}
