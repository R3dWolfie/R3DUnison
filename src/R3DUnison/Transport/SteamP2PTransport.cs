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

        private static bool _networkingConfigured;

        public SteamP2PTransport()
        {
            LocalPeerId = SteamUser.GetSteamID().m_SteamID;
            _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            Core.MainThreadDispatcher.OnFrame += Poll;
            ConfigureNetworking();
        }

        // Raise Steam's per-connection send buffer + rate cap so level transfers aren't
        // throttled to a few chunks per round-trip (the default 512 KB buffer is why TUF
        // downloads crawled). Global config, applied once.
        private static void ConfigureNetworking()
        {
            if (_networkingConfigured) return;
            _networkingConfigured = true;
            SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, 8 * 1024 * 1024);
            SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, 30 * 1024 * 1024);
        }

        private static void SetGlobalInt(ESteamNetworkingConfigValue key, int value)
        {
            IntPtr p = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(p, value);
                SteamNetworkingUtils.SetConfigValue(
                    key,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                    IntPtr.Zero,
                    ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    p);
            }
            catch (Exception e)
            {
                Main.Log($"Networking config {key} failed: {e.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>Call when room membership changes. Peers = everyone in the room (self is filtered out).</summary>
        public void UpdateTopology(bool isHost, IEnumerable<ulong> peers)
        {
            IsHost = isHost;
            var next = new HashSet<ulong>();
            foreach (var p in peers)
            {
                if (p != LocalPeerId) next.Add(p);
            }
            // Close Steam messaging sessions for peers who left, so they don't leak until Dispose.
            foreach (var gone in _allowedPeers)
            {
                if (!next.Contains(gone))
                {
                    var identity = default(SteamNetworkingIdentity);
                    identity.SetSteamID64(gone);
                    try { SteamNetworkingMessages.CloseSessionWithUser(ref identity); } catch { }
                }
            }
            _allowedPeers.Clear();
            foreach (var p in next) _allowedPeers.Add(p);
        }

        public bool Send(ulong peerId, byte[] payload, SendMode mode)
        {
            if (_disposed) return false;
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
                    // k_EResultLimitExceeded = send buffer full → caller should back off and retry.
                    if (result != EResult.k_EResultLimitExceeded) Main.Log($"Send to {peerId} returned {result}");
                    return false;
                }
                return true;
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
                        // A handler may have disposed us (e.g. LeaveRoom) mid-batch — stop
                        // before re-calling ReceiveMessagesOnChannel on a torn-down transport.
                        if (_disposed) return;
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
