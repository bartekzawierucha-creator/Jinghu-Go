using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Infrastructure
{
    public enum LocationStatus
    {
        Unavailable,   // device has no location service / disabled by the user in system settings
        Denied,        // the app was refused permission
        Initializing,
        Running,
    }

    public sealed class LocationFix
    {
        public LocationFix(double lat, double lon, double accuracyM, DateTimeOffset capturedAt)
        {
            Lat = lat;
            Lon = lon;
            AccuracyM = accuracyM;
            CapturedAt = capturedAt;
        }

        public double Lat { get; }
        public double Lon { get; }
        public double AccuracyM { get; }
        public DateTimeOffset CapturedAt { get; }
    }

    /// <summary>Device location. Unity: UnityLocationSource (Input.location). Tests: a scripted fake.</summary>
    public interface ILocationSource
    {
        LocationStatus Status { get; }

        /// <summary>Fixes captured after <paramref name="since"/> (oldest first). Implementations keep a small ring buffer.</summary>
        IReadOnlyList<LocationFix> FixesSince(DateTimeOffset since);
    }
}
