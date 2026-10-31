using System.Collections.Generic;
using HousingStudio.Core;

namespace HousingStudio.Session;

/// <summary>What looking after a home (<see cref="Care"/>) reads of a run.</summary>
public sealed partial class PlacementRunner
{
    /// <summary>The design's pieces the run (or the last one) was given.</summary>
    public IReadOnlyList<PlanEntry> Entries => entries;

    /// <summary>Counts the runs started (a run's pieces are planned again after a clear out: this tells runs apart, they don't).</summary>
    public int RunNumber { get; private set; }

    /// <summary>The run is putting away its clear out; the design's pieces are planned once that's done.</summary>
    public bool ClearingOutFirst => clearOutReplan;

    /// <summary>The dyes expected at the start, while the dye phase isn't planned yet (0 once it is, or without dyeing).</summary>
    public int DyesStillExpected => dyesPlanned ? 0 : dyesExpected;
}
