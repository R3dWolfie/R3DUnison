using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace R3DUnison.Transport
{
    /// <summary>
    /// ITransport over SteamNetworkingMessages: session-based P2P, sessions open on
    /// demand when the first message is sent. Incoming sessions are accepted only from
    /// peers in the current room (UpdateTopology). All calls/events on the main thread —
    /// the game's own SteamAPI.RunCallbacks pump drives the callbacks, and polling
    /// happens on MainThreadDispatcher.OnFrame.
    /// </summary>
    public class SteamP2PTransport : ITransport
    {
        private const int Channel = 0;
        private const int MaxMessagesPerPoll = 64;

        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;
        private readonly IntPtr[] _msgPtrs = new IntPtr[MaxMessagesPerPoll];
        private readonly HashSet<ulong> _allowedPeers = new HashSet<ulong>();
        private bool _disposed;

        public bool IsHost { get; private set; }
        public ulong LocalPeerId { get; }

        public event Action<ulong> PeerConnected;
#pragma warning disable 0067 // raised by session connection-loss detection, which lands in M2
        public event Action<ulong> PeerDisconnected;
#pragma warning restore 0067
        public event Action<ulong, byte[]> MessageReceived;

        public SteamP2PTransport()
        {
            LocalPeerId = SteamUser.GetSteamID().m_SteamID;
            _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            Core.MainThreadDispatcher.OnFrame += Poll;
        }

        /// <summary>Call when room membership changes. Peers = everyone in the room (self is filtered out).</summary>
        public void UpdateTopology(bool isHost, IEnumerable<ulong> peers)
        {
            IsHost = isHost;
            _allowedPeers.Clear();
            foreach (var p in peers)
            {
                if (p != LocalPeerId)
                {
                    _allowedPeers.Add(p);
                }
            }
        }

        public void Send(ulong peerId, byte[] payload, SendMode mode)
        {
            if (_disposed) return;
            var identity = default(SteamNetworkingIdentity);
            identity.SetSteamID64(peerId);
            int flags = mode == SendMode.Reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
            var handle = GCHandle.Alloc(payload, GCHandleType.Pinned);
            try
            {
                var result = SteamNetworkingMessages.SendMessageToUser(
                    ref identity, handle.AddrOfPinnedObject(), (uint)payload.Length, flags, Channel);
                if (result != EResult.k_EResultOK)
                {
                    Main.Log($"Send to {peerId} returned {result}");
                }
            }
            finally
            {
                handle.Free();
            }
        }

        public void Broadcast(byte[] payload, SendMode mode)
        {
            foreach (var peer in _allowedPeers)
            {
                Send(peer, payload, mode);
            }
        }

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t request)
        {
            var identity = request.m_identityRemote;
            ulong id = identity.GetSteamID64();
            if (_allowedPeers.Contains(id))
            {
                SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
                PeerConnected?.Invoke(id);
            }
            else
            {
                Main.Log($"Rejected P2P session from {id} (not in this room)");
            }
        }

        private void Poll()
        {
            if (_disposed) return;
            int count;
            do
            {
                count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _msgPtrs, MaxMessagesPerPoll);
                for (int i = 0; i < count; i++)
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_msgPtrs[i]);
                    ulong from = msg.m_identityPeer.GetSteamID64();
                    byte[] data = null;
                    if (msg.m_cbSize > 0 && _allowedPeers.Contains(from))
                    {
                        data = new byte[msg.m_cbSize];
                        Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
                    }
                    SteamNetworkingMessage_t.Release(_msgPtrs[i]);
                    if (data != null)
                    {
                        MessageReceived?.Invoke(from, data);
                    }
                }
            } while (count == MaxMessagesPerPoll);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Core.MainThreadDispatcher.OnFrame -= Poll;
            _sessionRequest.Dispose();
            foreach (var peer in _allowedPeers)
            {
                var identity = default(SteamNetworkingIdentity);
                identity.SetSteamID64(peer);
                SteamNetworkingMessages.CloseSessionWithUser(ref identity);
            }
            _allowedPeers.Clear();
        }
    }
}
