using System;
using System.Collections.Generic;
using Steamworks;

namespace R3DUnison.Transport
{
    public class RoomInfo
    {
        public CSteamID LobbyId;
        public string Name;
        public int Players;
        public int Capacity;
        /// <summary>Display name of the level the host is currently playing ("" = in menu).</summary>
        public string Level;
        public bool IsAuto;
    }

    /// <summary>
    /// Room directory + membership over Steam lobbies. Public lobbies tagged with our
    /// marker key give a room browser with zero infrastructure. All callbacks arrive on
    /// the main thread via the game's own SteamAPI.RunCallbacks pump.
    /// </summary>
    public class SteamLobby : IDisposable
    {
        public const int MaxPlayers = 8;
        private const string KeyMarker = "r3du";
        private const string KeyName = "name";
        private const string KeyProtocol = "proto";
        private const string KeyLevel = "level";
        private const string KeyLevelId = "lvlkey";
        private const string KeyAuto = "auto";
        private const string KeyDeathSync = "dsync";

        private readonly CallResult<LobbyCreated_t> _created;
        private readonly CallResult<LobbyEnter_t> _joined;
        private readonly CallResult<LobbyMatchList_t> _listed;
        private readonly Callback<LobbyChatUpdate_t> _chatUpdate;
        private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;

        public CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;
        public bool InRoom => CurrentLobby.IsValid();
        public bool IsOwner => InRoom && SteamMatchmaking.GetLobbyOwner(CurrentLobby) == SteamUser.GetSteamID();
        public ulong OwnerId => InRoom ? SteamMatchmaking.GetLobbyOwner(CurrentLobby).m_SteamID : 0;
        public string RoomName => InRoom ? SteamMatchmaking.GetLobbyData(CurrentLobby, KeyName) : "";

        public event Action<List<RoomInfo>> RoomsListed;
        public event Action Entered;
        public event Action<string> JoinFailed;
        public event Action MembersChanged;
        public event Action Left;

        public SteamLobby()
        {
            _created = CallResult<LobbyCreated_t>.Create(OnCreated);
            _joined = CallResult<LobbyEnter_t>.Create(OnJoined);
            _listed = CallResult<LobbyMatchList_t>.Create(OnListed);
            _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        }

        public void CreateRoom(string name, bool auto = false)
        {
            if (InRoom) Leave();
            _pendingRoomName = string.IsNullOrWhiteSpace(name) ? $"{SteamFriends.GetPersonaName()}'s room" : name.Trim();
            _pendingAuto = auto;
            _created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MaxPlayers));
        }

        private string _pendingRoomName;
        private bool _pendingAuto;

        private void OnCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                JoinFailed?.Invoke($"Room creation failed ({(ioFailure ? "IO failure" : result.m_eResult.ToString())})");
                return;
            }
            CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(CurrentLobby, KeyMarker, "1");
            SteamMatchmaking.SetLobbyData(CurrentLobby, KeyName, _pendingRoomName);
            SteamMatchmaking.SetLobbyData(CurrentLobby, KeyProtocol, Protocol.ProtocolInfo.Version.ToString());
            if (_pendingAuto) SteamMatchmaking.SetLobbyData(CurrentLobby, KeyAuto, "1");
            Entered?.Invoke();
        }

        /// <summary>Owner only: advertise the level currently being played (null = back in menu).</summary>
        public void SetLevelInfo(string levelKey, string levelDisplay)
        {
            if (!InRoom || !IsOwner) return;
            SteamMatchmaking.SetLobbyData(CurrentLobby, KeyLevelId, levelKey ?? "");
            SteamMatchmaking.SetLobbyData(CurrentLobby, KeyLevel, levelDisplay ?? "");
        }

        public string CurrentLevelDisplay => InRoom ? SteamMatchmaking.GetLobbyData(CurrentLobby, KeyLevel) : "";

        /// <summary>Room rule (owner-set): anyone dying restarts the level for everyone.</summary>
        public bool DeathSyncEnabled => InRoom && SteamMatchmaking.GetLobbyData(CurrentLobby, KeyDeathSync) == "1";

        public void SetDeathSync(bool enabled)
        {
            if (InRoom && IsOwner) SteamMatchmaking.SetLobbyData(CurrentLobby, KeyDeathSync, enabled ? "1" : "0");
        }

        public void RefreshRooms()
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyMarker, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            _listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        private void OnListed(LobbyMatchList_t result, bool ioFailure)
        {
            var rooms = new List<RoomInfo>();
            if (!ioFailure)
            {
                for (int i = 0; i < result.m_nLobbiesMatching; i++)
                {
                    var lobby = SteamMatchmaking.GetLobbyByIndex(i);
                    rooms.Add(new RoomInfo
                    {
                        LobbyId = lobby,
                        Name = SteamMatchmaking.GetLobbyData(lobby, KeyName),
                        Players = SteamMatchmaking.GetNumLobbyMembers(lobby),
                        Capacity = SteamMatchmaking.GetLobbyMemberLimit(lobby),
                        Level = SteamMatchmaking.GetLobbyData(lobby, KeyLevel),
                        IsAuto = SteamMatchmaking.GetLobbyData(lobby, KeyAuto) == "1",
                    });
                }
            }
            RoomsListed?.Invoke(rooms);
        }

        public void JoinRoom(CSteamID lobby)
        {
            if (InRoom) Leave();
            _joined.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private void OnJoined(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                JoinFailed?.Invoke($"Join failed ({(ioFailure ? "IO failure" : ((EChatRoomEnterResponse)result.m_EChatRoomEnterResponse).ToString())})");
                return;
            }
            CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
            Entered?.Invoke();
        }

        // Friend clicked "Join game" in the Steam overlay
        private void OnJoinRequested(GameLobbyJoinRequested_t request) => JoinRoom(request.m_steamIDLobby);

        private void OnChatUpdate(LobbyChatUpdate_t update)
        {
            if (InRoom && update.m_ulSteamIDLobby == CurrentLobby.m_SteamID)
            {
                MembersChanged?.Invoke();
            }
        }

        public List<KeyValuePair<ulong, string>> GetMembers()
        {
            var members = new List<KeyValuePair<ulong, string>>();
            if (!InRoom) return members;
            int count = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
            for (int i = 0; i < count; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobby, i);
                members.Add(new KeyValuePair<ulong, string>(id.m_SteamID, SteamFriends.GetFriendPersonaName(id)));
            }
            return members;
        }

        public void InviteOverlay()
        {
            if (InRoom) SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        }

        public void Leave()
        {
            if (!InRoom) return;
            SteamMatchmaking.LeaveLobby(CurrentLobby);
            CurrentLobby = CSteamID.Nil;
            Left?.Invoke();
        }

        public void Dispose()
        {
            Leave();
            _created.Dispose();
            _joined.Dispose();
            _listed.Dispose();
            _chatUpdate.Dispose();
            _joinRequested.Dispose();
        }
    }
}
