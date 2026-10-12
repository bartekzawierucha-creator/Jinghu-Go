using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Infrastructure
{
    /// <summary>Raw HTTP response. StatusCode 0 means the request never got a response (network failure).</summary>
    public sealed class HttpResult
    {
        public HttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public int StatusCode { get; }
        public string Body { get; }
        public bool IsNetworkError => StatusCode == 0;
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>Engine-specific HTTP. Unity: UnityWebRequestTransport. Tests/editor tools: HttpClientTransport.</summary>
    public interface IHttpTransport
    {
        Task<HttpResult> SendAsync(string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
    }

    /// <summary>Serializer seam: Unity uses Newtonsoft (com.unity.nuget.newtonsoft-json); tests use System.Text.Json.</summary>
    public interface IJsonSerializer
    {
        string Serialize(object value);
        T Deserialize<T>(string json);
    }

    /// <summary>
    /// Persists the refresh token between app launches. Production implementations must use the OS keystore
    /// (Android Keystore / iOS Keychain); PlayerPrefs is plain text and only acceptable for development.
    /// </summary>
    public interface ITokenStore
    {
        string LoadRefreshToken();
        void SaveRefreshToken(string token);
        void Clear();
    }

    public sealed class InMemoryTokenStore : ITokenStore
    {
        private string _token;
        public string LoadRefreshToken() => _token;
        public void SaveRefreshToken(string token) => _token = token;
        public void Clear() => _token = null;
    }
}
