namespace HousingStudio;

/// <summary>A rehearsal (Ready's Preview → Rehearse the run): its own settings.</summary>
public sealed partial class Configuration
{
    /// <summary>
    /// A rehearsal lets go of a piece it held at its spot without setting it down: its old spot is
    /// written back, then the editor's Move button is pressed (a mode change drops what the editor
    /// holds; not yet seen in the game). Until this is on, the rehearsal holds before that click for
    /// Continue; switch it on once you've watched it work ("Trust it" in the rehearsal's callout).
    /// </summary>
    public bool RehearsalTrustLetGo { get; set; }
}
