using LrSample.Logging;
using LrSample.Pricing;
using LrSample.Pricing.Models;
using LrSample.Primitives;

BuildInfo.Print(typeof(Program), typeof(Pricer), typeof(Quote), typeof(SampleLog), typeof(BuildInfo));

// Long-running: serves quotes until stopped (Ctrl+C), like a real service under `lrun up`
return await SampleService.RunAsync("OrderGateway", 5101, () =>
{
    var quote = Pricer.Price("EURUSD");
    return $"{quote.Instrument} mid {quote.Mid}, {Pricer.Status()}";
});
