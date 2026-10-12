using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Infrastructure
{
    public sealed class HttpClientTransport : IHttpTransport
    {
        private readonly HttpClient _http;

        public HttpClientTransport(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<HttpResult> SendAsync(string method, string path, string jsonBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)))
            {
                foreach (var h in headers)
                {
                    request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }

                if (jsonBody != null)
                {
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                try
                {
                    using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        return new HttpResult((int)response.StatusCode, body);
                    }
                }
                catch (HttpRequestException)
                {
                    return new HttpResult(0, string.Empty);
                }
            }
        }
    }
}
