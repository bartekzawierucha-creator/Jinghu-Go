using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JianghuGo.Client.Infrastructure;
using UnityEngine.Networking;

namespace JianghuGo.Unity.Infrastructure
{
    /// <summary>IHttpTransport over UnityWebRequest (works on Android/iOS, respects platform TLS).</summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;

        public UnityWebRequestTransport(string baseUrl, int timeoutSeconds = 15)
        {
            if (!baseUrl.StartsWith("https://", StringComparison.Ordinal) && !UnityEngine.Debug.isDebugBuild)
            {
                throw new ArgumentException("Release builds must talk to the server over HTTPS.", nameof(baseUrl));
            }

            _baseUrl = baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<HttpResult> SendAsync(string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            using (var request = new UnityWebRequest(_baseUrl + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = _timeoutSeconds;
                if (jsonBody != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                foreach (var h in headers)
                {
                    request.SetRequestHeader(h.Key, h.Value);
                }

                var operation = request.SendWebRequest();
                using (ct.Register(() => request.Abort()))
                {
                    while (!operation.isDone)
                    {
                        await Task.Yield(); // continues on Unity's main thread via UnitySynchronizationContext
                    }
                }

                ct.ThrowIfCancellationRequested();
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    return new HttpResult(0, string.Empty);
                }

                return new HttpResult((int)request.responseCode, request.downloadHandler.text ?? string.Empty);
            }
        }
    }
}
