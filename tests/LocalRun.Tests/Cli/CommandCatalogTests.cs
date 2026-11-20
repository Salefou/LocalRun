namespace LocalRun.Tests.Cli;

using LocalRun.Cli;
using LocalRun.Logger;
using LocalRun.Options;
using System.Reflection;

public class CommandCatalogTests
{
    private const string Probe = "--zz-probe";

    /// <summary>Asked by a parser only to point at what replaced them: rejected, so not documented.</summary>
    private static readonly string[] RemovedOptions = ["--remote", "--go"];

    public static TheoryData<string> CommandNames => [.. CommandCatalog.Commands.Select(command => command.Name)];

    /// <summary>
    /// Every command reads all its options before <c>EnsureAllConsumed</c>, which rejects the probe: what it asked for by
    /// then is what it accepts, and the help must list exactly that.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommandNames))]
    public async Task EachCommand_DocumentsExactlyTheOptionsItReads(string name)
    {
        var args = new CommandLineArgs([Probe]);

        var ex = await Assert.ThrowsAsync<UsageException>(() => LocalRunCli.RunCommandAsync(name, args, new CommandOutput()));

        Assert.Contains(Probe, ex.Message);
        var documented = CommandCatalog.Commands.Single(command => command.Name == name).Options.Select(option => option.Name);
        Assert.Equal(documented.Order(), args.Asked.Except(RemovedOptions).Order());
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void EachOption_HasTheKindItsParserExpects(string name)
    {
        // A flag given a value, or a value option given none, is a usage error: the kind says which
        foreach (var option in CommandCatalog.Commands.Single(command => command.Name == name).Options)
        {
            Assert.Equal(option.Kind == "flag", option.Value == null);
            Assert.Contains(option.Kind, new[] { "flag", "value", "values" });
            Assert.EndsWith(".", option.Description);
        }
    }

    [Fact]
    public void TheTextHelp_ListsEveryCommandAndOption()
    {
        using var writer = new StringWriter();
        LocalRunCli.WriteHelp(writer);
        string help = writer.ToString();

        foreach (var command in CommandCatalog.Commands)
        {
            Assert.Contains($"\n  {command.Name}", help);
            foreach (var option in command.Options)
                Assert.Contains(option.Name, help);
        }
        Assert.All(CommandCatalog.GlobalOptions, option => Assert.Contains($"\n  {option.Name}", help));
        Assert.All(help.Split(Environment.NewLine).Where(line => !line.StartsWith("  lrun ")), line => Assert.True(line.Length <= 120, line));
    }

    [Fact]
    public void ExitCodes_AreTheCliConstants()
    {
        var constants = typeof(LocalRunCli).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name.StartsWith("Exit")).Select(field => (int)field.GetValue(null)!);

        Assert.Equal(constants.Order(), CommandCatalog.ExitCodes.Select(exit => exit.Code).Order());
    }

    [Fact]
    public void ErrorCodes_AreAllListed_WithTheirExitCode()
    {
        var constants = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral).Select(field => (string)field.GetValue(null)!);

        Assert.Equal(constants.Order(), ErrorCodes.All.Select(code => code.Code).Order());
        Assert.Equal(LocalRunCli.ExitUsage, ErrorCodes.All.Single(code => code.Code == ErrorCodes.Usage).ExitCode);
        Assert.Equal(ErrorCodes.Of(new VerifyFailedException("")).ExitCode, ErrorCodes.All.Single(code => code.Code == ErrorCodes.VerifyFailed).ExitCode);
        Assert.Equal(ErrorCodes.Of(new Exception()).ExitCode, ErrorCodes.All.Single(code => code.Code == ErrorCodes.CommandFailed).ExitCode);
    }

    [Fact]
    public void DiagnosticCodes_AreAllListed()
    {
        var events = typeof(DiagnosticEvents).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Microsoft.Extensions.Logging.EventId))
            .Select(field => ((Microsoft.Extensions.Logging.EventId)field.GetValue(null)!).Name!);

        Assert.Equal([.. events, "unspecified"], DiagnosticEvents.All.Select(code => code.Code));
        Assert.All(DiagnosticEvents.All, code => Assert.NotEmpty(code.Meaning));
    }

    [Theory]
    [InlineData("one two three", 7, new[] { "one two", "three" })]
    [InlineData("averyveryverylongword x", 5, new[] { "averyveryverylongword", "x" })]
    [InlineData("", 10, new[] { "" })]
    public void Wrap_BreaksBetweenWords(string text, int width, string[] lines)
    {
        Assert.Equal(lines, CommandCatalog.Wrap(text, width));
    }
}
