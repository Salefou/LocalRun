using LrSample.Pricing.Models;
using LrSample.Primitives;

BuildInfo.Print(typeof(Program), typeof(Quote), typeof(BuildInfo));
var exposure = new Quote("EURUSD", 99.5m, 100.5m).Mid * 1_000_000;
Console.WriteLine($"  exposure {exposure:N0}");
