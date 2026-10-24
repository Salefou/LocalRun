using LrSample.Pricing.Models;
using LrSample.Primitives;

BuildInfo.Print(typeof(Program), typeof(Quote), typeof(BuildInfo));

// Long-running: serves the current exposure until stopped (Ctrl+C), like a real service under `lrun up`
return await SampleService.RunAsync("RiskService", 5102, () =>
{
    var exposure = new Quote("EURUSD", 99.5m, 100.5m).Mid * 1_000_000;
    return $"exposure {exposure:N0}";
});
