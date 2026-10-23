namespace LrSample.Pricing;

using LrSample.Logging;
using LrSample.Pricing.Models;

public static class Pricer
{
    public static Quote Price(string instrument) => new(instrument, 99.5m, 100.5m);

    public static string Status() => $"pricer {SampleLog.Uptime(TimeSpan.FromMinutes(90))}";
}
