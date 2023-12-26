using AeonHacs;
using Newtonsoft.Json;

namespace AeonHacs.Components
{
    public class Sensor : BindableObject, ISensor
    {
        [JsonProperty]
        public virtual double Value
        {
            get => value;
            protected set => Set(ref this.value, value);
        }
        double value;
    }
}
