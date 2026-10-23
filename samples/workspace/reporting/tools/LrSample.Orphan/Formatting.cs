namespace LrSample.Orphan;

// In no solution: LocalRun drops dependencies on it unless `lrun build --include-external`
public static class Formatting
{
    public static string Money(decimal amount) => amount.ToString("N2");
}
