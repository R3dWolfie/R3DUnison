using System;

namespace R3DUnison.Transport
{
    /// <summary>
    /// The relay-ready seam. M1 implements this over Steam lobbies + SteamNetworkingSockets
    /// (host-relayed star: clients talk to the host, the host fans out). A WebSocket relay
    /// implementation can be added later without touching Session/Protocol.
    /// Implementations must raise all events on the Unity main thread (via MainThreadDispatcher).
    /// </summary>
    public interface ITransport : IDisposable
    {
        bool IsHost { get; }
        ulong LocalPeerId { get; }

        event Action<ulong> PeerConnected;
        event Action<ulong> PeerDisconnected;
        event Action<ulong, byte[]> MessageReceived;

        void Send(ulong peerId, byte[] payload, SendMode mode);

        /// <summary>Host: send to all peers. Client: send to host.</summary>
        void Broadcast(byte[] payload, SendMode mode);
    }

    public enum SendMode
    {
        /// <summary>Ordered + guaranteed. Room state, level info, ready/countdown, results.</summary>
        Reliable,
        /// <summary>Fire-and-forget. High-rate per-hit player events; a lost one is superseded by the next.</summary>
        Unreliable,
    }
}
