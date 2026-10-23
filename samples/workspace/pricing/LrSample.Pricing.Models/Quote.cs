namespace LrSample.Pricing.Models;

public sealed record Quote(string Instrument, decimal Bid, decimal Ask)
{
    public decimal Mid => (Bid + Ask) / 2;
}
