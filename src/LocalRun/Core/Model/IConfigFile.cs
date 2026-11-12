namespace LocalRun.Core.Model;

/// <summary>
/// A file one command writes for the next (<c>localrun.config.json</c>, <c>repack.config.json</c>).
/// </summary>
public interface IConfigFile
{
    /// <summary>
    /// The one version this <c>lrun</c> writes and the highest it reads. Renaming or removing a field, or changing
    /// what one means, bumps it; adding one doesn't (unknown fields are ignored on read).
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Shape of the file; absent in files written before it existed, which read as 1.</summary>
    int SchemaVersion { get; }
}
