using Winwright.Processes;
using Winwright.Projects;
using Winwright.Scenarios;
using Winwright.Verdicts;
using Winwright.Windowing;

namespace PhotoBoost.Cases;

/// <summary>
/// Every UI case this repository declares in cases/*.cases.json, run against the launched app
/// (PB38, after quickshell's CasesRun). The accessibility tree is read rather than the screen
/// photographed, so it needs neither foreground nor a capture.
///
/// <para><b>One test, not one per case</b>: the engine runs the selection and answers one verdict
/// over all of it, and lends a shareable fixture's window to every case that asks for it.</para>
///
/// <para><b>A desk that cannot be observed is a skip with its reason</b>, never a pass that
/// checked nothing.</para>
/// </summary>
public sealed class CasesRun
{
    [Fact]
    public void EveryUiCaseInThisRepositoryRuns()
    {
        IReadOnlyList<CaseDeclaration> declared = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository.Root, "cases")));
        Assert.NotEmpty(declared);

        Desk desk = Desk.Read();
        if (!desk.CanObserve)
        {
            Assert.Skip($"no UI case ran: this desk lacks {desk.FirstAbsent!.Name}");
        }

        ProjectDeclaration project = ForThisBuild(Repository.Root);
        using ProcessRegister register = ProcessRegister.For(project);
        SuiteVerdict verdict = Suite.Launch(declared, Selection.All, register, project);

        string ran = $"UI cases: {verdict.Ran.Count} of {declared.Count} declared ran";
        TestContext.Current.TestOutputHelper?.WriteLine(ran);
        TestContext.Current.TestOutputHelper?.WriteLine(Read(verdict));

        // A case declared and not run is a claim nobody checked.
        Assert.True(verdict.Ran.Count == declared.Count, $"{ran}.{Environment.NewLine}{Read(verdict)}");
        Assert.True(verdict.Outcome == RunOutcome.Passed, Read(verdict));
        Assert.Equal(0, verdict.ExitCode);

        // And nothing this run started is still on the desk.
        IReadOnlyList<Survivor> survivors = register.StopAll();
        Assert.True(survivors.Count == 0, string.Join(Environment.NewLine, survivors));
    }

    /// <summary>
    /// The declaration pointed at the configuration this assembly was built in: winwright.json names
    /// the Debug app, and a Release suite must launch the Release one. A copy with that one segment
    /// changed is written beside the original, since its paths are relative to its own folder.
    /// </summary>
    private static ProjectDeclaration ForThisBuild(string repository)
    {
        string configuration = typeof(CasesRun).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>()
            .Select(one => one.Configuration)
            .FirstOrDefault() ?? "Debug";

        string declared = Path.Combine(repository, "winwright.json");
        if (configuration == "Debug")
        {
            return ProjectDeclaration.Load(declared);
        }

        string text = File.ReadAllText(declared);
        string pointed = text.Replace("/bin/Debug/", $"/bin/{configuration}/", StringComparison.Ordinal);
        Assert.True(pointed != text, "winwright.json names no /bin/Debug/ path to point at this build");

        string copy = Path.Combine(repository, $"winwright.{configuration}.json");
        File.WriteAllText(copy, pointed);
        return ProjectDeclaration.Load(copy);
    }

    /// <summary>The whole reading: failures, what could not be asked, what broke, and the trace.</summary>
    private static string Read(SuiteVerdict verdict)
    {
        List<string> lines = [.. verdict.Render()];
        foreach (CaseResult unhappy in verdict.Unhappy)
        {
            lines.Add(string.Empty);
            lines.Add($"{unhappy.Declared.Name}:");
            lines.AddRange(unhappy.Verdict.Failures.Select(one => $"  failed    {one}"));
            lines.AddRange(unhappy.Verdict.Unchecked.Select(one => $"  unchecked {one}"));
            lines.AddRange(unhappy.Verdict.Broke.Select(one => $"  broke     {one}"));
            lines.AddRange(unhappy.Trace.Select(one => $"  trace     {one}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
