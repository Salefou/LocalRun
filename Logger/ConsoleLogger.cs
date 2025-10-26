namespace LocalRun.Logger;

using System;

internal class ConsoleLogger<T> : ILoggerShishe<T>
{
    public void Info(string message)
    {
        Console.WriteLine(message);
    }

    public void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("[WRN]");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Write("[ERR]");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}

