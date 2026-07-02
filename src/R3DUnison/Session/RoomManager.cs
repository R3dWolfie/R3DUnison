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
            Status = $"In room '{Lobby.RoomName}'";
            RefreshMembers();
        }

        private void OnLeft()
        {
            _transport?.Dispose();
            _transport = null;
            Members.Clear();
            IsAutoRoom = false;
            _announcedLevel = null;
            Status = "Left the room.";
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
            }
        }

        public void Dispose()
        {
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
