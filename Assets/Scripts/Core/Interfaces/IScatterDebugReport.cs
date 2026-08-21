using System.Text;

// Lets the F10 capture sidecar describe scatter without Core referencing the Planet assembly, the same way
// IGrassDebugStatsProvider does for grass. The Planet side owns the knowledge of what is worth saying, because
// only it knows the library layout; Core just asks for the text.
public interface IScatterDebugReport
{
    void Append(StringBuilder sb);
}
