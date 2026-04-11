![image](https://github.com/ymyh/lychee/blob/main/logo.png)

# LYCHEE

A simple archetype-based ECS (Entity-Component-System) framework for .NET 10.0 / C# 14.

## Features

- **Cache-friendly archetype storage** - Components are grouped by archetype for improved data locality, but moving entities
  between archetypes (e.g., adding/removing components) incurs data copy overhead and is cache-unfriendly.
- **Automatic parallelism** - DAG-based System dependency analysis provides basic automatic identification of Systems that can
  execute in parallel
- **Source generation** - Use the `[AutoImplSystem]` Attribute to automatically generate System code
- **Deferred commands** - Entity modifications are batch-committed at synchronization points for concurrent safety
- **Flexible scheduling system** - Supports single-thread/multi-thread execution with configurable commit timing

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
using lychee.interfaces;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct Position : IComponent
{
    public float X;
    public float Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct Velocity : IComponent
{
    public float X;
    public float Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct Health : IComponent
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

The `[AutoImplSystem]` attribute accepts an optional `multiThreaded` parameter. When set to `true`, entity iteration
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
- `Commands` - Records deferred entity operations (creation, deletion, adding components, etc.)
- `Entity` - Typically passed by `ref` (i.e., `ref Entity entity`), provides access to the current entity being processed

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

- `CreateEntity()` - Create a new entity
- `RemoveEntity(Entity)` - Remove an entity
- `AddComponent<T>(ref Entity, in T)` or `entity.AddComponent<T>(in T)` - Add a component
- `RemoveComponent<T>(ref Entity)` or `entity.RemoveComponent<T>()` - Remove a component
- `AddComponents<T>(ref Entity, in T)` or `entity.AddComponents<T>(in T)` - Add a component bundle

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
```

> **Note**: `AlterComponents` performs all additions and removals in a single archetype migration, which is more
> efficient than calling `AddComponent` / `RemoveComponent` separately (each triggers its own migration).

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
Events sent in the current frame will be readable in the next update.

### Adding Events

Events are registered as resources in the App:

```csharp
// Define event data type
public struct DamageEvent
{
    public Entity Target;
    public int Amount;
}

// Register event
app.AddEvent<DamageEvent>();
```

### Sending Events

Use the `[Resource]` attribute to access the event in a System:

```csharp
[AutoImplSystem]
partial class CombatSystem
{
    private static void Execute([Resource] Event<DamageEvent> damageEvent, ref Health health)
    {
        if (health.Value <= 0)
        {
            damageEvent.SendEvent(new DamageEvent { Target = entity, Amount = 10 });
        }
    }
}
```

### Reading Events

Events sent in the previous frame can be read using `GetEnumerable()`:

```csharp
[AutoImplSystem]
partial class DamageDisplaySystem
{
    private static void Execute([Resource] Event<DamageEvent> damageEvent)
    {
        foreach (var ev in damageEvent.GetEnumerable())
        {
            Console.WriteLine($"Entity {ev.Target} took {ev.Amount} damage");
        }
    }
}
```

**Note**: Events are automatically exchanged at the beginning of each update called.

## Scheduling System

### Schedules Provided by BasicGamePlugin

`BasicGamePlugin` provides standard game loop schedules:

| Schedule             | Description                          |
|----------------------|--------------------------------------|
| `StartUp`            | Execute once at startup              |
| `First`              | First call each frame                |
| `FixedUpdate`        | Fixed interval update (default 20ms) |
| `Update`             | Regular update                       |
| `PostUpdate`         | Post-processing update               |
| `Render`             | Render update                        |
| `RenderTransparency` | Transparent rendering                |
| `RenderUI`           | UI rendering                         |
| `Last`               | Last call each frame                 |

### System Ordering and Parallel Execution

Use tuple syntax to control System execution order and parallelism:

```csharp
// InitSystem executes first
// MoveSystem and RotateSystem execute in parallel (if component access is compatible)
// RenderSystem executes last
gamePlugin.Update.AddSystems<(InitSystem, (MoveSystem, RotateSystem), RenderSystem)>();
```

**Parallelism rule**: Systems can execute in parallel if and only if they only read the same data (no write conflicts).

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
[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct Position : IComponent
{
    public float X, Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct Velocity : IComponent
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

## System Requirements

- .NET 10.0
- C# 14
