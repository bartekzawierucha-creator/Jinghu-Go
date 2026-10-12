// API-surface stubs of UnityEngine for type-checking only. Bodies throw: nothing here is ever executed.
#pragma warning disable CA1050, CA1034, CA1051, CA1707, CA1716, CA1720, CA2211, CS0626
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }

    public class Object
    {
        public static void Destroy(Object obj) => throw new NotSupportedException();
        public static T FindFirstObjectByType<T>() where T : Object => throw new NotSupportedException();
        public static implicit operator bool(Object o) => o is not null;
    }

    public class Component : Object
    {
        public GameObject gameObject => throw new NotSupportedException();
        public Transform transform => throw new NotSupportedException();
        public T GetComponent<T>() => throw new NotSupportedException();
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name, params Type[] components) => throw new NotSupportedException();
        public Transform transform => throw new NotSupportedException();
        public T GetComponent<T>() => throw new NotSupportedException();
        public T AddComponent<T>() where T : Component => throw new NotSupportedException();
    }

    public class Transform : Component, IEnumerable
    {
        public void SetParent(Transform parent, bool worldPositionStays) => throw new NotSupportedException();
        public IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
    }

    public struct Vector2
    {
        public Vector2(float x, float y) => throw new NotSupportedException();
        public static Vector2 zero => default;
        public static Vector2 one => default;
    }

    public struct Color
    {
        public Color(float r, float g, float b) => throw new NotSupportedException();
        public Color(float r, float g, float b, float a) => throw new NotSupportedException();
        public static Color white => default;
    }

    public sealed class RectOffset { public RectOffset(int left, int right, int top, int bottom) => throw new NotSupportedException(); }
    public sealed class Font : Object { }
    public static class Resources { public static T GetBuiltinResource<T>(string path) where T : Object => throw new NotSupportedException(); }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public sealed class Canvas : Behaviour { public RenderMode renderMode { get; set; } }
    public enum TextAnchor { UpperLeft, MiddleCenter }
    public enum HorizontalWrapMode { Wrap, Overflow }

    public static class PlayerPrefs
    {
        public static string GetString(string key, string defaultValue) => throw new NotSupportedException();
        public static void SetString(string key, string value) => throw new NotSupportedException();
        public static void DeleteKey(string key) => throw new NotSupportedException();
        public static void Save() => throw new NotSupportedException();
    }

    public static class Debug { public static bool isDebugBuild => throw new NotSupportedException(); }
    public static class Application { public static int targetFrameRate { get; set; } }

    public enum LocationServiceStatus { Stopped, Initializing, Running, Failed }

    public struct LocationInfo
    {
        public float latitude => throw new NotSupportedException();
        public float longitude => throw new NotSupportedException();
        public float horizontalAccuracy => throw new NotSupportedException();
        public double timestamp => throw new NotSupportedException();
    }

    public sealed class LocationService
    {
        public bool isEnabledByUser => throw new NotSupportedException();
        public LocationServiceStatus status => throw new NotSupportedException();
        public LocationInfo lastData => throw new NotSupportedException();
        public void Start(float desiredAccuracyInMeters, float updateDistanceInMeters) => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
    }

    public static class Input { public static LocationService location => throw new NotSupportedException(); }
}

namespace UnityEngine.Android
{
    public static class Permission
    {
        public const string FineLocation = "android.permission.ACCESS_FINE_LOCATION";
        public static bool HasUserAuthorizedPermission(string permission) => throw new NotSupportedException();
        public static void RequestUserPermission(string permission) => throw new NotSupportedException();
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
}

namespace UnityEngine.EventSystems
{
    public sealed class EventSystem : Behaviour { }
    public sealed class StandaloneInputModule : Behaviour { }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;

    public class Graphic : Behaviour
    {
        public Color color { get; set; }
        public RectTransform rectTransform => throw new NotSupportedException();
    }

    public sealed class Image : Graphic { }

    public sealed class Text : Graphic
    {
        public Font font { get; set; }
        public int fontSize { get; set; }
        public string text { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public TextAnchor alignment { get; set; }
    }

    public class Selectable : Behaviour { public bool interactable { get; set; } }

    public sealed class Button : Selectable
    {
        public sealed class ButtonClickedEvent { public void AddListener(UnityAction call) => throw new NotSupportedException(); }
        public ButtonClickedEvent onClick => throw new NotSupportedException();
    }

    public sealed class InputField : Selectable
    {
        public enum ContentType { Standard, Password }
        public string text { get; set; }
        public Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public ContentType contentType { get; set; }
    }

    public sealed class LayoutElement : Behaviour { public float minHeight { get; set; } }

    public sealed class VerticalLayoutGroup : Behaviour
    {
        public RectOffset padding { get; set; }
        public float spacing { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childControlHeight { get; set; }
        public bool childControlWidth { get; set; }
    }

    public sealed class CanvasScaler : Behaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }

    public sealed class GraphicRaycaster : Behaviour { }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable
    {
        public string text => throw new NotSupportedException();
        public void Dispose() => throw new NotSupportedException();
    }

    public sealed class DownloadHandlerBuffer : DownloadHandler { }

    public class UploadHandler : IDisposable { public void Dispose() => throw new NotSupportedException(); }

    public sealed class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] data) => throw new NotSupportedException(); }

    public sealed class UnityWebRequestAsyncOperation { public bool isDone => throw new NotSupportedException(); }

    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public UnityWebRequest(string url, string method) => throw new NotSupportedException();
        public DownloadHandler downloadHandler { get; set; }
        public UploadHandler uploadHandler { get; set; }
        public int timeout { get; set; }
        public Result result => throw new NotSupportedException();
        public long responseCode => throw new NotSupportedException();
        public void SetRequestHeader(string name, string value) => throw new NotSupportedException();
        public UnityWebRequestAsyncOperation SendWebRequest() => throw new NotSupportedException();
        public void Abort() => throw new NotSupportedException();
        public void Dispose() => throw new NotSupportedException();
    }
}
