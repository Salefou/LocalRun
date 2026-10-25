using LocalRun.Core.Model;

namespace LocalRun.Options;

public class RepackOptions
{
    public required bool CleanBeforeRestore { get; init; }

    public required BumpOptions? BumpOptions { get; init; }
}
