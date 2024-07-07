using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AeonHacs
{
    /// <summary>
    /// Provides Piecewise Cubic Hermite Interpolating Polynomial (PCHIP) interpolation for a set of data.
    /// This class uses a monotonic interpolation method that preserves the shape of the data and avoids overshoots.
    /// </summary>
    public class PchipInterpolator : BindableObject
    {
        /// <summary>
        /// List of (double X, double Y) tuples of calibration data points, where
        /// X is a scale output and Y is the corresponding true kilograms.
        /// </summary>
        [JsonProperty]
        public List<(double X, double Y)> CalibrationData
        {
            get => calibrationData;
            set
            {
                calibrationData = value;
                Initialize(calibrationData);
            }
        }
        List<(double X, double Y)> calibrationData;

        private List<(double X, double Y, double D)> points;

        /// <summary>
        /// Creates a new instance of the <see cref="PchipInterpolator"/> class.
        /// </summary>
        public PchipInterpolator() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="PchipInterpolator"/> class.
        /// </summary>
        /// <param name="dataPoints">The data points for interpolation. Each point must have an X and Y value.</param>
        /// <exception cref="ArgumentException">Thrown when fewer than two data points are provided.</exception>
        public PchipInterpolator(List<(double X, double Y)> dataPoints)
        {
            Initialize(dataPoints);
        }

        private void Initialize(List<(double X, double Y)> dataPoints)
        {
            if (dataPoints == null || dataPoints.Count < 2)
                throw new ArgumentException("At least two points are required for interpolation.", nameof(dataPoints));

            // Sort by X and initialize derivative (D) at 0.0
            points = dataPoints
                .OrderBy(p => p.X)
                .Select(p => (p.X, p.Y, D: 0.0))
                .ToList();

            CalculateSlopes();
        }

        /// <summary>
        /// Calculates the derivatives (slopes) for each data point, used in the interpolation.
        /// This method ensures the interpolation is both smooth and preserves the monotonicity of the data set.
        /// </summary>
        private void CalculateSlopes()
        {
            int n = points.Count;
            double[] delta = new double[n - 1];

            // Calculate initial slopes between each pair of points
            for (int i = 0; i < n - 1; i++)
            {
                delta[i] = (points[i + 1].Y - points[i].Y) / (points[i + 1].X - points[i].X);
            }

            // Set the derivative for the first point
            points[0] = (points[0].X, points[0].Y, delta[0]);

            // Calculate and assign derivatives for the middle points
            for (int i = 1; i < n - 1; i++)
            {
                double d = 0;
                if (delta[i - 1] * delta[i] > 0) // Check if consecutive slopes have the same sign
                {
                    double w1 = 2 * (points[i + 1].X - points[i].X) + (points[i].X - points[i - 1].X);
                    double w2 = (points[i + 1].X - points[i].X) + 2 * (points[i].X - points[i - 1].X);
                    d = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
                }
                points[i] = (points[i].X, points[i].Y, d);
            }

            // Set the derivative for the last point
            points[n - 1] = (points[n - 1].X, points[n - 1].Y, delta[n - 2]);
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
            if (points.Count < 2)
                return double.NaN; // Not enough points to interpolate

            // Handle extrapolation on the left side
            if (x < points.First().X)
            {
                var first = points.First();
                double slope = first.D; // Derivative at the first point
                return first.Y + slope * (x - first.X); // Linear extrapolation using the first slope
            }

            // Handle extrapolation on the right side
            if (x > points.Last().X)
            {
                var last = points.Last();
                double slope = last.D; // Derivative at the last point
                return last.Y + slope * (x - last.X); // Linear extrapolation using the last slope
            }

            var left = points.LastOrDefault(p => x >= p.X);
            var right = points.FirstOrDefault(p => x < p.X);
            if (right.Equals(default) || right.X == left.X)
                right = left;

            double span = right.X - left.X;
            if (span == 0)
                return left.Y;

            double t = (x - left.X) / span;

            // Hermite interpolation polynomials
            double h00 = (1 + 2 * t) * (1 - t) * (1 - t);
            double h10 = t * (1 - t) * (1 - t) * span;
            double h01 = t * t * (3 - 2 * t);
            double h11 = t * t * (t - 1) * span;

            return h00 * left.Y + h01 * right.Y + h10 * left.D + h11 * right.D;
        }
    }
}
