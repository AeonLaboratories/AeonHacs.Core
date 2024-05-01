using static AeonHacs.Utilities.Utility;
using static LabJack.LabJackUD.LJUD;

namespace AeonHacs.Components
{
    public class SableCA10 : SerialController
    {
        /// <summary>
        /// Carbon dioxide percentage, pressure corrected % to 0.00001% (>= 1000 ppm) or 0.00001% (< 1000 ppm)
        /// </summary>
        public double CO2Percent
        {
            get => co2Percent;
            set => Ensure(ref co2Percent, value);
        }
        double co2Percent;

        /// <summary>
        /// Carbon dioxide partial pressure in kPa to 0.0001 kPa (>= 1 kPa) or 0.00001 kPa (< 1 kPa)
        /// </summary>
        public double CO2PartialPressurekPa
        {
            get => co2PartialPressurekPa;
            set => Ensure(ref co2PartialPressurekPa, value);
        }
        double co2PartialPressurekPa;

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
        /// Cell temperature in degrees C, to 0.001 deg C
        /// </summary>
        public double CellTemperature
        {
            get => cellTemperature;
            set => Ensure(ref cellTemperature, value);
        }
        double cellTemperature;

        /// <summary>
        /// HacsComponent interface for Sable Systems CA-10 carbon dioxide analyzer.
        /// </summary>
        public SableCA10()
        {
            DefaultCommand.Message = "?";
            DefaultCommand.ResponsesExpected = 1;
            DefaultCommand.Hurry = false;

            SelectServiceHandler = SelectService;
            ResponseProcessor = ValidateResponse;
        }

        /// <summary>
        /// Always returns the DefaultCommand object Command("?", 1, false).
        /// </summary>
        /// <returns>Command("?", 1, false)</returns>
        protected virtual Command SelectService()
        {
            return DefaultCommand;
        }

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
        /// 
        /// </summary>
        /// <param name="response"></param>
        /// <param name="which"></param>
        /// <returns></returns>
        protected virtual bool ValidateResponse(string response, int which)
        {
            try
            {
                var lines = response.GetLines();
                if (lines.Length != 1) return false;
                var line = 1;

                var values = lines[0].GetValues();
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
    }
}