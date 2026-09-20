namespace Ledger.ReadModel.Seeding;

/// <summary>Seeding configuration (section <c>Seed</c>). Defaults match the demo scale in spec §2.</summary>
public sealed record SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Populate the projection store on startup when it is empty.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>Delete existing projections before seeding. Off by default so startup re-runs are
    /// idempotent (spec §2).</summary>
    public bool Reset { get; set; }

    public int Accounts { get; set; } = 1000;

    public int Transactions { get; set; } = 20000;

    /// <summary>Fixed RNG seed: the same seed always produces the same projections (spec §2).</summary>
    public int RandomSeed { get; set; } = 20260101;

    /// <summary>Anchor for <c>posted_at</c>; transactions are spread backwards from this instant.</summary>
    public DateTimeOffset PostedAtAnchor { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}