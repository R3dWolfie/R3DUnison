using System;
using System.Linq;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// F9-toggled multiplayer window in the R3D theme: create bar, searchable card
    /// list of public rooms, roster view inside a room. Layout authored at 1080p and
    /// scaled by resolution.
    /// </summary>
    public class MultiplayerWindow : MonoBehaviour
    {
        private const int WindowId = 0x52334455; // "R3DU"

        private bool _visible;
        private Rect _rect = new Rect(80, 80, 620, 700);
        private string _roomName = "";
        private string _search = "";
        private Vector2 _scroll;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9)) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            UnisonTheme.Ensure();
            _rect = GUILayout.Window(WindowId, _rect, DrawWindow, GUIContent.none, UnisonTheme.Window);
            GUI.matrix = previousMatrix;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("R3D UNISON", UnisonTheme.Title);
            GUILayout.FlexibleSpace();
            GUILayout.Label("MULTIPLAYER · F9", UnisonTheme.TitleTag);
            GUILayout.EndHorizontal();
            UnisonTheme.AccentBar();
            GUILayout.Space(14);

            var rm = RoomManager.Instance;
            if (rm == null || !rm.SteamReady)
            {
                GUILayout.Label("Waiting for Steam…", UnisonTheme.Dim);
            }
            else if (rm.InRoom)
            {
                DrawRoom(rm);
            }
            else
            {
                DrawBrowser(rm);
            }

            if (Session.SyncedStart.StatusLine != null)
            {
                GUILayout.Space(10);
                GUILayout.Label(Session.SyncedStart.StatusLine, UnisonTheme.LevelText);
            }
            if (rm != null && !string.IsNullOrEmpty(rm.Status))
            {
                GUILayout.Space(10);
                GUILayout.Label(rm.Status, UnisonTheme.Status);
            }
            GUI.DragWindow();
        }

        private void DrawBrowser(RoomManager rm)
        {
            GUILayout.BeginHorizontal();
            _roomName = GUILayout.TextField(_roomName, 40, UnisonTheme.TextField, GUILayout.MinWidth(320));
            GUILayout.Space(8);
            if (GUILayout.Button("CREATE ROOM", UnisonTheme.ButtonPrimary, GUILayout.Width(160)))
            {
                rm.CreateRoom(_roomName);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Label("PUBLIC ROOMS", UnisonTheme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("REFRESH", UnisonTheme.Button, GUILayout.Width(110)))
            {
                rm.RefreshRooms();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            _search = GUILayout.TextField(_search, 40, UnisonTheme.TextField);
            GUILayout.Space(8);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(330));
            var rooms = rm.AvailableRooms.Where(RoomMatchesSearch).ToList();
            foreach (var room in rooms)
            {
                GUILayout.BeginVertical(UnisonTheme.Card);
                GUILayout.BeginHorizontal();
                GUILayout.Label(room.Name, UnisonTheme.Name);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{room.Players}/{room.Capacity}", UnisonTheme.LevelText);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (!string.IsNullOrEmpty(room.Level))
                {
                    GUILayout.Label($"▶ {room.Level}", UnisonTheme.LevelText);
                }
                else
                {
                    GUILayout.Label("in menu", UnisonTheme.Dim);
                }
                GUILayout.FlexibleSpace();
                bool full = room.Players >= room.Capacity;
                GUI.enabled = !full;
                if (GUILayout.Button(full ? "FULL" : "JOIN", UnisonTheme.ButtonPrimary, GUILayout.Width(90)))
                {
                    rm.JoinRoom(room);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            if (rooms.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(rm.AvailableRooms.Count == 0
                    ? "No rooms found — create one, or just play a level."
                    : "Nothing matches your search.", UnisonTheme.Dim);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10);
            bool announce = UnisonTheme.Toggle(Main.Settings.AutoAnnounce, "Announce levels I play as public rooms");
            if (announce != Main.Settings.AutoAnnounce)
            {
                Main.Settings.AutoAnnounce = announce;
                Main.Settings.Save(Main.Mod);
            }
        }

        private bool RoomMatchesSearch(Transport.RoomInfo room)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return (room.Name != null && room.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                || (room.Level != null && room.Level.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawRoom(RoomManager rm)
        {
            GUILayout.Label(rm.Lobby.RoomName, UnisonTheme.Name);
            string level = rm.Lobby.CurrentLevelDisplay;
            GUILayout.Label(string.IsNullOrEmpty(level) ? "in menu" : $"▶ {level}", string.IsNullOrEmpty(level) ? UnisonTheme.Dim : UnisonTheme.LevelText);
            GUILayout.Space(12);

            GUILayout.Label($"PLAYERS · {rm.Members.Count}", UnisonTheme.Header);
            GUILayout.Space(6);
            foreach (var member in rm.Members)
            {
                GUILayout.BeginHorizontal(UnisonTheme.Row);
                bool connected = member.IsSelf || member.P2PConnected;
                GUILayout.Label("●", connected ? UnisonTheme.DotOn : UnisonTheme.DotOff);
                GUILayout.Label(member.Name, UnisonTheme.Name);
                if (member.IsHost) GUILayout.Label("HOST", UnisonTheme.ChipHost);
                if (member.IsSelf) GUILayout.Label("YOU", UnisonTheme.ChipYou);
                GUILayout.FlexibleSpace();
                if (!connected) GUILayout.Label("connecting…", UnisonTheme.Dim);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            bool sync = UnisonTheme.Toggle(Main.Settings.SyncedStarts, "Synced starts — host launching a level pulls everyone in");
            if (sync != Main.Settings.SyncedStarts)
            {
                Main.Settings.SyncedStarts = sync;
                Main.Settings.Save(Main.Mod);
            }

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("INVITE FRIENDS", UnisonTheme.ButtonPrimary, GUILayout.Width(180)))
            {
                rm.InviteFriends();
            }
            GUILayout.Space(8);
            if (GUILayout.Button("LEAVE ROOM", UnisonTheme.Button, GUILayout.Width(140)))
            {
                rm.LeaveRoom();
            }
            GUILayout.EndHorizontal();
        }
    }
}
