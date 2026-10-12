using JianghuGo.Client.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace JianghuGo.Unity.Infrastructure
{
    public sealed class NewtonsoftJsonSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTimeOffset,
        };

        public string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
        public T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
