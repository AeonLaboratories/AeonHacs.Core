using static AeonHacs.Components.CegsPreferences;

namespace AeonHacs.Components;

public class d13CPort : LinePort, Id13CPort
{
    /// <summary>
    /// A d13C port should be kept closed unless it is Loaded or Prepared.
    /// </summary>
    public virtual bool ShouldBeClosed =>
        State != States.Loaded &&
        State != States.Prepared;

    string Mass
    {
        get
        {
            var ugC = Sample.Micrograms_d13C;
            var umolC = ugC / GramsCarbonPerMole;
            return $" {ugC:0.0} µgC = {umolC:0.00} µmol";
        }
    }
    public override string Contents
    {
        get
        {
            var id = Sample?.LabId ?? "";
            var split = (Sample?.Split ?? 0) > 0 ? $".{Sample.Split}" : "";
            var mass = id.IsBlank() ? "" : Mass;
            return $"{id}{split}{mass}";
        }
    }
}
