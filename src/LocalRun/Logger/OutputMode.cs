namespace LocalRun.Logger;

using System;
using System.Linq;

/// <summary>
/// Whether this run writes machine output (<c>--json</c>): one JSON document on stdout, the logs on stderr in their plain
/// rendering. Set by <c>Main</c> before anything logs, since <see cref="Log"/> reads it once, when it creates its factory;
/// kept apart from <see cref="Log"/> so that setting it doesn't create the factory too early.
/// </summary>
public static class OutputMode
{
    public const string JsonFlag = "--json";

    public static bool Json { get; set; }

    /// <summary>Whether a command line asks for JSON, wherever the flag is: a value never starts with <c>--</c>.</summary>
    public static bool AsksForJson(string[] args) => args.Contains(JsonFlag, StringComparer.Ordinal);
}
