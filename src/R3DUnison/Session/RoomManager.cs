using System;
using System.Collections.Generic;
using System.Linq;
using R3DUnison.Protocol;
using R3DUnison.Transport;
using Steamworks;

namespace R3DUnison.Session
{
    public class MemberState
    {
        public ulong Id;
        public string Name;
        public bool IsSelf;
        public bool IsHost;
        /// <summary>True once a Hello made the round trip — actual P2P connectivity, not just lobby presence.</summary>
        public bool P2PConnected;

        // Live in-run stats (fed by LiveStats messages / local sampling)
        public float Progress;
        public float Accuracy;
        public bool Dead;
        public float StatsAt = -1000f;
        public int SeqId;
        public double SongPos;
        public string StatsKey;

        public bool HasFreshStats => UnityEngine.Time.realtimeSinceStartup - StatsAt < 3f;
    }

    /// <summary>
    /// Owns the current multiplayer session: lobby membership + P2P transport + roster.
    /// Steam objects are created lazily once the game's SteamIntegration reports ready
    /// (the mod loads before the game initializes Steam).
    /// </summary>
    public class RoomManager : IDisposable
    {
        public static RoomManager Instance { get; private set; }

        public bool SteamReady { get; private set; }
        public SteamLobby Lobby { get; private set; }
        public List<MemberState> Members { get; } = new List<MemberState>();
        public List<RoomInfo> AvailableRooms { get; } = new List<RoomInfo>();
        public string Status { get; private set; } = "";

        private SteamP2PTransport _transport;
        /// <summary>True while the current room was auto-created from level presence.</summary>
        public bool IsAutoRoom { get; private set; }

        public static void Init()
        {
            if (Instance == null) Instance = new RoomManager();
        }

        public static void Shutdown()
        {
            Instance?.Dispose();
            Instance = null;
        }

        private RoomManager()
        {
            Core.MainThreadDispatcher.OnFrame += Tick;
            Game.LevelTracker.Entered += OnLevelEntered;
            Game.LevelTracker.Exited += OnLevelExited;
        }

        private void Tick()
        {
            if (SteamReady)
            {
                // The game only pumps Steam callbacks in scnEditor/scnCLS/analytics —
                // never in the base-game flow — so we pump every frame ourselves.
                // Double-pumping where the game does too is harmless (queue drains once).
                SteamIntegration.instance?.CheckCallbacks();
                SyncedStart.Tick();
                TickLiveStats();
                Scoreboard.Tick();
                TickRoomSpeed();
                return;
            }
            if (!SteamIntegration.initialized) return;
            SteamReady = true;
            Lobby = new SteamLobby();
            Lobby.Entered += OnEntered;
            Lobby.Left += OnLeft;
            Lobby.MembersChanged += RefreshMembers;
            Lobby.RoomsListed += OnRoomsListed;
            Lobby.JoinFailed += message => Status = message;
            Main.Log("Steam ready — multiplayer available.");
        }

        public bool InRoom => Lobby != null && Lobby.InRoom;

        public void CreateRoom(string name)
        {
            Status = "Creating room…";
            Lobby.CreateRoom(name);
        }

        public void JoinRoom(RoomInfo room)
        {
            Status = $"Joining '{room.Name}'…";
            Lobby.JoinRoom(room.LobbyId);
        }

        public void RefreshRooms()
        {
            Status = "Searching for rooms…";
            Lobby.RefreshRooms();
        }

        public void LeaveRoom() => Lobby.Leave();

        public void InviteFriends() => Lobby.InviteOverlay();

        private void OnLevelEntered(Game.LevelPresence level)
        {
            if (!SteamReady) return;
            if (InRoom)
            {
                // Host playing something new: update what the room advertises
                Lobby.SetLevelInfo(level.Key, level.Display);
            }
            else if (Main.Settings.AutoAnnounce)
            {
                IsAutoRoom = true;
                _announcedLevel = level;
                Lobby.CreateRoom($"{SteamFriends.GetPersonaName()} · {level.Display}", auto: true);
            }
        }

        private Game.LevelPresence _announcedLevel;

        private void OnLevelExited()
        {
            SyncedStart.OnLocalLevelExited();
            if (!SteamReady || !InRoom) return;
            Lobby.SetLevelInfo(null, null);
            // An auto-room with nobody else in it dissolves when you stop playing;
            // if people joined, it stays alive as a normal room.
            if (IsAutoRoom && Members.Count <= 1)
            {
                LeaveRoom();
            }
        }

        private void OnEntered()
        {
            _transport?.Dispose();
            _transport = new SteamP2PTransport();
            _transport.MessageReceived += OnMessage;
            _transport.PeerConnected += _ => SendHello();
            if (_announcedLevel != null)
            {
                Lobby.SetLevelInfo(_announcedLevel.Key, _announcedLevel.Display);
                _announcedLevel = null;
            }
            if (Lobby.IsOwner)
            {
                Lobby.SetMode((Transport.RoomMode)Main.Settings.RoomModePref);
                Lobby.SetSpeed(Main.Settings.RoomSpeedPref);
            }
            Scoreboard.ResetSession();
            Status = $"In room '{Lobby.RoomName}'";
            RefreshMembers();
        }

        private void OnLeft()
        {
            SyncedStart.ResetAll();
            LevelTransfer.PeerReset();
            Scoreboard.ResetSession();
            Toasts.Clear();
            RestoreSpeed();
            _transport?.Dispose();
            _transport = null;
            Members.Clear();
            IsAutoRoom = false;
            _announcedLevel = null;
            Status = "Left the room.";
        }

        /// <summary>Reliable broadcast to everyone we can reach in the room.</summary>
        internal void SendAll(MessageType type, object payload)
        {
            _transport?.Broadcast(Codec.Encode(type, payload), SendMode.Reliable);
        }

        /// <summary>Reliable message to one specific member.</summary>
        internal void SendToPeer(ulong peerId, MessageType type, object payload)
        {
            _transport?.Send(peerId, Codec.Encode(type, payload), SendMode.Reliable);
        }

        private float _statsSentAt;
        private bool _localDead;
        private bool _localWon;
        private float _forceRestartAt = -1000f;

        public class ChatToast
        {
            public string Name;
            public string Text;
            public float At;
        }

        public readonly List<ChatToast> Toasts = new List<ChatToast>();

        public void SendChat(string text)
        {
            SendAll(MessageType.Chat, new ChatMsg { Text = text });
            AddToast(SteamFriends.GetPersonaName(), text);
        }

        private void AddToast(string name, string text)
        {
            Toasts.Add(new ChatToast { Name = name, Text = text, At = UnityEngine.Time.realtimeSinceStartup });
            while (Toasts.Count > 5) Toasts.RemoveAt(0);
        }

        // Sample our run 4×/s: stream stats to the room, detect deaths for the death-sync rule.
        private void TickLiveStats()
        {
            if (!InRoom || Members.Count < 2 || _transport == null) return;
            var level = Game.LevelTracker.Current;
            if (level == null)
            {
                _localDead = false;
                _localWon = false;
                return;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _statsSentAt < 0.25f) return;
            _statsSentAt = now;

            float progress = 0f, accuracy = 1f;
            bool dead = false, won = false;
            int seqId = 0;
            double songPos = 0;
            try
            {
                var controller = scrController.instance;
                if (controller == null) return;
                progress = controller.percentComplete;
                accuracy = controller.mistakesManager?.percentAcc ?? 1f;
                dead = controller.currentState == States.Fail || controller.currentState == States.Fail2;
                won = controller.currentState == States.Won;
                seqId = controller.currentSeqID;
                songPos = ADOBase.conductor != null ? ADOBase.conductor.songposition_minusi : 0;
            }
            catch
            {
                return; // mid-transition
            }

            if (won && !_localWon)
            {
                var result = new RunResultMsg { Key = level.Key, Acc = accuracy };
                SendAll(MessageType.RunResult, result);
                Scoreboard.NoteWon(SteamUser.GetSteamID().m_SteamID, SteamFriends.GetPersonaName(), result);
            }
            _localWon = won;

            var self = Members.FirstOrDefault(m => m.IsSelf);
            if (self != null)
            {
                self.Progress = progress;
                self.Accuracy = accuracy;
                self.Dead = dead;
                self.StatsAt = now;
                self.SeqId = seqId;
                self.SongPos = songPos;
                self.StatsKey = level.Key;
                Scoreboard.NoteStats(self, level.Key);
            }
            _transport.Broadcast(
                Codec.Encode(MessageType.LiveStats, new LiveStatsMsg
                {
                    Key = level.Key,
                    Progress = progress,
                    Accuracy = accuracy,
                    Dead = dead,
                    SeqId = seqId,
                    SongPos = songPos,
                }),
                SendMode.Unreliable);

            // Death-sync rule: our death in the room's synced level restarts everyone.
            if (dead && !_localDead && Lobby.DeathSyncEnabled && level.Key == SyncedStart.LastSyncedKey && now - _forceRestartAt > 3f)
            {
                _forceRestartAt = now;
                SendAll(MessageType.ForceRestart, new ForceRestartMsg { Key = level.Key });
                Main.Log("[deathsync] we died — restarting the room");
                DoForceRestart(level.Key);
            }
            _localDead = dead;
        }

        private bool _speedAsserted;

        // Custom levels multiply their pitch AND bake their timing from GCS.currentSpeedTrial
        // at load (the Speed Trial mechanism) — keep it asserted while a room speed is set so
        // every load, including retries and pulled-in clients, builds at the right speed.
        private void TickRoomSpeed()
        {
            if (!InRoom)
            {
                RestoreSpeed();
                return;
            }
            float speed = Lobby.SpeedMultiplier;
            if (UnityEngine.Mathf.Abs(speed - 1f) > 0.004f)
            {
                GCS.currentSpeedTrial = speed;
                _speedAsserted = true;
            }
            else
            {
                RestoreSpeed();
            }
        }

        private void RestoreSpeed()
        {
            if (_speedAsserted)
            {
                GCS.currentSpeedTrial = 1f;
                _speedAsserted = false;
            }
        }

        private void DoForceRestart(string key)
        {
            var level = Game.LevelTracker.Current;
            if (level == null || level.Key != key) return;
            foreach (var member in Members)
            {
                member.Dead = false;
                member.Progress = 0f;
            }
            try
            {
                Scoreboard.AbandonRound();
                SyncedStart.ForceResync();
                ADOBase.RestartScene();
            }
            catch (Exception e)
            {
                Main.LogError($"Force restart failed: {e.Message}");
            }
        }

        private void OnRoomsListed(List<RoomInfo> rooms)
        {
            AvailableRooms.Clear();
            AvailableRooms.AddRange(rooms);
            Status = rooms.Count == 0 ? "No rooms found — create one!" : $"{rooms.Count} room(s) found.";
        }

        private void RefreshMembers()
        {
            if (!InRoom) return;
            var stillConnected = new HashSet<ulong>(Members.Where(m => m.P2PConnected).Select(m => m.Id));
            ulong self = SteamUser.GetSteamID().m_SteamID;
            ulong owner = Lobby.OwnerId;
            Members.Clear();
            foreach (var member in Lobby.GetMembers())
            {
                Members.Add(new MemberState
                {
                    Id = member.Key,
                    Name = member.Value,
                    IsSelf = member.Key == self,
                    IsHost = member.Key == owner,
                    P2PConnected = member.Key == self || stillConnected.Contains(member.Key),
                });
            }
            // Full mesh for now: every member talks to every member. Star topology
            // (clients→host only) gets enforced in M2 where relaying starts to matter.
            _transport.UpdateTopology(Lobby.IsOwner, Members.Select(m => m.Id));
            SendHello();
        }

        private void SendHello()
        {
            _transport?.Broadcast(
                Codec.Encode(MessageType.Hello, new Hello { Name = SteamFriends.GetPersonaName() }),
                SendMode.Reliable);
        }

        private void OnMessage(ulong from, byte[] data)
        {
            var envelope = Codec.Decode(data);
            if (envelope == null) return;
            if (envelope.ProtocolVersion != ProtocolInfo.Version)
            {
                Status = $"Version mismatch with a peer — both sides need the latest R3D Unison.";
                return;
            }
            switch (envelope.Type)
            {
                case MessageType.Hello:
                    var member = Members.FirstOrDefault(m => m.Id == from);
                    if (member != null && !member.P2PConnected)
                    {
                        member.P2PConnected = true;
                        SendHello(); // answer once so the other side marks us too
                    }
                    break;
                case MessageType.StartLevel:
                    SyncedStart.OnStartLevel(from, Codec.Payload<StartLevelMsg>(envelope));
                    break;
                case MessageType.Ready:
                    SyncedStart.OnPeerReady(from, Codec.Payload<LevelReadyMsg>(envelope));
                    break;
                case MessageType.CountdownStart:
                    SyncedStart.OnGo(from, Codec.Payload<CountdownMsg>(envelope));
                    break;
                case MessageType.SyncAbort:
                    SyncedStart.OnAbort(from);
                    break;
                case MessageType.LiveStats:
                {
                    var stats = Codec.Payload<LiveStatsMsg>(envelope);
                    var sender = Members.FirstOrDefault(m => m.Id == from);
                    if (sender != null && stats != null)
                    {
                        sender.Progress = stats.Progress;
                        sender.Accuracy = stats.Accuracy;
                        sender.Dead = stats.Dead;
                        sender.StatsAt = UnityEngine.Time.realtimeSinceStartup;
                        sender.SeqId = stats.SeqId;
                        sender.SongPos = stats.SongPos;
                        sender.StatsKey = stats.Key;
                        Scoreboard.NoteStats(sender, stats.Key);
                    }
                    break;
                }
                case MessageType.RunResult:
                {
                    var result = Codec.Payload<RunResultMsg>(envelope);
                    var who = Members.FirstOrDefault(m => m.Id == from);
                    if (result != null) Scoreboard.NoteWon(from, who?.Name ?? from.ToString(), result);
                    break;
                }
                case MessageType.Chat:
                {
                    var chat = Codec.Payload<ChatMsg>(envelope);
                    var who = Members.FirstOrDefault(m => m.Id == from);
                    if (chat?.Text != null && chat.Text.Length <= 64) AddToast(who?.Name ?? "?", chat.Text);
                    break;
                }
                case MessageType.LevelRequest:
                    LevelTransfer.OnLevelRequest(from, Codec.Payload<LevelRequestMsg>(envelope));
                    break;
                case MessageType.LevelOffer:
                    LevelTransfer.OnLevelOffer(from, Codec.Payload<LevelOfferMsg>(envelope));
                    break;
                case MessageType.LevelChunk:
                    LevelTransfer.OnChunk(from, Codec.Payload<LevelChunkMsg>(envelope));
                    break;
                case MessageType.ChunkAck:
                    LevelTransfer.OnChunkAck(from, Codec.Payload<ChunkAckMsg>(envelope));
                    break;
                case MessageType.LevelDecline:
                    LevelTransfer.OnLevelDecline(from, Codec.Payload<LevelDeclineMsg>(envelope));
                    break;
                case MessageType.ForceRestart:
                {
                    var restart = Codec.Payload<ForceRestartMsg>(envelope);
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (restart != null && now - _forceRestartAt > 3f)
                    {
                        _forceRestartAt = now;
                        Main.Log("[deathsync] a player died — restarting");
                        DoForceRestart(restart.Key);
                    }
                    break;
                }
            }
        }

        public void Dispose()
        {
            SyncedStart.ResetAll();
            Core.MainThreadDispatcher.OnFrame -= Tick;
            Game.LevelTracker.Entered -= OnLevelEntered;
            Game.LevelTracker.Exited -= OnLevelExited;
            _transport?.Dispose();
            _transport = null;
            Lobby?.Dispose();
            Lobby = null;
        }
    }
}
