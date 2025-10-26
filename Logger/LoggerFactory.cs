namespace LocalRun.Logger;

public static class LoggerFactory
{
    public static ILoggerShishe<T> Create<T>()
    {
        return new ConsoleLogger<T>();
    }
}
