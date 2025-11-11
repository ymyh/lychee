namespace lychee;

public struct Entity : IEquatable<Entity>
{
    public int ID { get; internal set; }

    internal int Generation;

    internal Entity(int id, int generation)
    {
        ID = id;
        Generation = generation;
    }

    public bool Equals(Entity other)
    {
        return ID == other.ID && Generation == other.Generation;
    }

    public static bool operator ==(Entity a, Entity b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b)
    {
        return !a.Equals(b);
    }

    public override bool Equals(object? obj)
    {
        return obj is Entity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generation, ID);
    }
}

public struct EntityInfo
{
    internal int ArchetypeId;

    internal readonly int ChunkIdx;

    internal readonly int Idx;

    internal EntityInfo(int archetypeId, int chunkIdx, int idx)
    {
        ArchetypeId = archetypeId;
        ChunkIdx = chunkIdx;
        Idx = idx;
    }
}