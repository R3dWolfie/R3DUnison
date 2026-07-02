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
                ProtocolVersion = ProtocolInfo.Version,
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

        // Never throw out of the per-frame message loop: a malformed/empty payload from
        // any peer would otherwise kill every OnFrame subscriber for that frame.
        public static T Payload<T>(Envelope env)
        {
            try
            {
                if (env?.Payload == null) return default;
                return JsonConvert.DeserializeObject<T>(env.Payload);
            }
            catch (Exception e)
            {
                Main.LogError($"Bad payload for {typeof(T).Name}: {e.Message}");
                return default;
            }
        }
    }

    public class Hello
    {
        [JsonProperty("n")] public string Name;
        /// <summary>Planet colors (RGB hex, no #) so ghosts render in the player's real colors.</summary>
        [JsonProperty("c1")] public string Color1;
        [JsonProperty("c2")] public string Color2;
    }
}
