namespace PhotoBoost.Inference.Tests;

/// <summary>
/// PB14: the provider is DirectML on the GPU with the most video memory when the probe runs
/// there, CPU otherwise, with the reason kept for the status bar. Never a crash for a GPU.
/// </summary>
public sealed class ProviderDetectorTests
{
    private static readonly GpuAdapter Integrated = new(1, "Intel UHD 770", 128L << 20);
    private static readonly GpuAdapter Discrete = new(0, "NVIDIA RTX 4060", 8L << 30);

    [Fact]
    public void AutoPicksDirectMLOnTheAdapterWithTheMostVideoMemory()
    {
        FakeProbe probe = new(directMLWorks: true);

        ProviderSelection selection = ProviderDetector.Detect(ProviderPreference.Auto, [Integrated, Discrete], probe);

        Assert.Equal(ExecutionProvider.DirectML, selection.Provider);
        Assert.Equal(Discrete, selection.Adapter);
        Assert.Null(selection.Fallback);
        Assert.Equal("DirectML · NVIDIA RTX 4060, 8 GB", selection.Label);
    }

    [Fact]
    public void AFailingGpuFallsBackToCpuWithTheReason()
    {
        FakeProbe probe = new(directMLWorks: false);

        ProviderSelection selection = ProviderDetector.Detect(ProviderPreference.Auto, [Discrete], probe);

        Assert.Equal(ExecutionProvider.Cpu, selection.Provider);
        Assert.Equal("887A0004", selection.Fallback);
        Assert.Equal("CPU (GPU unavailable: 887A0004)", selection.Label);
    }

    [Fact]
    public void NoGpuFallsBackToCpuAndSaysSo()
    {
        ProviderSelection selection = ProviderDetector.Detect(ProviderPreference.DirectML, [], new FakeProbe(directMLWorks: true));

        Assert.Equal(ExecutionProvider.Cpu, selection.Provider);
        Assert.Equal("no DirectX 12 GPU found", selection.Fallback);
    }

    [Fact]
    public void CpuPreferenceNeverTriesTheGpu()
    {
        FakeProbe probe = new(directMLWorks: true);

        ProviderSelection selection = ProviderDetector.Detect(ProviderPreference.Cpu, [Discrete], probe);

        Assert.Equal(ExecutionProvider.Cpu, selection.Provider);
        Assert.Equal("CPU", selection.Label);
        Assert.DoesNotContain(ExecutionProvider.DirectML, probe.Tried);
    }

    [Fact]
    public void ACpuThatCannotRunIsAnError() =>
        Assert.Throws<InvalidOperationException>(() =>
            ProviderDetector.Detect(ProviderPreference.Cpu, [], new FakeProbe(directMLWorks: false, cpuWorks: false)));

    [Theory]
    [InlineData("auto", ProviderPreference.Auto)]
    [InlineData("dml", ProviderPreference.DirectML)]
    [InlineData("DirectML", ProviderPreference.DirectML)]
    [InlineData(" cpu ", ProviderPreference.Cpu)]
    public void ProviderFlagValuesParse(string value, ProviderPreference expected)
    {
        Assert.True(ProviderDetector.TryParse(value, out ProviderPreference preference));
        Assert.Equal(expected, preference);
    }

    [Theory]
    [InlineData("cuda")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownProviderFlagValuesAreRefused(string? value) =>
        Assert.False(ProviderDetector.TryParse(value, out _));

    [Fact]
    public void TheRealProbeRunsOnCpu()
    {
        Assert.True(new OnnxRuntimeProbe().TryRun(ExecutionProvider.Cpu, null, out string? error), error);
    }

    [Fact]
    public void RealDetectionAlwaysAnswers()
    {
        // Whatever this desk has: a GPU that runs, one that does not, or none. Never a throw.
        ProviderSelection selection = ProviderDetector.Detect(ProviderPreference.Auto);

        Assert.False(string.IsNullOrWhiteSpace(selection.Label));
        Assert.Equal(selection.Provider == ExecutionProvider.DirectML, selection.Adapter is not null);
    }

    [Fact]
    public void EveryListedAdapterIsHardwareWithAName()
    {
        foreach (GpuAdapter adapter in GpuAdapters.Enumerate())
        {
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            Assert.DoesNotContain("Basic Render", adapter.Name, StringComparison.Ordinal);
        }
    }

    private sealed class FakeProbe(bool directMLWorks, bool cpuWorks = true) : IProviderProbe
    {
        public List<ExecutionProvider> Tried { get; } = [];

        public bool TryRun(ExecutionProvider provider, GpuAdapter? adapter, out string? error)
        {
            Tried.Add(provider);
            bool works = provider == ExecutionProvider.DirectML ? directMLWorks : cpuWorks;
            error = works ? null : "887A0004";
            return works;
        }
    }
}
