using lychee.interfaces;

namespace lychee;

public struct Entity(Commands commands, Archetype archetype)
{
    internal Archetype Archetype = archetype;

    public EntityRef Ref { get; set; }

    internal EntityPos Pos;

    public int ID => Ref.ID;

    internal int Generation => Ref.Generation;

    public Entity(Commands commands, Archetype archetype, EntityRef entityRef, EntityPos pos) : this(commands, archetype)
    {
        Ref = entityRef;
        Pos = pos;
    }

    public void AddComponent<T>(in T component) where T : unmanaged, IComponent
    {
        commands.AddComponent(ref this, in component);
    }

    public void AddComponents<T>(in T components) where T : unmanaged, IComponentBundle
    {
        commands.AddComponents(ref this, in components);
    }

    public void RemoveComponent<T>() where T : unmanaged, IComponent
    {
        commands.RemoveComponent<T>(ref this);
    }

    public void RemoveComponents<T>() where T : unmanaged, IComponentBundle
    {
        commands.RemoveComponents<T>(ref this);
    }

    public void RemoveComponentsTuple<T>() where T : unmanaged
    {
        commands.RemoveComponentsTuple<T>(ref this);
    }

    public ref T GetComponent<T>() where T : unmanaged, IComponent
    {
        return ref commands.GetEntityComponent<T>(Archetype, Pos);
    }

    public bool WithComponent<T>() where T : unmanaged, IComponent
    {
        return commands.WithComponent<T>(ref this);
    }

    public bool WithoutComponent<T>() where T : unmanaged, IComponent
    {
        return commands.WithoutComponent<T>(ref this);
    }
}

public struct EntityRef : IEquatable<EntityRef>
{
    public int ID { get; }

    internal int Generation;

    internal EntityRef(int id, int generation)
    {
        ID = id;
        Generation = generation;
    }

    public bool Equals(EntityRef other)
    {
        return ID == other.ID && Generation == other.Generation;
    }

    public static bool operator ==(EntityRef a, EntityRef b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(EntityRef a, EntityRef b)
    {
        return !a.Equals(b);
    }

    public override bool Equals(object? obj)
    {
        return obj is EntityRef other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generation, ID);
    }
}

public struct EntityPos(int chunkIdx = 0, int idx = 0)
{
    internal readonly int ChunkIdx = chunkIdx;

    internal int Idx = idx;
}

public struct EntityInfo
{
    public readonly Archetype Archetype;

    internal EntityPos Pos;

    internal EntityInfo(Archetype archetype, EntityPos pos)
    {
        Archetype = archetype;
        Pos = pos;
    }
}
