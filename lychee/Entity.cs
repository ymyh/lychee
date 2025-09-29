namespace lychee;

public struct Entity(int id, int generation)
{
    public int ID = id;

    internal int Generation = generation;
}

public struct EntityInfo(int archetypeId, int archetypeIdx)
{
    internal int ArchetypeId = archetypeId;

    internal int ArchetypeIdx = archetypeIdx;
}