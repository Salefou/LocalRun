namespace LocalRun.Options;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Thrown for malformed command lines; the CLI reports it with a usage exit code.
/// </summary>
public class UsageException(string message) : Exception(message);

/// <summary>
/// Options of a single command, as <c>--name [values...]</c>. Values run until the next <c>--option</c>.
/// Each reader consumes the option it asks for, so <see cref="EnsureAllConsumed"/> can reject unknown ones.
/// </summary>
public class CommandLineArgs
{
    private readonly Dictionary<string, List<string>> optionToValues = new(StringComparer.Ordinal);

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
                throw new UsageException($"Unexpected argument '{arg}'.");
            }
            else
            {
                currentValues.Add(arg);
            }
        }
    }

    public bool Flag(string option)
    {
        if (!optionToValues.Remove(option, out var values))
            return false;
        if (values.Count != 0)
            throw new UsageException($"Option '{option}' does not take a value.");
        return true;
    }

    public string? Value(string option)
    {
        if (!optionToValues.Remove(option, out var values))
            return null;
        if (values.Count != 1)
            throw new UsageException($"Option '{option}' takes exactly one value.");
        return values[0];
    }

    public List<string>? Values(string option)
    {
        if (!optionToValues.Remove(option, out var values))
            return null;
        if (values.Count == 0)
            throw new UsageException($"Option '{option}' takes at least one value.");
        return values;
    }

    public void EnsureAllConsumed()
    {
        if (optionToValues.Count != 0)
            throw new UsageException($"Unknown option(s): {string.Join(", ", optionToValues.Keys)}.");
    }
}
