using lychee.interfaces;

namespace lychee;

/// <summary>
/// Identifies the kind of change a component hook observes.
/// </summary>
public enum ComponentHookKind
{
    /// <summary>A component becomes present on an entity.</summary>
    OnAdd,

    /// <summary>An existing component value is overwritten in place.</summary>
    OnReplace,

    /// <summary>A component leaves an entity.</summary>
    OnRemove,
}

/// <summary>
/// A callback registered per component type and hook kind.
/// </summary>
/// <typeparam name="T">The component type the hook observes, must be unmanaged and implement IComponent.</typeparam>
/// <param name="context">The context of the event, including the entity and the commands that issued it.</param>
/// <param name="component">
/// The value the event observes: the new value for OnAdd, the current value for OnReplace, and the old value for OnRemove.
/// </param>
public delegate void ComponentHook<T>(ref HookContext context, in T component) where T : unmanaged, IComponent;

/// <summary>
/// The type-erased form of a <see cref="ComponentHook{T}"/> as stored by the <see cref="TypeRegistrar"/>.
/// The wrapper turns the raw pointer back into the typed component before invoking the user hook.
/// </summary>
internal unsafe delegate void ComponentHookInvoker(ref HookContext context, void* component);

/// <summary>
/// Describes a component event and gives the hook read access to the entity it happened on.
/// Hooks are invoked synchronously inside the operation that caused them.
/// </summary>
public struct HookContext
{
#region Properties & Fields

    /// <summary>
    /// Gets the reference of the entity the event belongs to.
    /// </summary>
    public EntityRef Entity { get; }

    /// <summary>
    /// Gets the commands that triggered the hook. Structural changes made through it take effect immediately
    /// and synchronously trigger their own hooks.
    /// </summary>
    public Commands Commands { get; }

    /// <summary>
    /// Gets the application, which the hook can use to read resources.
    /// </summary>
    public App App { get; }

    internal unsafe void* Previous;

#endregion

#region Constructors

    internal HookContext(EntityRef entity, Commands commands, App app)
    {
        Entity = entity;
        Commands = commands;
        App = app;
        unsafe
        {
            Previous = null;
        }
    }

#endregion

#region Public Methods

    /// <summary>
    /// Checks whether the entity currently has a component of the specified type.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the entity has the component; otherwise, false.</returns>
    public readonly bool Has<T>() where T : unmanaged, IComponent
    {
        return Commands.HasLiveComponent<T>(Entity);
    }

    /// <summary>
    /// Tries to read a component of the specified type from the entity's current position.
    /// </summary>
    /// <typeparam name="T">The component type to read, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="component">When this method returns, contains the component if found; otherwise, the default value.</param>
    /// <returns>True if the component was found; otherwise, false.</returns>
    public readonly bool TryGet<T>(out T component) where T : unmanaged, IComponent
    {
        return Commands.TryGetLiveComponent(Entity, out component);
    }

    /// <summary>
    /// Gets a reference to a component of the specified type at the entity's current position.
    /// Writing through the returned reference does not trigger hooks.
    /// </summary>
    /// <typeparam name="T">The component type to read, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component.</returns>
    public readonly ref T GetComponent<T>() where T : unmanaged, IComponent
    {
        return ref Commands.GetLiveComponent<T>(Entity);
    }

    /// <summary>
    /// Tries to read the value a component had before it was overwritten.
    /// This is only meaningful for OnReplace hooks and returns false for every other kind.
    /// </summary>
    /// <typeparam name="T">The component type of the previous value.</typeparam>
    /// <param name="previous">When this method returns, contains the previous value if available; otherwise, the default value.</param>
    /// <returns>True if a previous value is available; otherwise, false.</returns>
    public readonly bool TryGetPrevious<T>(out T previous) where T : unmanaged, IComponent
    {
        unsafe
        {
            if (Previous == null)
            {
                previous = default;
                return false;
            }

            previous = *(T*)Previous;
        }

        return true;
    }

#endregion
}
