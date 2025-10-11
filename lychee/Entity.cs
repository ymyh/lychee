namespace lychee;

public struct Entity
{
    public int ID { get; internal set; }

    internal int Generation;

    internal Entity(int id, int generation)
    {
        ID = id;
        Generation = generation;
    }
}

public struct EntityInfo
{
    internal int ArchetypeId;

    internal int ArchetypeIdx;

    internal EntityInfo(int archetypeId, int archetypeIdx)
    {
        ArchetypeId = archetypeId;
        ArchetypeIdx = archetypeIdx;
    }
}