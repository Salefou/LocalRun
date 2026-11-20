namespace LocalRun.Options;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Thrown for malformed command lines; the CLI reports it with a usage exit code.
/// </summary>
public class UsageException(string message) : Exception(message);

/// <summary>
/// Arguments of a single command: positional arguments first (<c>schema config</c>), then options as
/// <c>--name [values...]</c>. Values run until the next <c>--option</c>. Each reader consumes what it asks for, so
/// <see cref="EnsureAllConsumed"/> can reject unknown options and stray arguments.
/// </summary>
public class CommandLineArgs
{
    private readonly Queue<string> arguments = new();
    private readonly Dictionary<string, List<string>> optionToValues = new(StringComparer.Ordinal);
    private readonly HashSet<string> asked = new(StringComparer.Ordinal);

    public CommandLineArgs(IEnumerable<string> args)
    {
        List<string>? currentValues = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--"))
            {
                if (optionToValues.ContainsKey(arg))
                    throw new UsageException($"Option '{arg}' is given more than once.");
                currentValues = [];
                optionToValues[arg] = currentValues;
            }
            else if (currentValues == null)
            {
                arguments.Enqueue(arg);
            }
            else
            {
                currentValues.Add(arg);
            }
        }
    }

    /// <summary>Every option a reader asked for, given or not: what the command accepts (checked against the help by tests).</summary>
    internal IReadOnlySet<string> Asked => asked;

    /// <summary>The next positional argument, or null when there is none left.</summary>
    public string? Argument() => arguments.TryDequeue(out var argument) ? argument : null;

    public bool Flag(string option)
    {
        asked.Add(option);
        if (!optionToValues.Remove(option, out var values))
            return false;
        if (values.Count != 0)
            throw new UsageException($"Option '{option}' does not take a value.");
        return true;
    }

    public string? Value(string option)
    {
        asked.Add(option);
        if (!optionToValues.Remove(option, out var values))
            return null;
        if (values.Count != 1)
            throw new UsageException($"Option '{option}' takes exactly one value.");
        return values[0];
    }

    public List<string>? Values(string option)
    {
        asked.Add(option);
        if (!optionToValues.Remove(option, out var values))
            return null;
        if (values.Count == 0)
            throw new UsageException($"Option '{option}' takes at least one value.");
        return values;
    }

    public void EnsureAllConsumed()
    {
        if (arguments.Count != 0)
            throw new UsageException($"Unexpected argument '{arguments.Peek()}'.");
        if (optionToValues.Count != 0)
            throw new UsageException($"Unknown option(s): {string.Join(", ", optionToValues.Keys)}.");
    }
}
