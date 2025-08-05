namespace lychee;

public struct Entity(int id, int generation)
{
    public int ID { get; } = id;

    internal int Generation { get; set; } = generation;
}

public struct EntityInfo(int archetypeId, int archetypeIdx)
{
    internal int ArchetypeId { get; set; } = archetypeId;

    internal int ArchetypeIdx { get; set; } =  archetypeIdx;
}
