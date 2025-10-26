namespace LocalRun.Logger;

using System;

public interface ILoggerShishe<T>
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
