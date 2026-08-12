using System.Collections.Generic;
using NUnit.Framework;

public class AssetBenchReportTests
{
    static BenchRow Row(int i, string label, BenchVerdict v, string note = "") => new BenchRow
    {
        Index = i,
        Label = label,
        Question = "q",
        Verdict = v,
        NeedsRework = false,
        Biome = "Grassland",
        Note = note,
        CandidatePath = "Assets/_Bench/Pack/Thing.prefab"
    };

    [Test]
    public void BuildMarkdown_IncludesBatchIdAndTimestamp()
    {
        string md = AssetBenchReport.BuildMarkdown("scatter-candidates", "2026-08-11T02:00:00Z",
            new List<BenchRow> { Row(0, "Quirky Fox", BenchVerdict.Keep) });

        StringAssert.Contains("scatter-candidates", md);
        StringAssert.Contains("2026-08-11T02:00:00Z", md);
    }

    [Test]
    public void BuildMarkdown_EmitsOneRowPerEntry()
    {
        var rows = new List<BenchRow>
        {
            Row(0, "Alpha", BenchVerdict.Keep),
            Row(1, "Bravo", BenchVerdict.Cut),
            Row(2, "Charlie", BenchVerdict.Later)
        };

        string md = AssetBenchReport.BuildMarkdown("b", "t", rows);

        StringAssert.Contains("Alpha", md);
        StringAssert.Contains("Bravo", md);
        StringAssert.Contains("Charlie", md);
    }

    [Test]
    public void BuildMarkdown_EscapesPipesInNotes()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t",
            new List<BenchRow> { Row(0, "A", BenchVerdict.Cut, "too dark | too shiny") });

        StringAssert.Contains(@"too dark \| too shiny", md);
    }

    [Test]
    public void BuildMarkdown_FlattensNewlinesInNotes()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t",
            new List<BenchRow> { Row(0, "A", BenchVerdict.Cut, "line one\nline two") });

        StringAssert.DoesNotContain("line one\nline two", md);
        StringAssert.Contains("line one line two", md);
    }

    [Test]
    public void BuildMarkdown_HandlesEmptyBatch()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t", new List<BenchRow>());

        Assert.IsNotNull(md);
        StringAssert.Contains("no entries", md.ToLowerInvariant());
    }

    [Test]
    public void BuildMarkdown_HandlesNullRows()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t", null);

        Assert.IsNotNull(md);
        StringAssert.Contains("no entries", md.ToLowerInvariant());
    }

    [Test]
    public void BuildMarkdown_SummarisesVerdictCounts()
    {
        var rows = new List<BenchRow>
        {
            Row(0, "A", BenchVerdict.Keep),
            Row(1, "B", BenchVerdict.Keep),
            Row(2, "C", BenchVerdict.Cut),
            Row(3, "D", BenchVerdict.Unjudged)
        };

        string md = AssetBenchReport.BuildMarkdown("b", "t", rows).ToLowerInvariant();

        StringAssert.Contains("keep 2", md);
        StringAssert.Contains("cut 1", md);
        StringAssert.Contains("unjudged 1", md);
    }

    [Test]
    public void BuildMarkdown_MarksNeedsRework()
    {
        var row = Row(0, "A", BenchVerdict.Keep);
        row.NeedsRework = true;

        string md = AssetBenchReport.BuildMarkdown("b", "t", new List<BenchRow> { row });

        StringAssert.Contains("yes", md);
    }
}
