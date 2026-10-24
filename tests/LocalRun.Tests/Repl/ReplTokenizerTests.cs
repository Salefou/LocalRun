namespace LocalRun.Tests.Repl;

using LocalRun.Repl;

public class ReplTokenizerTests
{
    [Fact]
    public void SplitsOnWhitespace()
    {
        Assert.Equal(["repack-preview", "--patch", "--suffix", "dev"], ReplTokenizer.Split("  repack-preview --patch\t--suffix  dev "));
    }

    [Fact]
    public void QuotesGroupValuesWithSpaces()
    {
        Assert.Equal(["build-config", "--root", @"C:\My Repos"], ReplTokenizer.Split(@"build-config --root ""C:\My Repos"""));
    }

    [Fact]
    public void EmptyQuotes_AreAnEmptyArgument()
    {
        Assert.Equal(["--suffix", ""], ReplTokenizer.Split(@"--suffix """""));
    }

    [Fact]
    public void BlankLine_HasNoArguments()
    {
        Assert.Empty(ReplTokenizer.Split("   "));
    }

    [Fact]
    public void ByteOrderMark_IsIgnored()
    {
        Assert.Equal(["pwd"], ReplTokenizer.Split("﻿pwd"));
    }

    [Fact]
    public void UnterminatedQuote_IsRejected()
    {
        Assert.Throws<FormatException>(() => ReplTokenizer.Split(@"cd ""C:\oops"));
    }
}
