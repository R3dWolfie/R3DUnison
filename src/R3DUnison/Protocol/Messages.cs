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
        LevelRequest,
        LevelOffer,
        LevelChunk,
        ChunkAck,
        LevelDecline,
        RunResult,
        Chat,
    }

    /// <summary>Terminal result of a run (sent on level completion).</summary>
    public class RunResultMsg
    {
        [JsonProperty("k")] public string Key;
        [JsonProperty("a")] public float Acc;
    }

    public class ChatMsg
    {
        [JsonProperty("t")] public string Text;
    }

    // --- P2P level transfer (host streams its level folder to peers who lack it) ---

    public class LevelRequestMsg
    {
        [JsonProperty("k")] public string Key;
    }

    public class LevelOfferMsg
    {
        [JsonProperty("k")] public string Key;
        [JsonProperty("sz")] public long Size;
        [JsonProperty("c")] public int Chunks;
    }

    public class LevelChunkMsg
    {
        [JsonProperty("k")] public string Key;
        [JsonProperty("i")] public int Index;
        [JsonProperty("d")] public string Data; // base64
    }

    public class ChunkAckMsg
    {
        [JsonProperty("k")] public string Key;
        /// <summary>-1 = accept offer / start sending; otherwise highest received chunk.</summary>
        [JsonProperty("i")] public int Index;
    }

    public class LevelDeclineMsg
    {
        [JsonProperty("k")] public string Key;
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
        /// <summary>Menu presence ("menu:" keys): planet world position in the level-select scene.</summary>
        [JsonProperty("px")] public float PosX;
        [JsonProperty("py")] public float PosY;
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
        /// <summary>Chart speed for this round, snapshotted by the initiator (host-authoritative).</summary>
        [JsonProperty("sp")] public float Speed;
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
