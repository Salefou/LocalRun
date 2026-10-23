using LrSample.Pricing.Models;
using LrSample.Primitives;
using LrSample.Reports;

BuildInfo.Print(typeof(Program), typeof(DailyReport), typeof(Quote), typeof(BuildInfo));
Console.WriteLine($"  {DailyReport.Line(new Quote("EURUSD", 99.5m, 100.5m))}");
