<div align="center"><img width="512" height="512" src="https://github.com/ymyh/lychee/blob/main/logo.svg"/></div>

# LYCHEE

A simple archetype-based ECS (Entity-Component-System) framework for .NET 10.0 / C# 14.

## Features

- **Cache-friendly archetype storage** - Components are grouped by archetype for improved data locality, but moving entities
  between archetypes (e.g., adding/removing components) incurs data copy overhead and is cache-unfriendly.
- **Automatic parallelism** - DAG-based System dependency analysis provides basic automatic identification of Systems that can
  execute in parallel
- **Source generation** - Use the `[AutoImplSystem]` Attribute to automatically generate System code
- **Deferred commands** - Structural changes are recorded during Systems and applied serially at commit points, so
  an `Entity` is a handle and nothing it changes is visible until the next commit
- **Flexible scheduling system** - Supports single-thread/multi-thread execution with configurable commit timing
- **Component hooks** - Observe `OnAdd` / `OnReplace` / `OnRemove` per component type and react synchronously while
  recorded commands are applied
- **Parent-child relationships** - The built-in `ChildOf` relationship keeps a read-only `Children` collection in sync
  and despawns subtrees cascadingly, with shallow and linked cloning

## Project Structure

```
lychee/          - Core ECS framework library
lychee_game/     - Game-specific plugin with common schedules
lychee_sg/       - Source generator/analyzer package
```

## Quick Start

### 1. Define Components

Components are unmanaged data structures implementing the `IComponent` interface:

```csharp
using lychee.attributes;

[Component]
struct Position
{
    public float X;
    public float Y;
}

[Component]
struct Velocity
{
    public float X;
    public float Y;
}

[Component]
struct Health
{
    public float Value;
}
```

**Recommended practice**: Use the `StructLayout` Attribute to specify memory layout and alignment for improved cache
efficiency. If no value is provided, the default alignment is 8.

### 2. Define Component Bundles (Optional)

A component bundle is a set of related components that can be added to an entity in a single operation:

```csharp
using lychee.interfaces;

struct Movement : IComponentBundle
{
    public Velocity Velocity;
    public Position Position;
}
```

### 3. Write Systems

Mark a System class with the `[AutoImplSystem]` Attribute and include an `Execute` method (static or non-static). The
source generator will automatically generate the implementation:

```csharp
using lychee.attributes;

[AutoImplSystem]
partial class MovementSystem
{
    private static void Execute(ref Position pos, in Velocity vel)
    {
        pos.X += vel.X;
        pos.Y += vel.Y;
    }
}
```

The `[AutoImplSystem]` attribute accepts an optional `multiThreaded` parameter(default `false`). When set to `true`, entity iteration
within the system is parallelized across multiple threads:

```csharp
[AutoImplSystem(multiThreaded: true)]
partial class MovementSystem
{
    private static void Execute(ref Position pos, in Velocity vel)
    {
        pos.X += vel.X;
        pos.Y += vel.Y;
    }
}
```

When using `multiThreaded`, pass a `SystemDescriptor` to `AddSystem` to control the thread count and group size:

```csharp
schedule.AddSystem<MovementSystem>(new SystemDescriptor
{
    ThreadCount = 4,   // Number of threads for this system
    GroupSize = 128    // Number of entities each thread processes per batch
});
```

- `ThreadCount` - How many threads to use for parallel entity iteration
- `GroupSize` - How many entities each thread processes in one batch

**The `Execute` method accepts four types of parameters**:

- Component types - Components can be passed by value or by reference. When passed by reference, mutability may affect
  System execution scheduling (only applies in multi-threaded execution mode)
- Resource types - Use the `[Resource]` Attribute on parameters to access globally unique resources. When passed by
  reference, the same effects apply as above
- `Commands` - Records deferred entity operations (creation, deletion, adding components, etc.). Nothing is applied
  until the next commit point
- `Entity` - A handle to the entity currently being processed, accepted as `in Entity entity` or `ref Entity entity`.
  It resolves its location from the world on demand

In addition to `Execute`, you can also define two methods named `BeforeExecute` and `AfterExecute`, which will execute
before or after `Execute` respectively. They cannot accept any parameters, so they can only be used for simple
functionality.

You can also override the `Predicate` method (from `ISystem`) to control whether a system should execute. It receives a
`ResourcePool` parameter and returns a `bool` — returning `false` will skip the system's execution entirely. By default,
it returns `true`.

#### System Filters

Use the `[SystemFilter]` Attribute to control which entities a System processes:

```csharp
using lychee.attributes;
using lychee.components;

// Only process entities that have Position and at least one of Velocity or Acceleration
// Exclude entities with the Disabled component
[SystemFilter(
    All = new[] { typeof(Position) },
    Any = new[] { typeof(Velocity), typeof(Acceleration) },
    None = new[] { typeof(Disabled) }
)]
[AutoImplSystem]
partial class PhysicsSystem
{
    private static void Execute(ref Position pos, in Velocity vel)
    {
        // Processing logic
    }
}
```

**Filter rules**:

- `All` - Must contain all listed components
- `Any` - Must contain at least one listed component
- `None` - Must not contain any listed components
- By default, entities with the `Disabled` component are automatically excluded (unless explicitly included in `All` or
  `Any`)

### 4. Create and Run Application

```csharp
using lychee;
using lychee_game;

// Create application
using var app = new App();

// Install game plugin (provides common schedules)
var gamePlugin = app.InstallPlugin<BasicGamePlugin>();

// Add Systems to schedules
gamePlugin.StartUp.AddSystem<InitSystem>();
gamePlugin.Update.AddSystems<(MovementSystem, HealthSystem)>();

// Execute
app.Update();
```

### 5. Use Commands to Modify Entities

Use the `Commands` parameter in Systems to perform entity operations:

```csharp
[AutoImplSystem]
partial class InitSystem
{
    private static void Execute(Commands commands)
    {
        // Create entities and add components
        for (var i = 0; i < 1000; i++)
        {
            var entity = commands.CreateEntity();
            entity.AddComponents(new Movement
            {
                Velocity = new() { X = 1.0f, Y = 0.5f },
                Position = new()
            });
        }
    }
}

[AutoImplSystem]
partial class DespawnSystem
{
    private static void Execute(Commands commands, ref Health health, ref Entity entity)
    {
        if (health.Value <= 0)
        {
            commands.RemoveEntity(entity);
        }
    }
}
```

**Commands operations**:

- `CreateEntity()` / `CreateEntityWithComponent<T>(in T)` / `CreateEntityWithComponents<T>(in T)` - Create an entity
- `RemoveEntity(Entity)` or `entity.Despawn()` - Remove an entity and, with it, its whole `Children` subtree
- `AddComponent<T>(in T)` / `AddComponents<T>(in T)` - Add a component or a component bundle to an entity
- `RemoveComponent<T>()` / `RemoveComponents<T>()` / `RemoveComponentsTuple<T>()` - Remove one or more components
- `ReplaceComponent<T>(in T)` - Overwrite an existing value and fire `OnReplace`
- `AlterComponents(configure)` - Add and remove components in a single archetype migration
- `CopyEntity(in Entity, CloneMode)` or `entity.Copy(CloneMode)` - Copy an entity, optionally with its linked subtree

> **Deferred semantics**: each of these only *records* an operation. The world changes when the buffer is applied at
> the next commit point, and the hooks fire there. An entity created in the current System therefore has no components
> until then, and `Entity` is a lightweight handle: it carries an ID and resolves its location from the world on demand.

### 6. Entity Runtime Operations

The `Entity` struct provides methods for runtime component queries and batch modifications:

```csharp
// Get a reference to a component
ref Position pos = ref entity.GetComponent<Position>();

// Check if entity has a specific component
if (entity.WithComponent<Disabled>()) { /* ... */ }

// Check if entity does not have a specific component
if (entity.WithoutComponent<Destroyed>()) { /* ... */ }

// Batch modify components in a single archetype migration
entity.AlterComponents(alter =>
{
    alter.Remove<Velocity>();
    alter.Add(new Immobile());
});

// Copy the entity; Shallow is the default, Linked also copies its linked subtree
var copy = entity.Copy();
var subtree = entity.Copy(CloneMode.Linked);
```

> **Note**: `AlterComponents` performs all additions and removals in a single archetype migration, which is more
> efficient than calling `AddComponent` / `RemoveComponent` separately (each triggers its own migration).

> **Note**: `GetComponent`, `WithComponent` and `WithoutComponent` read the world as it is committed. A component that
> was added in the current System is not visible through them until the next commit point.

## Component Hooks

Hooks let you observe per-component-type lifecycle events and react without polling. Register a hook on the `App`,
then the framework invokes it synchronously while the recorded commands are applied at a commit point.

| Hook | Fires when | Triggered by |
|---|---|---|
| `OnAdd` | A component becomes present on an entity | `AddComponent` / `AddComponents` / `CreateEntityWithComponent` / `CreateEntityWithComponents` / `CopyEntity`, and the added part of `AlterComponents` |
| `OnReplace` | An existing component value is overwritten in place | `ReplaceComponent`; a System `out` component parameter after `Execute` |
| `OnRemove` | A component leaves an entity | `RemoveComponent` / `RemoveComponents` / `RemoveComponentsTuple` / `RemoveEntity` (`Despawn`), and the removed part of `AlterComponents` |

`OnRemove` fires **before** the component is detached, so the entity and all of its other components are still
readable inside the hook.

### Registering a Hook

```csharp
app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in Health health) =>
{
    Console.WriteLine($"Entity {context.Entity} gained Health = {health.Value}");
});
```

The hook receives the value the event observes — the new value for `OnAdd`, the current value for `OnReplace`, and the
old value for `OnRemove`:

```csharp
public delegate void ComponentHook<T>(ref HookContext context, in T component)
    where T : unmanaged, IComponent;
```

Registering again for the same type and kind throws `InvalidOperationException`. Components with no registered hook
stay on the fast path: the framework does not even read the previous value for them.

### Reading the Entity Inside a Hook

`HookContext` exposes the entity, the `Commands` that triggered the hook, and the `App`:

- `context.Entity` — the entity the event belongs to.
- `context.Commands` — the hook queue. Structural changes enqueued here are applied right after the command that
  triggered the hook, before the next recorded command, so the commands that follow observe them.
- `context.App` — for reading resources.
- `context.Has<T>()`, `context.TryGet<T>(out var component)`, `context.GetComponent<T>()` — read the entity the event
  belongs to. There are `EntityRef` overloads (`Has<T>(EntityRef)`, `TryGet<T>(EntityRef, out var component)`,
  `Get<T>(EntityRef)`) for any other entity. `GetComponent`/`Get` returns a bare `ref`; writing through it does
  **not** trigger a hook.
- `context.TryGetPrevious<T>(out var previous)` — the value a component had before it was overwritten. Only meaningful
  for `OnReplace`; it returns `false` for every other kind.

```csharp
app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in Health health) =>
{
    if (context.TryGetPrevious<Health>(out var previous) && health.Value < previous.Value)
    {
        Console.WriteLine($"Entity {context.Entity} lost {previous.Value - health.Value} health");
    }
});
```

### Replacing a Value

`AddComponent` refuses a component the entity already has, because the archetype rejects a duplicate type id. To
overwrite an existing value and notify `OnReplace`, use `ReplaceComponent`. It never upserts: an invalid, removed, or
component-less entity does nothing and fires nothing.

```csharp
commands.ReplaceComponent(entity, new Health { Value = 10.0f });

// Or through the entity
entity.ReplaceComponent(new Health { Value = 10.0f });
```

A System that writes a component through an `out` parameter gets the same treatment: the generated loop snapshots the
value before `Execute` and calls the `OnReplace` hook after it returns. Ordinary `ref` and `Span<T>` parameters mutate
in place and do **not** fire a hook.

> **Threading**: hooks fire while recorded commands are applied, which is always single-threaded. A hook runs on the
> committing thread and sees the real world, so its order is deterministic. The scheduler's conflict analysis still
> does not see hook side effects, so a hook should only touch data the System it belongs to already declares.
>
> Archetypes are created while commands are applied, too, so archetype creation and the migration-edge cache are
> single-threaded as well. `GetOrCreateArchetypeWithTuple<T>` / `GetOrCreateArchetypeWithBundle<T>` create
> archetypes, so they must not be called from a parallel System; a running System only reads archetypes for its
> query.

> **Termination**: a hook's structural changes are drained in FIFO order right after the current command and never
> recurse on the stack, so there is no depth cap. Two hooks that keep triggering each other will hang the commit
> instead of overflowing the stack — hooks must terminate.

## Relationships

`ChildOf` is the built-in parent-child relationship. It lives on the child and holds the parent reference, and it is
the only authoritative copy of the hierarchy:

```csharp
using lychee.components;

var parent = commands.CreateEntity();
var child = commands.CreateEntity();

// The child points at the parent.
child.AddComponent(new ChildOf(parent.Ref));
commands.Commit();
```

For every entity that has children, the framework maintains a read-only reverse collection,
`RelationshipTarget<ChildOf>`, on the parent. User code never creates or edits it:

```csharp
// After the commit above the parent has the collection; read it in insertion order.
if (parent.WithComponent<RelationshipTarget<ChildOf>>())
{
    ReadOnlySpan<EntityRef> children = parent.GetComponent<RelationshipTarget<ChildOf>>().Entities;
}
```

The collection is created when the first child attaches and removed automatically when the last one detaches, so
"having `RelationshipTarget<ChildOf>`" means "having at least one child". Like every other structural change, it
becomes visible only at the next commit point.

### Changing the Parent

`ChildOf.Parent` is read-only. Change a child's parent by overwriting the component through `ReplaceComponent`, or by
writing it through an `out ChildOf` system parameter:

```csharp
// Reparent through ReplaceComponent.
child.ReplaceComponent(new ChildOf(newParent.Ref));

// Or from a System; the generated loop reports the replacement after Execute returns.
[AutoImplSystem]
partial class ReparentSystem
{
    private static void Execute(out ChildOf child, [Resource] ReparentTarget target)
    {
        child = new ChildOf(target.Parent);
    }
}
```

Both paths fire `OnReplace`, which detaches the child from its old parent and attaches it to the new one. Writing the
field through a plain `ref ChildOf` does **not** trigger the hooks and leaves the collections stale, so don't do it.

A `ChildOf` that points at a dead entity, at the child itself, or that would create a cycle is rejected when it is
applied: the `ChildOf` is removed and a warning is logged.

### Cascading Despawn

Removing an entity also removes its whole `Children` subtree, iteratively and without recursion on the stack:

```csharp
parent.Despawn();
commands.Commit();

// parent, its children, and their children are all gone.
```

Do not want that? Detach the child first; a child with no `ChildOf` survives its parent:

```csharp
child.RemoveComponent<ChildOf>();
parent.Despawn();
commands.Commit();
```

### Copying and Cloning

`Copy` reads the source's state when the command is applied. `CloneMode.Shallow` copies the entity alone — a copied
`ChildOf` still points at the original parent, so the copy becomes a sibling of the source, and the copy's own
`Children` starts empty. `CloneMode.Linked` recursively copies the whole subtree of every relationship whose target is
marked linked, repointing each copied `ChildOf` at the copied parent:

```csharp
var sibling = entity.Copy();                 // Shallow, the default
var subtree = entity.Copy(CloneMode.Linked); // The entity and its descendants
```

> **Native memory is not deep-copied.** A component that owns unmanaged memory (such as the framework's own
> `RelationshipTarget<TRelationship>`, which holds a `NativeList<EntityRef>`) cannot be safely `memcpy`'d. The
> framework writes relationship targets as `default` and lets the relationship hooks rebuild them, but it does not
> detect the pattern in user-defined components: a custom component that holds a `NativeList` and gets copied will
> alias the source's buffer.

## Resource System

Resources are globally singleton data managed through `ResourcePool`:

### Adding Resources

```csharp
// Reference type resource
app.AddResource(new GameState());

// Unmanaged type resource (stored in native memory)
app.AddResourceStruct(new Time { DeltaTime = 0.016f });
```

### Accessing Resources in Systems

```csharp
using lychee.attributes;

[AutoImplSystem]
partial class TimeSystem
{
    private static void Execute([Resource] ref Time time)
    {
        // Update time
        time.TotalTime += time.DeltaTime;
    }
}
```

**`[Resource]` attribute parameters**:

- `readOnly` (bool, default `false`) - For class-type resources only. When `true`, the resource is treated as read-only,
  allowing safe concurrent access in multi-threaded execution mode
- `acquireOnExec` (bool, default `false`) - When `true`, the resource is acquired (fetched) from the pool on each `Execute`
  call rather than cached during system initialization. Useful for resources that may be added after system initialization,
  such as those created by other systems

```csharp
[AutoImplSystem]
partial class MySystem
{
    // Acquire on each Execute since the resource may not exist at init time
    private static void Execute([Resource(acquireOnExec: true)] ref MyLateResource res)
    {
        // ...
    }
}
```

The event system provides a thread-safe way to communicate between Systems using double buffering.
Events written while a batch is readable stay invisible until the queue is published, which happens according to the
`EventPublishTiming` chosen when the event was registered.

### Adding Events

Events are registered as resources in the App. An `EventDescriptor` controls when events become readable and whether a batch is kept until it has been read:

```csharp
// Define event data type
public struct DamageEvent
{
    public Entity Target;
    public int Amount;
}

// Register event. UpdateEnd, the default, publishes at the end of the update so readers always see a whole
// update worth of events. ScheduleEnd publishes at the end of every schedule, and CommitPoint at every commit
// point, so later systems of the same update can react to what earlier ones did.
app.AddEvent<DamageEvent>();
app.AddEvent<HitEvent>(new EventDescriptor { Timing = EventPublishTiming.ScheduleEnd });
app.AddEvent<MoveEvent>(new EventDescriptor { Timing = EventPublishTiming.CommitPoint });

// ExchangeOnlyRead keeps a published batch around until a reader actually reads it, so a reader that does not
// run every update cannot miss it; the next batch waits in the back buffer until then.
app.AddEvent<QuestEvent>(new EventDescriptor { ExchangeOnlyRead = true });
```

### Sending Events

Declare an `EventWriter<T>` parameter. The writer resolves `Event<T>` from the resource pool for you:

```csharp
[AutoImplSystem]
partial class CombatSystem
{
    private static void Execute(EventWriter<DamageEvent> damageWriter, ref Health health)
    {
        if (health.Value <= 0)
        {
            damageWriter.Send(new DamageEvent { Target = entity, Amount = 10 });
        }
    }
}
```

### Reading Events

Declare an `EventReader<T>` parameter. Its cursor belongs to the reader, so every System reads every event of a batch
once no matter how many Systems read it:

```csharp
[AutoImplSystem]
partial class DamageDisplaySystem
{
    private static void Execute(EventReader<DamageEvent> damageReader)
    {
        while (damageReader.Read(out var ev))
        {
            Console.WriteLine($"Entity {ev.Target} took {ev.Amount} damage");
        }
    }
}
```

**Note**: The reader is created per execution, so its cursor starts at the beginning of the batch every time.
A multi-threaded System cannot declare one: every worker would share the cursor, which is a compile error.

## State System

The State system provides a finite state machine pattern for managing game states (e.g., game phases, UI screens).
Entities can be scoped to a specific state and are automatically despawned when the state changes.

### Registering a State

Use `App.AddState<T>()` to register a state. This creates a `State<T>` resource and automatically registers a cleanup
system that removes state-scoped entities when the state transitions:

```csharp
using lychee;

// Typically an enum
enum GamePhase { Menu, Playing, Paused, GameOver }

// Register state with initial value
var phase = app.AddState(GamePhase.Menu);
```

### Transitioning States

Call `State<T>.Set()` to transition to a new state. If the new value equals the current value, no transition occurs:

```csharp
[AutoImplSystem]
partial class PauseSystem
{
    private static void Execute([Resource] State<GamePhase> phase, Commands commands)
    {
        if (/* pause key pressed */)
        {
            phase.Set(GamePhase.Paused);
        }
    }
}
```

### Scoping Entities to a State

Use the `StateScoped<T>` component to bind an entity's lifetime to a specific state value. When the state changes
away from the scoped value, the entity is automatically despawned:

```csharp
[AutoImplSystem]
partial class MenuSetupSystem
{
    private static void Execute(Commands commands)
    {
        // This entity will be despawned when GamePhase transitions away from Menu
        commands.CreateEntityWithComponent(new StateScoped<GamePhase>
        {
            Value = GamePhase.Menu
        });
    }
}
```

**How it works**: `App.AddState<T>()` registers a `StateCleanupSystem<T>` in the `Last` schedule. Each frame, it
checks if the state has changed and removes all entities whose `StateScoped<T>.Value` does not match the current state.

## Scheduling System

### Schedules Provided by BasicGamePlugin

`BasicGamePlugin` provides standard game loop schedules:

| Schedule             | Description                                         |
|----------------------|-----------------------------------------------------|
| `First`              | Built-in. First call each frame                     |
| `StartUp`            | Execute once at startup                             |
| `FixedUpdate`        | Fixed interval update (default 20ms)                |
| `Update`             | Regular update                                      |
| `PostUpdate`         | Post-processing update                              |
| `Render`             | Render update                                       |
| `RenderTransparency` | Transparent rendering                               |
| `RenderUI`           | UI rendering                                        |
| `Last`               | Built-in. Last call each frame (state cleanup runs) |

### System Ordering and Parallel Execution

Systems run in layers. One layer is executed to completion before the next one starts, and the systems inside a
layer run in parallel. Two things decide what goes into which layer:

1. **The constraints you declare** — they are the only source of order.
2. **Conflicts** — two systems conflict when they touch the same component and at least one of them writes it.
   Conflicting systems are never put in the same layer.

Everything else is packed greedily, so systems that do not conflict share a layer. Declaration order is **not** a
constraint: it only breaks ties inside a layer, so the same configuration always produces the same order.

Declaring a constraint:

| Constraint | Declare it with |
|---|---|
| One system after another | `schedule.AddSystem(new SysB(), new SystemDescriptor { AddAfter = sysA })` |
| A group after another group | `schedule.AddSystems([new SysA()], [new SysB()])` — every member of group *k* runs after every member of group *k-1* |
| A whole set after another set | `app.ConfigureSetOrder(Stage.Physics, Order.Before, Stage.Render)` — see [System Sets](#system-sets) |

```csharp
// WritePositionSystem writes Position, ReadPositionSystem reads it — declared W, R, W, R.
schedule.AddSystem<WritePositionSystem>();
schedule.AddSystem<ReadPositionSystem>();
schedule.AddSystem<WritePositionSystem>();
schedule.AddSystem<ReadPositionSystem>();
schedule.Build();

// The two readers share a layer even though a writer was declared between them:
//   layer 1: the first writer
//   layer 2: both readers
//   layer 3: the second writer
```

**Which of two conflicting systems runs first is decided by the schedule when you have not decided it.** In the
example above the second reader runs in layer 2, *before* the writer that was declared before it, so it reads the
value of the first writer. Reading the layers back is the way to see this:

```csharp
foreach (var node in schedule.ExecutionGraph.AsList())
{
    Console.WriteLine($"{node.Data.System.GetType().Name} -> layer {node.Group}");
}
```

When the order matters, declare it. Either constrain the pair, or declare the writer before the readers:

```csharp
// R runs after W, so it reads what W wrote.
var writer = schedule.AddSystem<WritePositionSystem>();
schedule.AddSystem(new ReadPositionSystem(), new SystemDescriptor { AddAfter = writer });

// Both readers run after W and in parallel with each other.
var secondWriter = schedule.AddSystem<WritePositionSystem>();
schedule.AddSystem(new ReadPositionSystem(), new SystemDescriptor { AddAfter = secondWriter });
schedule.AddSystem(new ReadPositionSystem(), new SystemDescriptor { AddAfter = secondWriter });
```

> A reader declared *before* a writer and a reader declared *after* it do different work: the first sees the value
> from before the write, the second sees the value after it. That is exactly why their order cannot be left to the
> schedule once it stops being declaration order — state it with a constraint.

A group of the array form may give any of its members a descriptor, by writing that member as a system/descriptor
pair. It is the only way a system added through `AddSystems` can join a set, pick up an `AddAfter` dependency or get
a thread count, since a member written on its own is added with the default descriptor:

```csharp
// The group lists every member as a pair once any of them needs a descriptor.
schedule.AddSystems(
    [(new SysA(), null), (new SysB(), new SystemDescriptor { Sets = [Stage.Physics] })]);
```

The tuple form of `AddSystems` describes the same grouping in types instead of instances: each element of the tuple
is a group, and a nested tuple is a single parallel group. It cannot carry descriptors, so use the array form when
a system needs one.

```csharp
// SysA runs first, then SysB and SysC run in parallel (if compatible), then SysD runs.
schedule.AddSystems<(SysA, (SysB, SysC), SysD)>();
```

The systems of the tuple form are constructed for you, so they need a default constructor; when the instances
already exist, use the array form above.

### System Sets

A set groups systems so that you can constrain a whole set at once. Sets are backed by an enum — one enum per
classification dimension:

```csharp
enum Stage   { PreUpdate, Update, PostUpdate }
enum Feature { Input, Physics, Presentation }
```

Register each enum once, then put systems in sets through their descriptor:

```csharp
app.AddSystemSet<Stage>();
app.AddSystemSet<Feature>();

schedule.AddSystem<MoveSystem>(new SystemDescriptor { Sets = [Stage.Update, Feature.Physics] });
schedule.AddSystem<PollInputSystem>(new SystemDescriptor { Sets = [Stage.Update, Feature.Input] });
```

**Ordering**: every system in the first set (and in its child sets) runs before every system in the second one:

```csharp
app.ConfigureSetOrder(Feature.Input, Order.Before, Feature.Physics);
```

**Nesting**: a child set is a member of its parent *and* inherits the parent's predicate. A set has at most one
parent:

```csharp
app.ConfigureSetInSet(Stage.Update, Feature.Physics);
```

**Predicates**: a predicate on a set applies to every system in it, and to every system in its child sets:

```csharp
app.ConfigureSetPredicate(Stage.PostUpdate, pool => pool.Get<FrameStats>().HasWork);
```

Notes:

- **Constraints may be declared at any time** — before or after the systems they affect. They are recorded, not
  applied, so `ConfigureSetOrder` after `AddSystem` works exactly like setting it up front. See
  [When the Schedule Is Resolved](#when-the-schedule-is-resolved).
- **Use several enums instead of nesting a set under several parents.** A system may belong to as many sets as you
  like (`Sets = [Stage.Update, Feature.Physics]`), and orthogonal dimensions are better expressed as orthogonal
  enums than as a set with multiple parents.
- A set of an enum type that was never registered is an error, reported when the schedule is resolved.

### When the Schedule Is Resolved

Adding systems and configuring sets only *records* what you asked for. The execution graph is resolved from those
records in one pass, by `Build()`:

- Automatically, the first time the schedule executes (and again whenever something changed).
- Explicitly, whenever you call `schedule.Build()`. Doing this right after configuring surfaces ordering problems
  at configuration time instead of mid-frame.

```csharp
schedule.AddSystem<MovementSystem>(new SystemDescriptor { Sets = [Stage.Update] });

schedule.Build();          // Optional. Reports a bad configuration now rather than on the first frame.
Console.WriteLine(schedule.IsBuilt);   // true
```

`Build()` can throw `InvalidOperationException` — for a set type that was never registered, for a cycle in the set
ordering or nesting, or for a conflicting pair when ambiguity detection is set to `Error` — and
`InvalidGraphException` when the declared constraints cannot all be satisfied. `ExecutionGraph` is empty until the
schedule is built; `IsBuilt` tells you whether it currently matches the recorded configuration.
`schedule.ClearSystems()` discards the systems and the resolved graph.

### Ambiguity Detection

When two systems conflict and nothing constrains their order, the schedule separates them on its own. Which one runs
first is then the schedule's decision and not yours, so it is reported:

```csharp
var schedule = new DefaultSchedule(app, "Update")
{
    AmbiguityDetection = BasicSchedule.AmbiguityDetectionEnum.Warn   // the default
};

schedule.AddSystem<WritePositionSystem>();
schedule.AddSystem<ReadPositionSystem>();
schedule.Build();

foreach (var (a, b) in schedule.Ambiguities)
{
    Console.WriteLine($"{a.GetType().Name} and {b.GetType().Name} conflict without an ordering constraint");
}
```

| `AmbiguityDetection` | Behaviour |
|---|---|
| `Ignore` | Nothing is checked; `Ambiguities` stays empty |
| `Warn` (default) | Every such pair is logged and collected in `Ambiguities` |
| `Error` | `Build()` throws as soon as it finds one |

The setting is per schedule, so a schedule that has to be strict (a physics step) and one that does not (startup
wiring) can differ. The fix for a reported pair is to constrain it: give one of the systems an ordering constraint,
or declare the writer before the readers.

### Compatibility Notes

The scheduling resolver was rewritten; three behaviours are deliberately different from earlier versions.

- **Groups from `AddSystems` are ordered more strictly.** Every member of group *k* runs after every member of group
  *k-1*, where it used to depend on the first member of group *k-1* only. Systems that accidentally ran in parallel
  before are now ordered, which matches what this section always documented.
- **`ExecutionGraph` is a result, not a running record.** It used to be filled in as systems were added; it is now
  empty until the schedule is resolved. Read it after `Build()` or after the first execution, and use `IsBuilt` to
  know whether it is up to date.
- **A conflicting pair's order no longer follows declaration order.** Conflicting systems are guaranteed different
  layers, but *which* of the two comes first is decided by the packing when no constraint covers the pair. To fix
  the order — and with it which write each reader observes — declare a constraint.
- **`AddSystems` no longer takes a leading `addAfter`.** Ordering a whole group after one system is now expressed on
  that system's descriptor, and a group member may carry a descriptor by being written as a system/descriptor pair.
  Calling it as `AddSystems(afterSystem, [..], [..])` no longer compiles; write the dependency on the descriptor of
  the group's members instead.

### Schedule Configuration

`DefaultSchedule` provides a basic System scheduler that offers simple organization of how Systems are executed; Systems
can be executed in single/multi-threaded mode and switched at any time.

```csharp
using lychee;

var schedule = new DefaultSchedule(app,
    BasicSchedule.ExecutionModeEnum.MultiThread,  // Multi-threaded execution
    BasicSchedule.CommitPointEnum.Synchronization // Commit at synchronization points
);

app.AddSchedule(schedule, "CustomSchedule");
```

**Execution modes**:

- `SingleThread` - Execute all Systems sequentially
- `MultiThread` - Execute independent Systems in parallel

**Commit points**:

- `Synchronization` - Commit after each synchronization point
- `ScheduleEnd` - Commit once when all Systems under the current Schedule have finished execution

## Plugin System

Create custom plugins to organize functionality:

```csharp
using lychee;
using lychee.interfaces;

public class MyPlugin : IPlugin
{
    public void Install(App app)
    {
        // Create and add schedule
        var schedule = new DefaultSchedule(app);
        app.AddSchedule(schedule, "MySchedule");

        // Add resources
        app.AddResource(new MyConfig());
    }
}

// Use plugin
app.InstallPlugin<MyPlugin>();
```

## Complete Example

```csharp
using lychee;
using lychee.attributes;
using lychee.components;
using lychee.interfaces;
using lychee_game;

// Define components
[Component]
struct Position
{
    public float X, Y;
}

[Component]
struct Velocity
{
    public float X, Y;
}

struct Movement : IComponentBundle
{
    public Velocity Velocity;
    public Position Position;
}

// Define Systems
[AutoImplSystem]
partial class InitSystem
{
    private static void Execute(Commands commands)
    {
        for (var i = 0; i < 1000; i++)
        {
            var entity = commands.CreateEntity();
            entity.AddComponents(new Movement
            {
                Velocity = new() { X = 1.0f, Y = 0.5f },
                Position = new()
            });
        }
    }
}

[AutoImplSystem]
partial class MovementSystem
{
    private static void Execute(ref Position pos, in Velocity vel)
    {
        pos.X += vel.X;
        pos.Y += vel.Y;
    }
}

// Main program
public static class Program
{
    public static void Main()
    {
        using var app = new App();

        var gamePlugin = app.InstallPlugin<BasicGamePlugin>();

        gamePlugin.StartUp.AddSystem<InitSystem>();
        gamePlugin.Update.AddSystem<MovementSystem>();

        app.Update();
    }
}
```

## Hive

`Hive<T>` (`lychee.collections`) is a block-based sequence container for `unmanaged` element types, modelled after the
upcoming C++26 `std::hive`. It targets workloads where elements are inserted and removed continuously while external
code keeps long-lived references to them — particles, projectiles, subscriptions, network connections, and similar
frequently created and destroyed objects.

### Why use it

| Property | `Hive<T>` | `NativeList<T>` | `SparseMap<T>` |
|---|---|---|---|
| Element address stays stable across insert / remove | ✅ | ❌ (grows by moving) | ⚠️ (removals swap elements) |
| Removal moves other elements | ❌ | ✅ | ✅ |
| Random access | ❌ | ✅ | ✅ (by key) |
| Extra metadata per element | 1 skipfield slot (2 bytes) | none | key + index |
| Iteration order | physical block order | insertion order | dense order |

Elements live inside fixed native blocks that are never relocated, so a `T*` obtained from `GetPointer` keeps
pointing at the same element no matter how many insertions, removals, `Reserve` or `TrimCapacity()` calls happen.
Removals leave a hole behind instead of moving other elements, and later insertions reuse those holes.

### Basic usage

```csharp
using lychee.collections;

public struct Projectile
{
    public int Damage;
    public float TimeToLive;
}

var projectileHive = new Hive<Projectile>();

var handle = projectileHive.Add(new Projectile { Damage = 10, TimeToLive = 2.0f });

// The raw pointer is stable until the element is removed.
unsafe
{
    Projectile* projectile = projectileHive.GetPointer(handle);
    projectile->TimeToLive -= deltaTime;
}

// ... later, after many insertions and removals ...
if (projectileHive.IsAlive(handle))
{
    ref var projectile = ref projectileHive.GetReference(handle);
    projectile.Damage *= 2;
}
```

`ref` accesses from `GetReference` are only valid for immediate use. Store a `T*` (long-lived access) or a
`HiveHandle` (survives even `Splice`-style block reuse) when the reference has to outlive the call.

### Removing while iterating

`Enumerator` exposes `RemoveCurrent()`, which corresponds to the C++ idiom `it = hive.erase(it)`:

```csharp
var enumerator = projectileHive.GetEnumerator();
while (enumerator.MoveNext())
{
    if (enumerator.Current.TimeToLive <= 0f)
    {
        enumerator.RemoveCurrent();
    }
}
```

`RemoveCurrent()` removes the element the enumerator sits on; the following `MoveNext()` lands on the element after
the removed one. Use `Remove(handle)` when the element is already known.

### Capacity semantics

- `Capacity` is the sum of every block capacity, including reserved blocks, unused tail space and holes. It is
  **not** the number of elements the hive can hold contiguously.
- `Reserve(count)` only grows `Capacity`. It never invalidates pointers.
- `TrimCapacity()` / `TrimCapacity(count)` only release reserved blocks. They never invalidate pointers.
- `Clear()` drops every element but keeps the blocks, so `Capacity` is unchanged and no pointer is invalidated.
- `BlockCapacityLimits` controls the smallest and largest block; `BlockCapacityDefaultLimits` and
  `BlockCapacityHardLimits` expose the defaults and the hard bounds.

### Important limitations

- Iteration order is the physical order of elements inside the blocks and is unrelated to insertion order. The
  container is not randomly accessible.
- A `HiveHandle` is permanently dead once its block is emptied, even if the hive later recycles that block, because
  a recycled block receives a fresh identifier. A handle whose block is still active can, however, alias a newly
  inserted element if its slot is reused — the skipfield tracks slot occupancy, not element identity. Store your own
  identity inside the element when you need to distinguish elements across removals.
- `ShrinkToFit`, `Reshape`, `Splice`, `Sort`, `Unique`, reverse iteration and block views are not implemented yet.

## Performance Benchmarks

`lychee.Benchmarks` is a [BenchmarkDotNet](https://benchmarkdotnet.org/) project that measures `Hive<T>` against
`List<T>`.

### Running

```bash
# Every benchmark (takes several minutes)
dotnet run --project lychee.Benchmarks -c Release -- --filter '*'

# A single class
dotnet run --project lychee.Benchmarks -c Release -- --filter '*AddBenchmarks*'

# List everything without running it
dotnet run --project lychee.Benchmarks -c Release -- --list flat
```

**Always pass `-c Release`.** Reports land in `BenchmarkDotNet.Artifacts/`, which is git-ignored.

The project is part of `lychee.sln` but **excluded from the solution build** (its `Build.0` entries were removed),
so `dotnet build lychee.sln` and any CI that builds the solution never compile it. Build and run it explicitly
with the commands above.

### What is measured

| Class | Question it answers |
|---|---|
| `AddBenchmarks` | block based growth vs array based growth; the cost of the free list fast path after a mass removal |
| `RemoveBenchmarks` | O(1) hole based removal vs `List.RemoveAt` shifting removal |
| `IterationBenchmarks` | jump based iteration vs contiguous iteration, and how much hole density costs (`HoleRatio` 0 / 0.25 / 0.5 / 0.9) |
| `MutationDuringIterationBenchmarks` | `RemoveCurrent()` while walking vs `List.RemoveAll` |
| `CapacityBenchmarks` | `Reserve` / `Clear` / `Clear` followed by a refill |
| `ReferenceBenchmarks` | reading through a stable `T*`, and the cost of recovering a handle with `GetHandle` |

### How to read the results

- **`RemoveBenchmarks` is not a same-semantics comparison.** `Hive.Remove` leaves a hole and moves nothing, while
  `List.RemoveAt` memmoves the tail on every removal. Those numbers describe two different cost models, not two
  implementations of the same operation.
- `List<T>` cannot express "stable reference" at all, so `ReferenceBenchmarks` compares against
  `CollectionsMarshal.AsSpan`, the closest equivalent the BCL offers.
- The mutating benchmarks need a fresh container per invocation, so they run with `InvocationCount=1`. That adds a
  fixed per-invocation overhead of roughly 5–10 µs: trust the `Count = 100000` rows, and read the `Count = 1000`
  rows for trends only.
- `[MemoryDiagnoser]` reports **managed** allocations only. `NativeMemory.AlignedAlloc` is invisible to it — watch
  `Capacity` and `BlockCount` when reasoning about native memory.

## System Requirements

- .NET 10.0
- C# 14
