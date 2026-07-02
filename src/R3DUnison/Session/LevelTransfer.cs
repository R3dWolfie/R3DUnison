using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using R3DUnison.Protocol;

namespace R3DUnison.Session
{
    /// <summary>
    /// P2P level delivery. When a peer lacks the custom level a synced start needs,
    /// it asks the host; the host zips its level folder (background thread, capped),
    /// offers it with a size, and on acceptance streams base64 chunks over the
    /// reliable channel with a small ack window. The peer installs into
    /// Documents/A Dance of Fire and Ice/Worlds/ and re-enters the synced-start flow.
    /// </summary>
    public static class LevelTransfer
    {
        private const int ChunkSize = 192 * 1024;          // ×4/3 base64 ≈ 256 KB < Steam's 512 KB message cap
        private const int SendWindow = 24;                 // ~6 MB in flight; fits the raised 8 MB send buffer
        private const long MaxLevelBytes = 250L * 1024 * 1024;
        private const float PeerStallTimeout = 25f;        // no chunk for this long → give up loudly

        // ---------------- host side ----------------

        private class PeerSend
        {
            public int NextChunk;
            public int Acked = -1;
            public bool Active;
        }

        private static byte[] _zipBytes;
        private static string _zipKey;
        private static bool _zipping;
        private static readonly HashSet<ulong> _waitingOffer = new HashSet<ulong>();
        private static readonly Dictionary<ulong, PeerSend> _sends = new Dictionary<ulong, PeerSend>();

        private static int TotalChunks => _zipBytes == null ? 0 : (_zipBytes.Length + ChunkSize - 1) / ChunkSize;

        public static bool HostBusy => _zipping || _sends.Values.Any(s => s.Active);

        public static string HostStatusSuffix
        {
            get
            {
                if (_zipping) return " · packing level…";
                var active = _sends.Values.Where(s => s.Active).ToList();
                if (active.Count == 0) return "";
                int total = TotalChunks;
                string pcts = string.Join(", ", active.Select(s => total == 0 ? "0%" : $"{100 * (s.Acked + 1) / total}%"));
                return $" · sending level ({pcts})";
            }
        }

        public static void HostReset()
        {
            _zipBytes = null;
            _zipKey = null;
            _zipping = false;
            _waitingOffer.Clear();
            _sends.Clear();
        }

        public static void OnLevelRequest(ulong from, LevelRequestMsg msg)
        {
            var rm = RoomManager.Instance;
            if (rm == null || msg?.Key == null || !SyncedStart.IsHostingKey(msg.Key)) return;
            string dir = SyncedStart.HostLevelDir;
            if (dir == null) return; // official level — nothing to send

            if (_zipBytes != null && _zipKey == msg.Key)
            {
                SendOffer(from);
                return;
            }
            _waitingOffer.Add(from);
            if (_zipping) return;

            _zipping = true;
            _zipKey = msg.Key;
            Main.Log($"[transfer] packing '{dir}' for peers");
            Task.Run(() =>
            {
                try
                {
                    long total = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    if (total > MaxLevelBytes)
                    {
                        throw new Exception($"level folder is {total / 1048576} MB (limit {MaxLevelBytes / 1048576} MB)");
                    }
                    using (var stream = new MemoryStream())
                    {
                        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            string baseDir = Path.GetDirectoryName(dir);
                            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                string rel = file.Substring(baseDir.Length + 1).Replace('\\', '/');
                                var entry = archive.CreateEntry(rel, CompressionLevel.Fastest);
                                using (var target = entry.Open())
                                using (var source = File.OpenRead(file))
                                {
                                    source.CopyTo(target);
                                }
                            }
                        }
                        var bytes = stream.ToArray();
                        Core.MainThreadDispatcher.Post(() =>
                        {
                            _zipping = false;
                            _zipBytes = bytes;
                            Main.Log($"[transfer] packed {bytes.Length / 1024} KB, offering to {_waitingOffer.Count} peer(s)");
                            foreach (var peer in _waitingOffer.ToList()) SendOffer(peer);
                            _waitingOffer.Clear();
                        });
                    }
                }
                catch (Exception e)
                {
                    Core.MainThreadDispatcher.Post(() =>
                    {
                        _zipping = false;
                        _waitingOffer.Clear();
                        Main.LogError($"[transfer] packing failed: {e.Message}");
                    });
                }
            });
        }

        private static void SendOffer(ulong peer)
        {
            _sends[peer] = new PeerSend();
            RoomManager.Instance?.SendToPeer(peer, MessageType.LevelOffer,
                new LevelOfferMsg { Key = _zipKey, Size = _zipBytes.Length, Chunks = TotalChunks });
        }

        public static void OnChunkAck(ulong from, ChunkAckMsg msg)
        {
            if (_zipBytes == null || msg?.Key != _zipKey || !_sends.TryGetValue(from, out var send)) return;
            if (msg.Index == -1)
            {
                send.Active = true;
            }
            else if (msg.Index > send.Acked)
            {
                send.Acked = msg.Index;
                if (send.Acked >= TotalChunks - 1)
                {
                    send.Active = false;
                    Main.Log($"[transfer] peer {from} finished downloading");
                }
            }
            SyncedStart.NotifyTransferActivity();
            PumpSend(from, send);
        }

        // Send up to the window; stop the instant a send fails (buffer full) and leave
        // NextChunk where it is — the next ack or the Tick retry pump picks it back up.
        private static void PumpSend(ulong peer, PeerSend send)
        {
            while (send.Active && send.NextChunk < TotalChunks && send.NextChunk <= send.Acked + SendWindow)
            {
                if (!SendChunk(peer, send.NextChunk)) break;
                send.NextChunk++;
            }
        }

        private static bool SendChunk(ulong peer, int index)
        {
            int offset = index * ChunkSize;
            int length = Math.Min(ChunkSize, _zipBytes.Length - offset);
            string data = Convert.ToBase64String(_zipBytes, offset, length);
            return RoomManager.Instance?.SendToPeer(peer, MessageType.LevelChunk,
                new LevelChunkMsg { Key = _zipKey, Index = index, Data = data }) == true;
        }

        public static void OnLevelDecline(ulong from, LevelDeclineMsg msg)
        {
            _waitingOffer.Remove(from);
            _sends.Remove(from);
            SyncedStart.PeerDeclined(from);
        }

        /// <summary>Per-frame: retry stalled host sends (buffer freed up) + peer-side stall timeout.</summary>
        public static void Tick()
        {
            // Host: re-pump any active send whose window has room but stopped on a full buffer.
            if (_zipBytes != null && _sends.Count > 0)
            {
                var rm = RoomManager.Instance;
                foreach (var kv in _sends.ToList())
                {
                    // Drop peers who left the room mid-send.
                    if (rm != null && rm.InRoom && !rm.Members.Any(m => m.Id == kv.Key))
                    {
                        _sends.Remove(kv.Key);
                        continue;
                    }
                    if (kv.Value.Active) PumpSend(kv.Key, kv.Value);
                }
            }

            // Peer: if a download went silent, fail loudly instead of hanging forever.
            if (_receiveBuffer != null && UnityEngine.Time.realtimeSinceStartup - _lastChunkAt > PeerStallTimeout)
            {
                Main.LogError("[transfer] download stalled — no data from host");
                var display = _pendingStart?.Display;
                PeerReset();
                PeerStatus = $"Download of '{display}' stalled — ask the host to start it again.";
            }
        }

        // ---------------- peer side ----------------

        private static StartLevelMsg _pendingStart;
        private static ulong _hostId;
        private static byte[] _receiveBuffer;
        private static int _expectedChunks, _receivedChunks;
        private static float _lastChunkAt;

        /// <summary>Non-null → show the download prompt.</summary>
        public static LevelOfferMsg PendingOffer { get; private set; }
        public static string PendingDisplay => _pendingStart?.Display;
        /// <summary>0..1 while downloading, -1 otherwise.</summary>
        public static float DownloadProgress { get; private set; } = -1f;
        public static string PeerStatus { get; private set; }

        public static void BeginPeerFlow(ulong hostId, StartLevelMsg msg)
        {
            _pendingStart = msg;
            _hostId = hostId;
            PendingOffer = null;
            DownloadProgress = -1f;
            PeerStatus = $"Asking host for '{msg.Display}'…";
            RoomManager.Instance?.SendToPeer(hostId, MessageType.LevelRequest, new LevelRequestMsg { Key = msg.Key });
        }

        public static void OnLevelOffer(ulong from, LevelOfferMsg msg)
        {
            if (_pendingStart == null || msg?.Key != _pendingStart.Key) return;
            PendingOffer = msg;
            PeerStatus = null;
        }

        public static void Accept()
        {
            if (PendingOffer == null) return;
            // Reject a bogus/oversized offer rather than allocating gigabytes or overflowing.
            if (PendingOffer.Size <= 0 || PendingOffer.Size > MaxLevelBytes || PendingOffer.Chunks <= 0)
            {
                PeerStatus = "That level transfer looks invalid — declined.";
                Main.LogError($"[transfer] rejected offer: size={PendingOffer.Size} chunks={PendingOffer.Chunks}");
                Decline();
                return;
            }
            _receiveBuffer = new byte[PendingOffer.Size];
            _expectedChunks = PendingOffer.Chunks;
            _receivedChunks = 0;
            _lastChunkAt = UnityEngine.Time.realtimeSinceStartup;
            DownloadProgress = 0f;
            var key = PendingOffer.Key;
            PendingOffer = null;
            RoomManager.Instance?.SendToPeer(_hostId, MessageType.ChunkAck, new ChunkAckMsg { Key = key, Index = -1 });
        }

        public static void Decline()
        {
            if (_pendingStart == null) return;
            RoomManager.Instance?.SendToPeer(_hostId, MessageType.LevelDecline, new LevelDeclineMsg { Key = _pendingStart.Key });
            PeerReset();
        }

        public static void OnChunk(ulong from, LevelChunkMsg msg)
        {
            if (_receiveBuffer == null || _pendingStart == null || msg?.Key != _pendingStart.Key || msg.Data == null) return;
            // Validate the attacker/mismatch-controlled index and length before copying —
            // a bad index or overlong chunk must not crash the per-frame message loop.
            if (msg.Index < 0 || msg.Index >= _expectedChunks) return;
            long offset = (long)msg.Index * ChunkSize;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(msg.Data);
            }
            catch (FormatException)
            {
                return;
            }
            if (offset < 0 || offset + bytes.Length > _receiveBuffer.Length) return;
            Buffer.BlockCopy(bytes, 0, _receiveBuffer, (int)offset, bytes.Length);
            _receivedChunks++;
            _lastChunkAt = UnityEngine.Time.realtimeSinceStartup;
            DownloadProgress = (float)_receivedChunks / _expectedChunks;
            RoomManager.Instance?.SendToPeer(from, MessageType.ChunkAck, new ChunkAckMsg { Key = msg.Key, Index = msg.Index });
            if (_receivedChunks >= _expectedChunks) Complete();
        }

        private static void Complete()
        {
            var start = _pendingStart;
            ulong host = _hostId;
            try
            {
                string worlds = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "A Dance of Fire and Ice", "Worlds");
                Directory.CreateDirectory(worlds);
                long extracted = 0;
                using (var stream = new MemoryStream(_receiveBuffer))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Length == 0) continue;
                        string rel = entry.FullName.Replace('\\', '/');
                        if (rel.Contains("..") || Path.IsPathRooted(rel)) continue; // path traversal guard
                        // Cap total decompressed size — a small zip must not fill the disk (zip bomb).
                        extracted += entry.Length;
                        if (extracted > MaxLevelBytes)
                        {
                            throw new Exception($"level unpacks to more than {MaxLevelBytes / 1048576} MB — refused");
                        }
                        string target = Path.Combine(worlds, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var source = entry.Open())
                        using (var output = File.Create(target))
                        {
                            source.CopyTo(output);
                        }
                    }
                }
                Main.Log($"[transfer] installed '{start.Display}' into Worlds");
                PeerReset();
                SyncedStart.OnStartLevel(host, start); // resolves now → loads, gates, reports ready
            }
            catch (Exception e)
            {
                _receiveBuffer = null;
                DownloadProgress = -1f;
                PeerStatus = $"Install failed: {e.Message}";
                Main.LogError($"[transfer] install failed: {e}");
            }
        }

        public static void PeerReset()
        {
            _pendingStart = null;
            PendingOffer = null;
            _receiveBuffer = null;
            DownloadProgress = -1f;
            PeerStatus = null;
        }
    }
}
