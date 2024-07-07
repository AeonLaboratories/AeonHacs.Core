using static AeonHacs.Components.CegsPreferences;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    /// <summary>
    /// Sable model CA10 carbon dioxide analyzer.
    /// </summary>
    public class SableCA10 : SerialController
    {
        static double kPaToTorr = Torr / (Pascal / 1000.0);
        static double percentToPpm = 1000000 / 100;
        static Command StatusCommand { get; set; } = new Command() { Message = "?", ResponsesExpected = 1, Hurry = false };

        [HacsPreConnect]
        public void PreConnect()
        {
            CO2Ppm = new($"{Name}.CO2Ppm", () => co2Percent * percentToPpm);
            CO2PartialPressureTorr = new($"{Name}.CO2PartialPressureTorr", () => co2PartialPressurekPa * kPaToTorr);
            Pressure = new($"{Name}.Pressure", () => barometricPressurekPa * kPaToTorr);
            Temperature = new($"{Name}.Temperature", () => cellTemperature);
        }

        /// <summary>
        /// Carbon dioxide percentage, pressure corrected % to 0.0001% (&gt;= 1000 ppm) or 0.00001% (&lt; 1000 ppm)
        /// </summary>
        public double CO2Percent
        {
            get => co2Percent;
            set => Ensure(ref co2Percent, value);
        }
        double co2Percent;

        /// <summary>
        /// Carbon dioxide content in parts per million
        /// </summary>
        public NamedValue CO2Ppm { get; private set; }

        /// <summary>
        /// Carbon dioxide partial pressure in kPa to 0.0001 kPa (&gt;= 1 kPa) or 0.00001 kPa (&lt; 1 kPa)
        /// </summary>
        public double CO2PartialPressurekPa
        {
            get => co2PartialPressurekPa;
            set => Ensure(ref co2PartialPressurekPa, value);
        }
        double co2PartialPressurekPa;
        /// <summary>
        /// Carbon dioxide partial pressure in Torr
        /// </summary>
        public NamedValue CO2PartialPressureTorr { get; private set; }

        /// <summary>
        /// Barometric pressure in kPa to 0.001 kPa
        /// </summary>
        public double BarometricPressurekPa
        {
            get => barometricPressurekPa;
            set => Ensure(ref barometricPressurekPa, value);
        }
        double barometricPressurekPa;
        /// <summary>
        /// Carbon Analyzer chamber pressure in Torr
        /// </summary>
        public NamedValue Pressure { get; private set; }

        /// <summary>
        /// Cell temperature in degrees C, to 0.001 deg C
        /// </summary>
        public double CellTemperature
        {
            get => cellTemperature;
            set => Ensure(ref cellTemperature, value);
        }
        double cellTemperature;
        public NamedValue Temperature { get; private set; }

        /// <summary>
        /// HacsComponent interface for Sable Systems CA-10 carbon dioxide analyzer.
        /// </summary>
        public SableCA10()
        {
            SelectServiceHandler = SelectService;
            ResponseProcessor = ValidateResponse;
        }

        /// <summary>
        /// Always returns the StatusCommand.
        /// </summary>
        /// <returns>Command { Message = &quot;?&quot;, ResponsesExpected = 1, Hurry = false }</returns>
        protected virtual Command SelectService() => Stopping ? DefaultCommand : StatusCommand;


        #region controller response validation helpers

        // TODO: These two helper methods are present in multiple places.
        // Where should they be moved to?
        bool ErrorCheck(bool errorCondition, string errorMessage)
        {
            if (errorCondition)
            {
                if (LogEverything)
                    Log?.Record($"{errorMessage}");
                return true;
            }
            return false;
        }
        bool LengthError(object[] elements, int nExpected, string elementDescription = "value", string where = "")
        {
            var n = elements.Length;
            if (!where.IsBlank()) where = $" {where}";
            return ErrorCheck(n != nExpected,
                $"Expected {ToUnitsString(nExpected, elementDescription)}{where}, not {n}.");
        }

        #endregion controller response validation helpers

        /// <summary>
        /// Interprets the response and determines whether it is valid.
        /// </summary>
        /// <param name="response"></param>
        /// <param name="which: when multiple responses are recieved, which one"></param>
        /// <returns>Whether the response was valid.</returns>
        protected virtual bool ValidateResponse(string response, int which)
        {
            try
            {
                var lines = response.GetLines();
                if (lines.Length != 1) return false;
                var line = 1;

                //            1        2
                // 012345678901234567890123456789
                //"0.06203,0.05216,092.288,31.104"
                var s = lines[0].TrimEnd();
                if (s.Length != 30)
                {
                    if (LogEverything)
                        Log?.Record($"Expected 30 characters in response, not {s.Length}.");
                    return false;
                }

                var values = s.Split([',']);
                if (LengthError(values, 4, "value", $"on controller data line {line}"))
                    return false;

                var d = double.Parse(values[0]);
                //if (ErrorCheck(d < 0 || d >= 1,
                //        $"Invalid CO2 percentage in status report: {d:0.00000}"))
                //    return false;
                CO2Percent = d;

                d = double.Parse(values[1]);
                CO2PartialPressurekPa = d;

                d = double.Parse(values[2]);
                BarometricPressurekPa = d;

                d = double.Parse(values[3]);
                CellTemperature = d;
            }
            catch { return false; }
            return true;
        }

        public override string ToString()
        {
            return $"{Name}: CO2 {CO2Ppm.Value:0.0} ppm\r\n" +
                IndentLines($"CO2 Partial Pressure: {CO2PartialPressureTorr.Value:0.0e0} Torr\r\n" +
                            $"Ambient Pressure: {Pressure.Value:0.0e0} Torr\r\n" +
                            $"Sensor Temperature: {Temperature.Value:0.000} °C");
        }
    }
}