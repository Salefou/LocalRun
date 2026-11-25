namespace LocalRun.Core.Modules;

using System;

/// <summary>
/// The way past a failure (a command to run, an option to add), carried by the exception that reports it, whatever its
/// type. The CLI prints it on a line of its own, marked (<see cref="Logger.LogEvents.Hint"/>), and in the JSON
/// <c>error.hint</c>: apart from the message, so that it is easy to find and to act on.
/// </summary>
public static class ErrorHint
{
    private const string Key = "LocalRun.Hint";

    /// <summary>Attaches a hint to an exception about to be thrown; a null hint attaches nothing.</summary>
    public static T With<T>(T exception, string? hint) where T : Exception
    {
        if (!string.IsNullOrEmpty(hint))
            exception.Data[Key] = hint;
        return exception;
    }

    /// <summary>The hint attached to an exception, or null.</summary>
    public static string? Of(Exception exception) => exception.Data[Key] as string;
}
