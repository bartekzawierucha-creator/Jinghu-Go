using System;
using System.Collections.Generic;
using JianghuGo.Client.Infrastructure;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace JianghuGo.Unity.Infrastructure
{
    /// <summary>
    /// ILocationSource over Unity's Input.location. Collects distinct fixes into a small ring buffer; stops the
    /// location service when the app is paused (battery) and restarts it on resume.
    /// iOS additionally needs "Location Usage Description" in Player Settings.
    /// </summary>
    public sealed class UnityLocationSource : MonoBehaviour, ILocationSource
    {
        private const int Capacity = 60;
        private readonly List<LocationFix> _buffer = new List<LocationFix>();
        private double _lastTimestamp;
        private bool _permissionRequested;

        public LocationStatus Status { get; private set; } = LocationStatus.Initializing;

        public IReadOnlyList<LocationFix> FixesSince(DateTimeOffset since) => _buffer.FindAll(f => f.CapturedAt > since);

        private void OnEnable() => StartService();

        private void OnApplicationPause(bool paused)
        {
            if (paused) Input.location.Stop();
            else StartService();
        }

        private void StartService()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                if (_permissionRequested)
                {
                    Status = LocationStatus.Denied;
                    return;
                }

                _permissionRequested = true;
                Permission.RequestUserPermission(Permission.FineLocation);
                Status = LocationStatus.Initializing;
                return;
            }
#endif
            if (!Input.location.isEnabledByUser)
            {
                Status = LocationStatus.Unavailable;
                return;
            }

            Input.location.Start(10f, 5f); // desired accuracy 10 m, update every 5 m
            Status = LocationStatus.Initializing;
        }

        private void Update()
        {
            switch (Input.location.status)
            {
                case LocationServiceStatus.Failed:
                    Status = LocationStatus.Unavailable;
                    return;
                case LocationServiceStatus.Stopped:
                    if (Status == LocationStatus.Initializing && _permissionRequested) StartService(); // permission dialog answered
                    return;
                case LocationServiceStatus.Initializing:
                    Status = LocationStatus.Initializing;
                    return;
            }

            Status = LocationStatus.Running;
            var d = Input.location.lastData;
            if (d.timestamp <= _lastTimestamp) return;
            _lastTimestamp = d.timestamp;
            _buffer.Add(new LocationFix(d.latitude, d.longitude, Math.Max(1, d.horizontalAccuracy),
                DateTimeOffset.FromUnixTimeMilliseconds((long)(d.timestamp * 1000))));
            if (_buffer.Count > Capacity) _buffer.RemoveAt(0);
        }
    }
}
