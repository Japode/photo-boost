using PhotoBoost.Inference;

namespace PhotoBoost.App.Tests;

/// <summary>PB39: every flag parses both ways, and anything else is refused with a message that names it.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void NoArgumentsIsAPlainStart()
    {
        ParsedCommandLine parsed = CommandLine.Parse([]);

        Assert.True(parsed.Ok);
        Assert.Equal(new AppOptions(), parsed.Options);
        Assert.Equal(AppOptions.DefaultModels, parsed.Options!.ModelsFolder);
    }

    [Fact]
    public void EveryFlagParsesWithASpaceOrAnEqualsSign()
    {
        ParsedCommandLine parsed = CommandLine.Parse(
            ["--open", @"C:\photos\a b.jpg", "--models=D:\\models", "--provider", "cpu", "--export=out.png", "--quality", "High"]);

        Assert.Equal(
            new AppOptions(@"C:\photos\a b.jpg", @"D:\models", ProviderPreference.Cpu, "out.png", QualityMode.High),
            parsed.Options);
        Assert.Equal(@"D:\models", parsed.Options!.ModelsFolder);
    }

    [Theory]
    [InlineData("auto", ProviderPreference.Auto)]
    [InlineData("dml", ProviderPreference.DirectML)]
    [InlineData("cpu", ProviderPreference.Cpu)]
    [InlineData("winml", ProviderPreference.WindowsML)] // PB43
    public void TheProviderFlagTakesItsFourValues(string value, ProviderPreference expected) =>
        Assert.Equal(expected, CommandLine.Parse(["--provider", value]).Options!.Provider);

    [Theory]
    [InlineData(new[] { "--zoom", "2" }, "Unknown option --zoom")]
    [InlineData(new[] { "photo.jpg" }, "use --open \"photo.jpg\"")]
    [InlineData(new[] { "--open" }, "--open needs a value")]
    [InlineData(new[] { "--open", "--models", "x" }, "--open needs a value")]
    [InlineData(new[] { "--export=" }, "--export needs a value")]
    [InlineData(new[] { "--provider", "cuda" }, "not one of auto, winml, dml, cpu")]
    [InlineData(new[] { "--quality", "fast" }, "not one of compact, high")]
    [InlineData(new[] { "--open", "a.jpg", "--open", "b.jpg" }, "--open is given twice")]
    public void AnythingElseIsRefusedWithAMessage(string[] args, string message)
    {
        ParsedCommandLine parsed = CommandLine.Parse(args);

        Assert.False(parsed.Ok);
        Assert.Contains(message, parsed.Error, StringComparison.Ordinal);
    }
}
