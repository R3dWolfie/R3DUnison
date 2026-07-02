using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// Compact in-play overlay: who's in your room while you're actually playing.
    /// Left edge, below typical HUD mods. Gains live acc/miss columns with the
    /// ghost-race milestone — for now it shows presence + P2P state.
    /// </summary>
    public class RosterOverlay : MonoBehaviour
    {
        private void OnGUI()
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || Game.LevelTracker.Current == null) return;
            if (rm.Members.Count <= 1) return; // alone: nothing worth overlaying

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            UnisonTheme.Ensure();

            float height = 44 + rm.Members.Count * 30;
            GUILayout.BeginArea(new Rect(16, 240, 250, height), UnisonTheme.Overlay);
            GUILayout.Label("R3D UNISON", UnisonTheme.OverlayHead);
            GUILayout.Space(4);
            foreach (var member in rm.Members)
            {
                GUILayout.BeginHorizontal();
                bool connected = member.IsSelf || member.P2PConnected;
                GUILayout.Label("●", connected ? UnisonTheme.DotOn : UnisonTheme.DotOff);
                GUILayout.Label(member.Name, UnisonTheme.Label);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();

            GUI.matrix = previousMatrix;
        }
    }
}
