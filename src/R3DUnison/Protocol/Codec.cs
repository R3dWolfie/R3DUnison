using System;
using System.Text;
using Newtonsoft.Json;

namespace R3DUnison.Protocol
{
    /// <summary>JSON-over-UTF8 for now; swap the internals for binary later without touching callers.</summary>
    public static class Codec
    {
        public static byte[] Encode<T>(MessageType type, T payload)
        {
            var env = new Envelope
            {
                Type = type,
                Payload = JsonConvert.SerializeObject(payload),
            };
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(env));
        }

        public static Envelope Decode(byte[] data)
        {
            try
            {
                return JsonConvert.DeserializeObject<Envelope>(Encoding.UTF8.GetString(data));
            }
            catch (Exception e)
            {
                Main.LogError($"Failed to decode message ({data.Length} bytes): {e.Message}");
                return null;
            }
        }

        public static T Payload<T>(Envelope env) => JsonConvert.DeserializeObject<T>(env.Payload);
    }

    public class Hello
    {
        [JsonProperty("n")] public string Name;
    }
}
