using System.Collections.Generic;
using System.Text;

public enum BenchVerdict
{
    Unjudged,
    Keep,
    Cut,
    Later,

    /// <summary>
    /// Could not be judged — it did not render properly, so there was nothing to look at. Deliberately not
    /// <see cref="Later"/>: that means "seen, deciding later", and mixing the two files assets nobody ever
    /// saw onto the reconsider list.
    /// </summary>
    Blocked,

    /// <summary>The bench failed to place it at all. Set by the bench, not by a verdict key.</summary>
    Error,
}

/// <summary>One judged entry. Mutable because the bench edits it in place as verdicts and notes arrive.</summary>
public sealed class BenchRow
{
    public int Index;
    public string Label;
    public string Question;
    public BenchVerdict Verdict;
    public bool NeedsRework;
    public string Biome;
    public string Note;
    public string CandidatePath;
}

/// <summary>
/// Serialises bench verdicts to markdown: diffable, readable in-repo, and parseable by the promoter.
/// </summary>
public static class AssetBenchReport
{
    public static string BuildMarkdown(string batchId, string isoTimestamp, IReadOnlyList<BenchRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("# Asset bench — ").Append(batchId).AppendLine();
        sb.AppendLine();
        sb.Append("_Judged ").Append(isoTimestamp).Append("._").AppendLine();
        sb.AppendLine();

        if (rows == null || rows.Count == 0)
        {
            sb.AppendLine("No entries in this batch.");
            return sb.ToString();
        }

        int keep = 0, cut = 0, later = 0, unjudged = 0, error = 0, blocked = 0;
        foreach (BenchRow r in rows)
        {
            switch (r.Verdict)
            {
                case BenchVerdict.Keep: keep++; break;
                case BenchVerdict.Cut: cut++; break;
                case BenchVerdict.Later: later++; break;
                case BenchVerdict.Blocked: blocked++; break;
                case BenchVerdict.Error: error++; break;
                default: unjudged++; break;
            }
        }

        sb.Append("**Keep ").Append(keep)
          .Append(" · Cut ").Append(cut)
          .Append(" · Later ").Append(later)
          .Append(" · Blocked ").Append(blocked)
          .Append(" · Unjudged ").Append(unjudged)
          .Append(" · Error ").Append(error)
          .Append("**").AppendLine();
        sb.AppendLine();

        sb.AppendLine("| # | Label | Verdict | Needs rework | Biome | Note | Question | Path |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (BenchRow r in rows)
        {
            // 1-based to match the HUD and the number keys used to judge it.
            sb.Append("| ").Append(r.Index + 1)
              .Append(" | ").Append(Escape(r.Label))
              .Append(" | ").Append(r.Verdict)
              .Append(" | ").Append(r.NeedsRework ? "yes" : "")
              .Append(" | ").Append(Escape(r.Biome))
              .Append(" | ").Append(Escape(r.Note))
              .Append(" | ").Append(Escape(r.Question))
              .Append(" | `").Append(Escape(r.CandidatePath)).Append("` |")
              .AppendLine();
        }

        return sb.ToString();
    }

    static string Escape(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("|", @"\|").Replace("\r", " ").Replace("\n", " ");
}
