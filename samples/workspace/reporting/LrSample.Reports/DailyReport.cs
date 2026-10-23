namespace LrSample.Reports;

using LrSample.Orphan;
using LrSample.Pricing.Models;

public static class DailyReport
{
    public static string Line(Quote quote) => $"{quote.Instrument}: {Formatting.Money(quote.Mid)}";
}
