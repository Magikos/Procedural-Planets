using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class ConsoleRegressionTests
{
    [TestCase("debug.overlay", "false")]
    [TestCase("debug.overlay", "true")]
    [TestCase("console.anchor", "Top")]
    public void AcceptingCommandOpensParameterPicker(string command, string choice)
    {
        var input = new ConsoleInputController(new ConsoleScrollback(), null, () => { });
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var suggestions = typeof(ConsoleInputController).GetField("_suggestions", flags);
        suggestions.SetValue(input, new IntellisenseEngine().Update(command).ToArray());
        var accept = typeof(ConsoleInputController).GetMethod("AcceptSuggestion", flags);
        accept.Invoke(input, null);
        Assert.That(input.Text, Is.EqualTo(command + " "));
        Assert.That(input.Suggestions.Select(s => s.DisplayText), Does.Contain(choice));
        Assert.That(input.GhostActive, Is.False);
        int index = input.Suggestions.ToList().FindIndex(s => s.DisplayText == choice);
        typeof(ConsoleInputController).GetField("_activeSuggestionIdx", flags).SetValue(input, index);
        accept.Invoke(input, null);
        Assert.That(input.Text, Is.EqualTo(command + " " + choice + " "));
        Assert.That(input.Suggestions, Is.Empty);
    }

    Dictionary<string, CommandData> _saved;
    IDictionary<string, CommandData> Registry => (IDictionary<string, CommandData>)ConsoleRegistry.Commands;
    static int _invocations;

    [SetUp]
    public void SetUp()
    {
        _saved = new Dictionary<string, CommandData>(Registry);
        ConsoleRegistry.Scan();
        _invocations = 0;
    }

    [TearDown]
    public void TearDown()
    {
        Registry.Clear();
        foreach (var entry in _saved) Registry.Add(entry.Key, entry.Value);
    }

    [Test]
    public void ImmediateExecutionRejectsAsyncBeforeInvocation()
    {
        AddProbe(nameof(AsyncProbe), true);
        var result = CommandExecutor.ExecuteImmediate("probe");
        Assert.That(result.Success, Is.False);
        Assert.That(_invocations, Is.Zero);
    }

    public static Awaitable AsyncProbe() { _invocations++; return TestConsoleCommands.Async(0); }
    public static ConsoleCommandResult FailureProbe() => ConsoleCommandResult.Fail("rejected by domain");
    public static string RecordProbe() { _invocations++; return "recorded"; }

    [Test]
    public void ScriptStopsOnDomainFailureAndRunsDefer()
    {
        AddProbe(nameof(RecordProbe), false);
        Registry["probe.record"] = Registry["probe"];
        AddProbe(nameof(FailureProbe), false);
        var script = new ConsoleScriptDefinition("Regression", "Regression", "@defer probe.record\nprobe\nprobe.record");
        Assert.Throws<InvalidOperationException>(() =>
            ConsoleScriptRunner.RunAsync(script, null, default).GetAwaiter().GetResult());
        Assert.That(_invocations, Is.EqualTo(1), "Only the deferred command may run.");
    }

    [Test]
    public void EmptyInputOffersFamiliesWithoutExecutableSelection()
    {
        var choices = new IntellisenseEngine().Update("");
        Assert.That(choices.Count, Is.GreaterThan(0));
        Assert.That(choices.All(choice => choice.IsGroup && choice.CompletionText.EndsWith(".")), Is.True);
    }

    [Test]
    public void NumericExpressionsAndEmptyQuotedStringsStillBind()
    {
        var command = new CommandData { Parameters = new[] {
            new ParameterData { Name = "number", Type = typeof(float) },
            new ParameterData { Name = "text", Type = typeof(string) } } };
        Assert.That(CommandParser.TryBind(command, CommandParser.Tokenize("(2+3)*4 \"\""), out var args, out var error), Is.True, error);
        Assert.That(args[0], Is.EqualTo(20f));
        Assert.That(args[1], Is.EqualTo(""));
    }

    void AddProbe(string method, bool async)
    {
        var info = typeof(ConsoleRegressionTests).GetMethod(method);
        Registry["probe"] = new CommandData { Alias = "probe", Description = "", Method = info,
            DeclaringType = typeof(ConsoleRegressionTests), TargetType = MonoTargetType.Static,
            Parameters = Array.Empty<ParameterData>(), IsAsync = async, ReturnType = info.ReturnType };
    }

    [Test]
    public void ExplicitDomainFailureKeepsErrorAndCommandContext()
    {
        AddProbe(nameof(FailureProbe), false);
        var result = CommandExecutor.ExecuteImmediate("probe");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("rejected by domain"));
        Assert.That(result.Alias, Is.EqualTo("probe"));
    }

    [TestCase("script.run Grass Baseline")]
    [TestCase("script.run \"Grass ")]
    [TestCase("script.run 'Grass ")]
    public void FinalStringCompletionKeepsOfferingChoices(string input)
    {
        Assert.That(new IntellisenseEngine().Update(input).Count, Is.GreaterThan(0));
    }

    [Test]
    public void HelpCompletionIncludesEveryPermittedAlias()
    {
        var suggestions = new IntellisenseEngine().Update("help ").Select(s => s.DisplayText).ToArray();
        foreach (var entry in ConsoleRegistry.Commands.Where(pair => ConsoleCommandPolicy.CanExecute(pair.Value)))
            Assert.That(suggestions, Does.Contain(entry.Key));
        Assert.That(suggestions, Does.Contain("Player"));
        Assert.That(suggestions, Does.Contain("camera"));
    }

    [Test]
    public void MidLineCompletionPreservesFollowingArgument()
    {
        const string input = "water.set Wave 0.5";
        var suggestions = new IntellisenseEngine().Update(input, "water.set Wave".Length);
        Assert.That(suggestions.Count, Is.GreaterThan(0));
        foreach (var suggestion in suggestions)
        {
            Assert.That(suggestion.CompletionText, Does.EndWith(" 0.5"));
            Assert.That(suggestion.CompletionCursor, Is.LessThan(suggestion.CompletionText.Length));
        }
    }

    [Test]
    public void VectorArgumentsUseParserConsumption()
    {
        var command = new CommandData { Parameters = new[] {
            new ParameterData { Name = "position", Type = typeof(Vector3) },
            new ParameterData { Name = "enabled", Type = typeof(bool) } } };
        const string input = "probe 1 2 3 tr";
        Assert.That(CommandParser.TryGetArgument(input, input.Length, command, out int index, out _, out _, out string partial), Is.True);
        Assert.That(index, Is.EqualTo(1));
        Assert.That(partial, Is.EqualTo("tr"));
    }

    [TestCase(ConsoleReleasePolicy.Unreviewed, false)]
    [TestCase(ConsoleReleasePolicy.DevelopmentOnly, false)]
    [TestCase(ConsoleReleasePolicy.Administrator, false)]
    [TestCase(ConsoleReleasePolicy.Player, true)]
    public void ReleasePolicyFailsClosed(ConsoleReleasePolicy policy, bool allowed)
    {
        Assert.That(ConsoleCommandPolicy.CanExecute(policy, false), Is.EqualTo(allowed));
        Assert.That(ConsoleCommandPolicy.CanExecute(policy, true), Is.True);
    }

    [TestCase("camera.teleport", "camera.location.go")]
    [TestCase("path.clear", "path.cache.clear")]
    [TestCase("script.run", "run-script")]
    [TestCase("path.stroke-start", "path.stroke.start")]
    [TestCase("path.mouse", "path.mouse.enabled")]
    [TestCase("path.replay", "path.saved.replay")]
    [TestCase("debug.capture", "debug.capture.run")]
    [TestCase("tree.gen", "tree.preview.generate")]
    [TestCase("help", "console.help")]
    public void AliasesShareDescriptorAndPolicy(string name, string alias)
    {
        Assert.That(ConsoleRegistry.TryGet(name, out var canonical), Is.True);
        Assert.That(ConsoleRegistry.TryGet(alias, out var alternate), Is.True);
        Assert.That(alternate, Is.SameAs(canonical));
    }

    [Test]
    public void EntireCatalogMeetsAuthoringContract()
    {
        foreach (var command in CommandCatalog.All) Assert.DoesNotThrow(() => CommandContract.Validate(command));
        Assert.That(CommandCatalog.All.Select(c => c.Alias).Distinct().Count(), Is.EqualTo(CommandCatalog.All.Count()));
        Assert.That(CommandCatalog.Search("Player").Count(), Is.EqualTo(8));
        Assert.That(CommandCatalog.Search("Unreviewed"), Is.Empty);
    }

    [TestCase("group")]
    [TestCase("policy")]
    [TestCase("name")]
    [TestCase("description")]
    [TestCase("alias")]
    [TestCase("parameter")]
    [TestCase("provider")]
    [TestCase("return")]
    [TestCase("target")]
    public void AuthoringContractRejectsInvalidDeclarations(string defect)
    {
        var command = new CommandData {
            Alias = "probe.valid", Group = "Tests", Description = "A valid probe.",
            ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly, TargetType = MonoTargetType.Static,
            Method = typeof(ConsoleRegressionTests).GetMethod(nameof(FailureProbe)),
            DeclaringType = typeof(ConsoleRegressionTests), Parameters = Array.Empty<ParameterData>(),
            ReturnType = typeof(ConsoleCommandResult) };
        switch (defect)
        {
            case "group": command.Group = null; break;
            case "policy": command.ReleasePolicy = ConsoleReleasePolicy.Unreviewed; break;
            case "name": command.Alias = "probe"; break;
            case "description": command.Description = ""; break;
            case "alias": command.Aliases = new[] { command.Alias }; break;
            case "parameter": command.Parameters = new[] { new ParameterData { Name = "bad", Type = typeof(DateTime) } }; break;
            case "provider": command.Parameters = new[] { new ParameterData { Name = "bad", Type = typeof(string), CompletionProvider = typeof(string) } }; break;
            case "return": command.ReturnType = typeof(System.Threading.Tasks.Task); break;
            case "target": command.TargetType = MonoTargetType.Registry; break;
        }
        Assert.Throws<InvalidOperationException>(() => CommandContract.Validate(command));
    }

    [Test]
    public void WaterCommandsUseSeparateAdapterAndExplicitResults()
    {
        foreach (var command in CommandCatalog.All.Where(c => c.Family == "water"))
        {
            Assert.That(command.DeclaringType, Is.EqualTo(typeof(WaterCommands)));
            Assert.That(command.ReturnType, Is.EqualTo(typeof(ConsoleCommandResult)));
        }
        var previous = ConsoleRegistry.GetInstance(typeof(WaterCommands));
        var owner = new GameObject("Console water regression");
        var surface = new PlanetWaterSurface(owner.transform);
        var commands = new WaterCommands(surface);
        try
        {
            Assert.That(ConsoleRegistry.GetInstance(typeof(WaterCommands)), Is.Not.Null);
            Assert.Throws<ArgumentNullException>(() => surface.ApplySettings(null));
            var outcome = CommandExecutor.ExecuteImmediate("water.set NotARealWaterField 1");
            Assert.That(outcome.Success, Is.False);
        }
        finally
        {
            surface.Dispose();
            commands.Dispose();
            Assert.That(ConsoleRegistry.GetInstance(typeof(WaterCommands)), Is.Null);
            if (previous is WaterCommands adapter) ConsoleRegistry.RegisterInstance(adapter);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase("../escaped")]
    [TestCase("..\\escaped")]
    [TestCase("C:\\escaped")]
    [TestCase("/")]
    public void DumpRejectsPathsWithoutWriting(string name) =>
        Assert.Throws<ArgumentException>(() => ConsoleBuiltins.ValidateDumpName(name));

    [TestCase("capture")]
    [TestCase("capture.txt")]
    [TestCase("")]
    public void DumpAcceptsFilenames(string name) => Assert.DoesNotThrow(() => ConsoleBuiltins.ValidateDumpName(name));

    [Test]
    public void PastePreservesTabsAndRejectsMultilineInput()
    {
        string clipboard = GUIUtility.systemCopyBuffer;
        try
        {
            var scrollback = new ConsoleScrollback();
            var input = new ConsoleInputController(scrollback, null, () => { });
            var paste = typeof(ConsoleInputController).GetMethod("PasteClipboard", BindingFlags.NonPublic | BindingFlags.Instance);
            GUIUtility.systemCopyBuffer = "echo\talpha";
            paste.Invoke(input, null);
            Assert.That(input.Text, Is.EqualTo("echo alpha"));
            GUIUtility.systemCopyBuffer = "echo beta\necho gamma";
            paste.Invoke(input, null);
            Assert.That(input.Text, Is.EqualTo("echo alpha"));
            Assert.That(scrollback.Count, Is.EqualTo(1));
        }
        finally { GUIUtility.systemCopyBuffer = clipboard; }
    }
}
