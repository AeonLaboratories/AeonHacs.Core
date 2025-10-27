using System;

namespace AeonHacs;

public abstract class HacsBase : HacsComponent, IHacsBase
{
    public Action SaveSettings { get; set; }
    public Action<string> SaveSettingsToFile { get; set; }
}