namespace CacheCleanup.Core;

public enum TargetCategory { System, Browsers, Developer, Apps, Gaming, Custom }

public enum RiskLevel { Safe, Moderate, Careful }

public enum TargetKind { Files, RecycleBin }

/// <summary>
/// A declarative description of one cleanable location: where it lives, what files count,
/// and how risky it is to remove them. Names and descriptions live in the localization files
/// under <c>target.{Id}.name</c> / <c>target.{Id}.desc</c>.
/// </summary>
public sealed class CleanupTarget
{
    public required string Id { get; init; }
    public required TargetCategory Category { get; init; }
    public required string Glyph { get; init; }

    public TargetKind Kind { get; init; } = TargetKind.Files;
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public bool RequiresAdmin { get; init; }
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>When true the "skip recent files" setting applies (temp folders).</summary>
    public bool RespectsAgeFilter { get; init; }
    public bool Recursive { get; init; } = true;

    /// <summary>Directory patterns; may contain environment variables and <c>*</c> segments.</summary>
    public string[] RootPatterns { get; init; } = [];
    public string[] FilePatterns { get; init; } = ["*"];

    /// <summary>Display name override (custom folders); null means use the localized key.</summary>
    public string? DisplayName { get; init; }

    public string NameKey => $"target.{Id}.name";
    public string DescriptionKey => $"target.{Id}.desc";

    public IReadOnlyList<string> ResolveRoots()
        => RootPatterns
            .SelectMany(PathResolver.Expand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(PathGuard.IsAllowedRoot)
            .ToList();
}
