namespace JianghuGo.Domain;

/// <summary>WGS-84 coordinate. Validated on construction via <see cref="TryCreate"/>.</summary>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    public static bool IsValid(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    public static GeoPoint? TryCreate(double lat, double lon) => IsValid(lat, lon) ? new GeoPoint(lat, lon) : null;
}

public static class GeoMath
{
    /// <summary>Mean Earth radius (IUGG); the same sphere PostGIS uses for ST_Distance(geography, geography, false).</summary>
    public const double EarthRadiusM = 6371008.7714;

    public const double MetersPerDegreeLat = Math.PI * EarthRadiusM / 180;

    /// <summary>Great-circle distance in metres (haversine). Verified against PostGIS in the database test suite.</summary>
    public static double DistanceM(GeoPoint a, GeoPoint b)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLon = Rad(b.Lon - a.Lon);
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) + (Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Degrees of longitude spanned by <paramref name="meters"/> at a latitude (clamped near the poles).</summary>
    public static double MetersToLonDegrees(double meters, double lat) =>
        meters / (MetersPerDegreeLat * Math.Max(0.01, Math.Cos(lat * Math.PI / 180)));

    public static double MetersToLatDegrees(double meters) => meters / MetersPerDegreeLat;
}

/// <summary>Stable hashing of coordinates/time buckets into RNG seeds (SplitMix64 finaliser).</summary>
public static class SeedMix
{
    public static ulong Mix(ulong seed, params long[] parts)
    {
        var h = seed;
        foreach (var p in parts)
        {
            h ^= unchecked((ulong)p) + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
            h = Finalise(h);
        }

        return h;
    }

    private static ulong Finalise(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
