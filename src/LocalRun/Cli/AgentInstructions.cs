namespace LocalRun.Cli;

using System;
using System.IO;

/// <summary>
/// The section users paste into their agent's instructions (README "From an agent", kept equal by a test), printed by
/// <c>lrun help --agent</c> and carried by <c>help --json</c>. Its stamp names the release it was written for, so the agent
/// can tell when to read <c>lrun changelog --since</c> that version and rewrite it.
/// </summary>
internal static class AgentInstructions
{
    public static string Text(string releaseVersion)
    {
        using var stream = typeof(AgentInstructions).Assembly.GetManifestResourceStream("LocalRun.AgentInstructions.md")
            ?? throw new InvalidOperationException("AgentInstructions.md is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return $"{Stamp(releaseVersion)}\n{reader.ReadToEnd().Replace("\r\n", "\n")}";
    }

    public static string Stamp(string releaseVersion) => $"<!-- written for {LocalRunCli.CommandName} {releaseVersion} -->";
}
