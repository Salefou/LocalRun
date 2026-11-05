namespace LocalRun.Core.Modules;

using System;

/// <summary>
/// A <c>dotnet</c> command (restore, pack, clean, build) failed: the code or the project needs fixing, retrying won't help.
/// The CLI reports it with its own exit code.
/// </summary>
public sealed class DotnetFailedException(string message) : Exception(message);
