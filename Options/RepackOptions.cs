using LocalRun.Core.Model;

namespace LocalRun.Options;

public class RepackOptions
{
    public bool UseRemoteAsVersionReference { get; init; }

    public BumpOptions? BumpOptions { get; init; }
}
