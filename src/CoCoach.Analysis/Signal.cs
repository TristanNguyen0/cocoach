namespace CoCoach.Analysis;

/// <summary>Small deterministic signal helpers for traces sampled on a fixed distance grid.</summary>
internal static class Signal
{
    /// <summary>
    /// Centred moving average over <c>2 * halfWindow + 1</c> samples, ignoring NaN.
    /// <paramref name="circular"/> wraps around the ends, which suits a closed lap.
    /// </summary>
    public static double[] MovingAverage(IReadOnlyList<double> x, int halfWindow, bool circular)
    {
        var n = x.Count;
        var result = new double[n];
        if (n == 0)
            return result;
        if (halfWindow <= 0)
        {
            for (var i = 0; i < n; i++)
                result[i] = x[i];
            return result;
        }

        // running sums over a window that slides one sample at a time
        double sum = 0;
        var count = 0;
        void Add(int j, int sign)
        {
            if (circular)
                j = Mod(j, n);
            else if (j < 0 || j >= n)
                return;
            var v = x[j];
            if (double.IsNaN(v))
                return;
            sum += sign * v;
            count += sign;
        }

        for (var j = -halfWindow; j <= halfWindow; j++)
            Add(j, +1);
        for (var i = 0; i < n; i++)
        {
            result[i] = count > 0 ? sum / count : double.NaN;
            Add(i - halfWindow, -1);
            Add(i + halfWindow + 1, +1);
        }
        return result;
    }

    /// <summary>Two passes of <see cref="MovingAverage"/>: a triangular kernel, smoother than one box.</summary>
    public static double[] Smooth(IReadOnlyList<double> x, int halfWindow, bool circular) =>
        MovingAverage(MovingAverage(x, halfWindow, circular), halfWindow, circular);

    public static int Mod(int i, int n) => ((i % n) + n) % n;

    /// <summary>Median of the finite values, or NaN when there are none.</summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(double.IsFinite).Order().ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Value at quantile <paramref name="q"/> (0..1) of the finite values, linear between ranks.</summary>
    public static double Quantile(IEnumerable<double> values, double q)
    {
        var sorted = values.Where(double.IsFinite).Order().ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        var pos = Math.Clamp(q, 0, 1) * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>Wraps an angle to (-pi, pi].</summary>
    public static double WrapAngle(double a)
    {
        a %= 2 * Math.PI;
        if (a <= -Math.PI)
            a += 2 * Math.PI;
        else if (a > Math.PI)
            a -= 2 * Math.PI;
        return a;
    }
}
