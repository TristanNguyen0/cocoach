namespace CoCoach.Analysis.Track;

/// <summary>A WGS-84 point that anchors a local east-north-up frame.</summary>
public sealed record GeoOrigin(double Lat, double Lon, double Alt);

/// <summary>WGS-84 geodetic to local east-north-up (ENU) conversion, in metres.</summary>
public static class Geodesy
{
    const double A = 6378137.0;                 // semi-major axis
    const double F = 1 / 298.257223563;         // flattening
    const double E2 = F * (2 - F);              // first eccentricity squared

    /// <summary>Converts a geodetic point to east, north, up metres relative to <paramref name="origin"/>.</summary>
    public static (double East, double North, double Up) ToEnu(GeoOrigin origin, double lat, double lon, double alt)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var (x, y, z) = ToEcef(lat, lon, alt);
        var (x0, y0, z0) = ToEcef(origin.Lat, origin.Lon, origin.Alt);
        double dx = x - x0, dy = y - y0, dz = z - z0;

        var phi = Deg(origin.Lat);
        var lam = Deg(origin.Lon);
        double sinPhi = Math.Sin(phi), cosPhi = Math.Cos(phi), sinLam = Math.Sin(lam), cosLam = Math.Cos(lam);

        var east = -sinLam * dx + cosLam * dy;
        var north = -sinPhi * cosLam * dx - sinPhi * sinLam * dy + cosPhi * dz;
        var up = cosPhi * cosLam * dx + cosPhi * sinLam * dy + sinPhi * dz;
        return (east, north, up);
    }

    /// <summary>Inverse of <see cref="ToEnu"/> (iterative latitude; used to generate test tracks).</summary>
    public static (double Lat, double Lon, double Alt) FromEnu(GeoOrigin origin, double east, double north, double up)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var phi0 = Deg(origin.Lat);
        var lam0 = Deg(origin.Lon);
        double sinPhi = Math.Sin(phi0), cosPhi = Math.Cos(phi0), sinLam = Math.Sin(lam0), cosLam = Math.Cos(lam0);

        var dx = -sinLam * east - sinPhi * cosLam * north + cosPhi * cosLam * up;
        var dy = cosLam * east - sinPhi * sinLam * north + cosPhi * sinLam * up;
        var dz = cosPhi * north + sinPhi * up;
        var (x0, y0, z0) = ToEcef(origin.Lat, origin.Lon, origin.Alt);
        double x = x0 + dx, y = y0 + dy, z = z0 + dz;

        var lon = Math.Atan2(y, x);
        var p = Math.Sqrt(x * x + y * y);
        var lat = Math.Atan2(z, p * (1 - E2));
        double alt = 0;
        for (var i = 0; i < 8; i++)
        {
            var sin = Math.Sin(lat);
            var n = A / Math.Sqrt(1 - E2 * sin * sin);
            alt = p / Math.Cos(lat) - n;
            lat = Math.Atan2(z, p * (1 - E2 * n / (n + alt)));
        }
        return (lat * 180 / Math.PI, lon * 180 / Math.PI, alt);
    }

    static (double X, double Y, double Z) ToEcef(double lat, double lon, double alt)
    {
        var phi = Deg(lat);
        var lam = Deg(lon);
        var sin = Math.Sin(phi);
        var n = A / Math.Sqrt(1 - E2 * sin * sin);
        var x = (n + alt) * Math.Cos(phi) * Math.Cos(lam);
        var y = (n + alt) * Math.Cos(phi) * Math.Sin(lam);
        var z = (n * (1 - E2) + alt) * sin;
        return (x, y, z);
    }

    static double Deg(double degrees) => degrees * Math.PI / 180;
}
