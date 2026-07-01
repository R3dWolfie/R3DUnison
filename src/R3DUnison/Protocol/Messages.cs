using Newtonsoft.Json;

namespace R3DUnison.Protocol
{
    /// <summary>
    /// Wire messages (JSON for now; can swap to binary later behind Codec).
    /// Versioned so old clients fail loud, not weird.
    /// </summary>
    public static class ProtocolInfo
    {
        public const int Version = 1;
    }

    public enum MessageType
    {
        Hello,
        RoomState,
        LevelInfo,
        Ready,
        CountdownStart,
        PlayerEvent,
        PlayerDeath,
        Results,
    }

    public class Envelope
    {
        [JsonProperty("v")] public int ProtocolVersion = ProtocolInfo.Version;
        [JsonProperty("t")] public MessageType Type;
        [JsonProperty("p")] public string Payload;
    }

    /// <summary>
    /// The core ghost-race message: one hit, stamped with *song* time.
    /// Peers render ghosts by song time, so network jitter moves the ghost, never your rhythm.
    /// </summary>
    public class PlayerEvent
    {
        [JsonProperty("st")] public double SongTimeMs;
        [JsonProperty("ti")] public int TileIndex;
        [JsonProperty("j")] public int Judgement;
        [JsonProperty("acc")] public float Accuracy;
        [JsonProperty("c")] public int Combo;
    }
}
