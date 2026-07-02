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
        StartLevel,
        SyncAbort,
        LiveStats,
        ForceRestart,
    }

    /// <summary>Periodic (unreliable) in-run snapshot for the roster overlay + ghost markers.</summary>
    public class LiveStatsMsg
    {
        [JsonProperty("k")] public string Key;
        [JsonProperty("p")] public float Progress;
        [JsonProperty("a")] public float Accuracy;
        [JsonProperty("d")] public bool Dead;
        [JsonProperty("s")] public int SeqId;
        [JsonProperty("t")] public double SongPos;
    }

    /// <summary>Death-sync room rule: someone died, everyone restarts together.</summary>
    public class ForceRestartMsg
    {
        [JsonProperty("k")] public string Key;
    }

    /// <summary>Initiator → all: load this level and hold at the start gate.</summary>
    public class StartLevelMsg
    {
        [JsonProperty("k")] public string Key;
        [JsonProperty("d")] public string Display;
        [JsonProperty("cp")] public int Checkpoint;
        /// <summary>Custom levels: the level's folder name + .adofai file name, so peers can find their copy.</summary>
        [JsonProperty("f")] public string Folder;
        [JsonProperty("fn")] public string File;
    }

    public class LevelReadyMsg
    {
        [JsonProperty("k")] public string Key;
    }

    public class CountdownMsg
    {
        [JsonProperty("ms")] public int DelayMs;
    }

    public class SyncAbortMsg
    {
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
