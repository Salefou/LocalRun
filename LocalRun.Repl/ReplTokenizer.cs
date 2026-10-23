namespace LocalRun.Repl;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Splits a REPL line into arguments like a shell would: on whitespace, with double quotes
/// grouping a value that contains spaces (e.g. <c>--root "C:\My Repos"</c>).
/// </summary>
internal static class ReplTokenizer
{
    public static string[] Split(string line)
    {
        List<string> tokens = [];
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (inQuotes)
            throw new FormatException("Unterminated quote.");
        if (hasToken)
            tokens.Add(current.ToString());
        return [.. tokens];
    }
}
