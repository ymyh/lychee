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

    public static bool operator==(Entity a, Entity b)
    {
        return a.Equals(b);
    }

    public static bool operator!=(Entity a, Entity b)
    {
        return !a.Equals(b);
    }
}

public struct EntityInfo
{
    internal int ArchetypeId;

    internal EntityInfo(int archetypeId)
    {
        ArchetypeId = archetypeId;
    }
}
