using UnityEngine;

/// <summary>Who something belongs to. Species-level and shared, so a wild creature carries no bytes for it.</summary>
public enum CreatureFaction : byte
{
    /// <summary>Passive animals: deer, rabbits, birds. Afraid of predators and of players.</summary>
    Wildlife = 0,

    /// <summary>Hunts wildlife. Wolves, bears.</summary>
    // planned: no predator species exists yet; the row is what makes the table mean anything, and the wolf
    // is blocked on combat rather than on this. docs/design/2026-08-26-creature-residency.md section 16.
    Predator = 1,

    Player = 2,
}

/// <summary>How one faction reacts to another. Directed: a wolf hunts a deer, a deer fears a wolf.</summary>
public enum FactionRelation : byte
{
    Neutral = 0,

    /// <summary>The subject is a threat: run.</summary>
    Afraid = 1,

    /// <summary>The subject is prey or an enemy: close on it.</summary>
    // planned: consumed by predator behaviour, which waits on combat. Same doc, section 16.
    Hostile = 2,
}

/// <summary>
/// Editor authoring surface for the directed faction table. Runtime reads <see cref="FactionRelationsDto"/>.
/// </summary>
/// <remarks>
/// Authored as a flat list rather than a grid because Unity cannot serialize a 2D array, and because the
/// interesting cells are few - everything unlisted is <see cref="FactionRelation.Neutral"/>, which is the
/// right default: a new faction ignores everything until someone says otherwise.
/// </remarks>
[CreateAssetMenu(menuName = "Planet/Faction Relations", fileName = "FactionRelations")]
public sealed class FactionRelations : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("The faction doing the reacting.")]
        public CreatureFaction Observer;

        [Tooltip("The faction being reacted to.")]
        public CreatureFaction Subject;

        public FactionRelation Relation;
    }

    public Entry[] Entries = System.Array.Empty<Entry>();
}

/// <summary>
/// The directed faction table the runtime reads. Answers one question: how does a creature of faction A react
/// to something of faction B?
/// </summary>
/// <remarks>
/// This is the whole of "what is a threat", and it is deliberately NOT per species. Bryan's requirement that
/// deer ignore deer, rabbits and birds is the single Wildlife-to-Wildlife cell rather than a rule written per
/// species, so adding a species means picking a faction and nothing else.
/// </remarks>
public sealed record FactionRelationsDto(FactionRelation[] Table)
{
    public const int FactionCount = 3;

    public FactionRelation Of(CreatureFaction observer, CreatureFaction subject)
    {
        int index = (int)observer * FactionCount + (int)subject;
        return Table != null && (uint)index < (uint)Table.Length ? Table[index] : FactionRelation.Neutral;
    }

    public bool IsThreat(CreatureFaction observer, CreatureFaction subject) =>
        Of(observer, subject) == FactionRelation.Afraid;

    public static FactionRelationsDto From(FactionRelations src)
    {
        if (src?.Entries == null || src.Entries.Length == 0)
            return Default;

        var table = new FactionRelation[FactionCount * FactionCount];
        foreach (FactionRelations.Entry e in src.Entries)
        {
            int index = (int)e.Observer * FactionCount + (int)e.Subject;
            if ((uint)index < (uint)table.Length)
                table[index] = e.Relation;
        }
        return new FactionRelationsDto(table);
    }

    /// <summary>
    /// The table the world uses when no asset is authored. The player is a threat to wildlife by default,
    /// armed or not (Bryan, 2026-08-26) - deer bolt on sight, which is the simplest rule and the one that
    /// makes a friendly spell feel like it did something.
    /// </summary>
    public static FactionRelationsDto Default { get; } = Build();

    static FactionRelationsDto Build()
    {
        var table = new FactionRelation[FactionCount * FactionCount];
        Set(table, CreatureFaction.Wildlife, CreatureFaction.Predator, FactionRelation.Afraid);
        Set(table, CreatureFaction.Wildlife, CreatureFaction.Player, FactionRelation.Afraid);
        Set(table, CreatureFaction.Predator, CreatureFaction.Wildlife, FactionRelation.Hostile);
        Set(table, CreatureFaction.Predator, CreatureFaction.Player, FactionRelation.Hostile);
        // Everything else stays Neutral, which is what makes deer ignore deer, rabbits and birds.
        return new FactionRelationsDto(table);
    }

    static void Set(FactionRelation[] table, CreatureFaction observer, CreatureFaction subject,
        FactionRelation relation) =>
        table[(int)observer * FactionCount + (int)subject] = relation;
}
