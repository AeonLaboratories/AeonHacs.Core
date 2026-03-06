using AeonHacs.Utilities;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

using Datum = (double Input, double Output, double Slope);

namespace AeonHacs;

/// <summary>
/// Provides Piecewise Cubic Hermite Interpolating Polynomial (PCHIP) interpolation for a set of data.
/// This class uses a monotonic interpolation method that preserves the shape of the data and avoids overshoots.
/// </summary>
public class PchipInterpolator : Operation
{
    [JsonProperty]
    public SortedObservableList<DataPoint> CalibrationData { get; } = new(Comparer<DataPoint>.Create((a, b) => a.Input.CompareTo(b.Input)));

    private Datum[] points = [];

    /// <summary>
    /// Creates a new instance of the <see cref="PchipInterpolator"/> class.
    /// </summary>
    public PchipInterpolator() : this([]) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PchipInterpolator"/> class.
    /// </summary>
    /// <param name="calibrationData">The data points for interpolation. Each point must have an Input and Output value.</param>
    public PchipInterpolator(params DataPoint[] calibrationData)
    {
        foreach (var point in calibrationData)
            CalibrationData.Add(point);
        CalibrationData.CollectionChanged += (_, _) => CalculateSlopes();
        CalculateSlopes();
    }

    public void Zero(double zero)
    {
        if (CalibrationData.Count > 0)
        {
            var first = CalibrationData[0];
            CalibrationData.Remove(first);
            CalibrationData.Add(first with { Input = zero });
        }
    }

    /// <summary>
    /// Calculates the derivatives (slopes) for each data point, used in the interpolation.
    /// This method ensures the interpolation is both smooth and preserves the monotonicity of the data set.
    /// </summary>
    private void CalculateSlopes()
    {
        Datum[] data = [..CalibrationData.Select(p => new Datum(p.Input, p.Output, 0))];
        if (data.Length < 2)
        {
            points = data;
            return;
        }

        double[] delta = new double[data.Length - 1];

        for (int i = 0; i < delta.Length; i++)
        {
            var left = data[i];
            var right = data[i + 1];
            delta[i] = (right.Output - left.Output) / (right.Input - left.Input);
        }

        data[0].Slope = delta[0];

        // Calculate and assign derivatives for the middle points
        for (int i = 1; i < delta.Length; i++)
        {
            var prev = data[i - 1];
            var curr = data[i];
            var next = data[i + 1];
            if (delta[i - 1] * delta[i] > 0) // Check if consecutive slopes have the same sign
            {
                double w1 = 2 * (next.Input - curr.Input) + (curr.Input - prev.Input);
                double w2 = (next.Input - curr.Input) + 2 * (curr.Input - prev.Input);
                data[i].Slope = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
            }
            else
                data[i].Slope = 0;
        }

        // Set the derivative for the last point
        data[^1].Slope = delta[^1];

        points = data;
    }

    /// <summary>
    /// Interpolates the Y value for a given X value using the PCHIP interpolation method.
    /// If the given X is outside of the range of provided points, this method extrapolates
    /// a Y value using the first derivative value if X is below the range,
    /// or the last derivative if X is above the range.
    /// </summary>
    /// <param name="x">The X value to interpolate.</param>
    /// <returns>The interpolated Y value at the specified X.</returns>
    public double Interpolate(double x)
    {
        if (points.Length == 1 && points[0].Input == x)
            return points[0].Output;
        if (points.Length < 2)
            return double.NaN; // Not enough points to interpolate

        // Handle extrapolation on the left side
        if (x < points[0].Input)
        {
            return points[0].Output + points[0].Slope * (x - points[0].Input); // Linear extrapolation using the first slope
        }

        // Handle extrapolation on the right side
        if (x >= points[^1].Input)
        {
            return points[^1].Output + points[^1].Slope * (x - points[^1].Input); // Linear extrapolation using the last slope
        }

        var left = points.Last(d => x >= d.Input);
        var right = points.First(p => x < p.Input);

        double span = right.Input - left.Input;

        double t = (x - left.Input) / span;

        // Hermite interpolation polynomials
        double h00 = (1 + 2 * t) * (1 - t) * (1 - t);
        double h10 = t * (1 - t) * (1 - t) * span;
        double h01 = t * t * (3 - 2 * t);
        double h11 = t * t * (t - 1) * span;

        return h00 * left.Output + h01 * right.Output + h10 * left.Slope + h11 * right.Slope;
    }

    public override double Execute(double input) => Interpolate(input);
}
