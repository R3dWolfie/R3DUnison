using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// F9-toggled IMGUI window: room browser + lobby roster. Deliberately plain —
    /// this is the M1 functional shell; the real themed UI lands in M3.
    /// Layout is authored at 1080p and scaled by resolution (readable at 4K).
    /// </summary>
    public class MultiplayerWindow : MonoBehaviour
    {
        private const int WindowId = 0x52334455; // "R3DU"

        private bool _visible;
        private Rect _rect = new Rect(80, 80, 560, 640);
        private string _roomName = "";
        private Vector2 _scroll;
        private GUIStyle _title, _label, _button, _textField, _toggle;

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
            EnsureStyles();
            _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "R3D Unison — Multiplayer  [F9]");
            GUI.matrix = previousMatrix;
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            _textField = new GUIStyle(GUI.skin.textField) { fontSize = 18 };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 18 };
        }

        private void DrawWindow(int id)
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.SteamReady)
            {
                GUILayout.Label("Waiting for Steam…", _label);
            }
            else if (rm.InRoom)
            {
                DrawRoom(rm);
            }
            else
            {
                DrawBrowser(rm);
            }

            if (rm != null && !string.IsNullOrEmpty(rm.Status))
            {
                GUILayout.Space(8);
                GUILayout.Label(rm.Status, _label);
            }
            GUI.DragWindow();
        }

        private void DrawBrowser(RoomManager rm)
        {
            bool announce = GUILayout.Toggle(Main.Settings.AutoAnnounce, " Announce levels I play as public rooms", _toggle);
            if (announce != Main.Settings.AutoAnnounce)
            {
                Main.Settings.AutoAnnounce = announce;
                Main.Settings.Save(Main.Mod);
            }
            GUILayout.Space(8);

            GUILayout.Label("Create a room", _title);
            GUILayout.BeginHorizontal();
            _roomName = GUILayout.TextField(_roomName, 40, _textField, GUILayout.MinWidth(280));
            if (GUILayout.Button("Create", _button, GUILayout.Width(120)))
            {
                rm.CreateRoom(_roomName);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Public rooms", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", _button, GUILayout.Width(120)))
            {
                rm.RefreshRooms();
            }
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(360));
            foreach (var room in rm.AvailableRooms)
            {
                GUILayout.BeginHorizontal();
                string playing = string.IsNullOrEmpty(room.Level) ? "" : $"  ·  playing {room.Level}";
                GUILayout.Label($"{room.Name}  ({room.Players}/{room.Capacity}){playing}", _label);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Join", _button, GUILayout.Width(100)))
                {
                    rm.JoinRoom(room);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawRoom(RoomManager rm)
        {
            GUILayout.Label($"Room: {rm.Lobby.RoomName}", _title);
            string level = rm.Lobby.CurrentLevelDisplay;
            if (!string.IsNullOrEmpty(level))
            {
                GUILayout.Label($"Level: {level}", _label);
            }
            GUILayout.Space(8);
            foreach (var member in rm.Members)
            {
                string link = member.IsSelf ? "you" : member.P2PConnected ? "✓ connected" : "… connecting";
                string host = member.IsHost ? "  [host]" : "";
                GUILayout.Label($"{member.Name}{host}  —  {link}", _label);
            }
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Invite friends", _button))
            {
                rm.InviteFriends();
            }
            if (GUILayout.Button("Leave room", _button))
            {
                rm.LeaveRoom();
            }
            GUILayout.EndHorizontal();
        }
    }
}
