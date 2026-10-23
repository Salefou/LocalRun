using LrSample.Logging;
using LrSample.Pricing;
using LrSample.Pricing.Models;
using LrSample.Primitives;

BuildInfo.Print(typeof(Program), typeof(Pricer), typeof(Quote), typeof(SampleLog), typeof(BuildInfo));
var quote = Pricer.Price("EURUSD");
Console.WriteLine($"  {quote.Instrument} mid {quote.Mid}, {Pricer.Status()}");
