namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// What a single processing run should do, beyond what the configuration already says.
/// </summary>
public sealed class RunOptions
{
    /// <summary>
    /// Gets a run that behaves exactly as configured.
    /// </summary>
    public static RunOptions Default { get; } = new();

    /// <summary>
    /// Gets a run that computes every change without writing anything, whatever the configuration
    /// says. Used by the "Run Dry Run Now" button.
    /// </summary>
    public static RunOptions DryRun { get; } = new() { ForceDryRun = true };

    /// <summary>
    /// Gets a value indicating whether the run must not write to Jellyfin.
    /// </summary>
    public bool ForceDryRun { get; init; }

    /// <summary>
    /// Gets a value indicating whether records for items that left the library are cleaned up.
    /// Only whole-library runs may do this; a run over a handful of items would delete the rest.
    /// </summary>
    public bool PruneMissingItems { get; init; } = true;
}
