using System;
using System.Collections.Generic;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using JianghuGo.Client.Infrastructure;

namespace JianghuGo.Android
{
    /// <summary>System.Text.Json with web defaults (camelCase, case-insensitive) — the serializer the client tests use.</summary>
    public sealed class StjSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public string Serialize(object value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// DEVELOPMENT ONLY: app-private SharedPreferences (not encrypted). Before release replace with an Android
    /// Keystore-backed store — tracked in docs/security/threat-model.md (T-ACC-4).
    /// </summary>
    public sealed class PrefsTokenStore : ITokenStore
    {
        private readonly ISharedPreferences _prefs;
        private readonly string Key;

        public PrefsTokenStore(ISharedPreferences prefs, string key)
        {
            _prefs = prefs;
            Key = key;
        }

        public string LoadRefreshToken()
        {
            var value = _prefs.GetString(Key, null);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public void SaveRefreshToken(string token)
        {
            using var e = _prefs.Edit();
            e.PutString(Key, token);
            e.Apply();
        }

        public void Clear()
        {
            using var e = _prefs.Edit();
            e.Remove(Key);
            e.Apply();
        }
    }

    /// <summary>
    /// ILocationSource over the platform LocationManager (GPS + network). Keeps distinct fixes in a small ring buffer.
    /// Started only while the world screen is open and the app is in the foreground (battery); the position is sent
    /// to the server only through the exploration flow — never logged or stored on the device.
    /// </summary>
    public sealed class AndroidLocationSource : Java.Lang.Object, ILocationListener, ILocationSource
    {
        public const int PermissionRequestCode = 7;
        private const int Capacity = 60;
        private readonly object _gate = new object();
        private readonly List<LocationFix> _buffer = new List<LocationFix>();
        private readonly Activity _activity;
        private LocationManager _manager;
        private long _lastTime;
        private bool _listening;
        private bool _permissionAsked;

        public AndroidLocationSource(Activity activity) => _activity = activity;

        public LocationStatus Status { get; private set; } = LocationStatus.Initializing;

        /// <summary>The most recent fix (for drawing the player on the map), or null.</summary>
        public LocationFix Last
        {
            get
            {
                lock (_gate) return _buffer.Count == 0 ? null : _buffer[_buffer.Count - 1];
            }
        }

        public event Action Updated;

        public IReadOnlyList<LocationFix> FixesSince(DateTimeOffset since)
        {
            lock (_gate) return _buffer.FindAll(f => f.CapturedAt > since);
        }

        public void Start()
        {
            if (_listening) return;
            if (_activity.CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
            {
                if (_permissionAsked)
                {
                    Status = LocationStatus.Denied;
                    return;
                }

                _permissionAsked = true;
                Status = LocationStatus.Initializing;
                _activity.RequestPermissions(new[] { global::Android.Manifest.Permission.AccessFineLocation, global::Android.Manifest.Permission.AccessCoarseLocation }, PermissionRequestCode);
                return;
            }

            _manager ??= (LocationManager)_activity.GetSystemService(Context.LocationService);
            var gps = _manager.IsProviderEnabled(LocationManager.GpsProvider);
            var network = _manager.IsProviderEnabled(LocationManager.NetworkProvider);
            if (!gps && !network)
            {
                Status = LocationStatus.Unavailable;
                return;
            }

            if (gps) _manager.RequestLocationUpdates(LocationManager.GpsProvider, 3000, 3f, this);
            if (network) _manager.RequestLocationUpdates(LocationManager.NetworkProvider, 5000, 5f, this);
            _listening = true;
            if (Status != LocationStatus.Running) Status = LocationStatus.Initializing;
        }

        public void Stop()
        {
            if (!_listening) return;
            _manager?.RemoveUpdates(this);
            _listening = false;
        }

        /// <summary>Called by the activity with the answer to the permission dialog.</summary>
        public void OnPermissionResult(bool granted)
        {
            if (granted) Start();
            else Status = LocationStatus.Denied;
            Updated?.Invoke();
        }

        public void OnLocationChanged(Location location)
        {
            if (location == null) return;
            lock (_gate)
            {
                if (location.Time <= _lastTime) return;
                _lastTime = location.Time;
                var accuracy = location.HasAccuracy ? Math.Max(1.0, location.Accuracy) : 50.0;
                _buffer.Add(new LocationFix(location.Latitude, location.Longitude, accuracy, DateTimeOffset.FromUnixTimeMilliseconds(location.Time)));
                if (_buffer.Count > Capacity) _buffer.RemoveAt(0);
            }

            Status = LocationStatus.Running;
            Updated?.Invoke();
        }

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        public void OnStatusChanged(string provider, Availability status, Bundle extras)
        {
        }
    }
}
