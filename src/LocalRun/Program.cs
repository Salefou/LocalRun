namespace LocalRun;

using LocalRun.Cli;
using LocalRun.Logger;
using System;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Before anything logs: the logger factory reads them once
        OutputMode.Json = OutputMode.AsksForJson(args);
        Log.MinimumLevel = Log.ChooseLevel(args, () => Options.UserSettings.Load().LogLevel);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            return await LocalRunCli.RunAsync(args);
        }
        finally
        {
            Log.Shutdown();
        }
    }
}
