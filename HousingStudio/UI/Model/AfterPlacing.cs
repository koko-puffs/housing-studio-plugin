namespace HousingStudio.UI.Model;

/// <summary>What <see cref="AfterPlacing"/> is shown of the game each tick.</summary>
/// <param name="RunActive">The placing run is going.</param>
/// <param name="RunNumber">The placing runner's count of runs started (a new run has a new number).</param>
/// <param name="RunFinished">The last placing run finished (not stopped, not a preview).</param>
/// <param name="ErrandsActive">The errands are going.</param>
/// <param name="ErrandsFinished">The errands finished (not stopped).</param>
/// <param name="ErrandsResultWaiting">The errands' summary is still on screen.</param>
/// <param name="FixturesActive">The fixtures are being set.</param>
/// <param name="Design">The design loaded now (its file, else its name).</param>
public readonly record struct AfterPlacingSeen(
    bool RunActive, int RunNumber, bool RunFinished, bool ErrandsActive, bool ErrandsFinished, bool ErrandsResultWaiting, bool FixturesActive,
    string? Design);

/// <summary>
/// "Set them for me after placing": follows the one placing run it was armed for (by its run
/// number), or the errands and every placing run they start, and says once when the fixtures are
/// due: the run (or every one of the errands' runs, and the errands themselves) finished, nothing else
/// runs, the errands' summary is put away, and the design is still the one armed for. A run stopped,
/// failed or previewed, a different run, the errands stopped, or another design: never. Pure, so it's
/// tested; stepped every framework tick, whether the window is open or not.
/// </summary>
public sealed class AfterPlacing
{
    private enum Phase
    {
        Off,
        Run,
        Errands,
        Due,
    }

    private Phase phase;
    private int runNumber;
    private string? design;
    private bool placedOne;
    private bool spoiled;
    private bool watching;
    private int watched;

    public bool Armed => phase != Phase.Off;

    /// <summary>A placing run started on Ready's behalf (already going: its number is the runner's now).</summary>
    public void ArmRun(int run, string? forDesign)
    {
        phase = Phase.Run;
        runNumber = run;
        design = forDesign;
    }

    /// <summary>The errands were sent out (they place after each trip).</summary>
    public void ArmErrands(string? forDesign)
    {
        phase = Phase.Errands;
        design = forDesign;
        placedOne = spoiled = watching = false;
    }

    public void Disarm() => phase = Phase.Off;

    /// <summary>One tick. True once, when the fixtures should be set now for the design armed for.</summary>
    public bool Step(in AfterPlacingSeen s)
    {
        switch (phase)
        {
            case Phase.Off:
                return false;
            case Phase.Run:
                if (s.RunNumber != runNumber)
                {
                    phase = Phase.Off;
                    return false;
                }
                if (s.RunActive) return false;
                phase = s.RunFinished ? Phase.Due : Phase.Off;
                break;
            case Phase.Errands:
                if (s.RunActive)
                {
                    watching = true;
                    watched = s.RunNumber;
                    return false;
                }
                if (watching)
                {
                    watching = false;
                    if (s.RunFinished && s.RunNumber == watched) placedOne = true;
                    else spoiled = true;
                }
                if (s.ErrandsActive) return false;
                phase = s.ErrandsFinished && placedOne && !spoiled ? Phase.Due : Phase.Off;
                break;
        }
        if (phase != Phase.Due) return false;
        if (s.RunActive || s.ErrandsActive || s.ErrandsResultWaiting || s.FixturesActive) return false;
        phase = Phase.Off;
        return s.Design == design;
    }
}
