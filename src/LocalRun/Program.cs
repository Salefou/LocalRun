namespace LocalRun;

using LocalRun.Cli;
using LocalRun.Logger;
using System;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Before anything logs: the logger factory reads it once
        OutputMode.Json = OutputMode.AsksForJson(args);
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
