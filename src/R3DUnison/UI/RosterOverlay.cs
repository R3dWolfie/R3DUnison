using System.Linq;
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
            if (rm == null || !rm.InRoom) return;
            bool syncActive = SyncedStart.Active;
            var toasts = rm.Toasts.Where(t => Time.realtimeSinceStartup - t.At < 5f).ToList();
            // Roster shows whenever you have company in the room (menu included);
            // toasts show whenever recent, even solo.
            bool showRoster = rm.Members.Count > 1 || syncActive;
            if (!showRoster && toasts.Count == 0) return;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            UnisonTheme.Ensure();

            bool showResults = showRoster && Scoreboard.LastRound != null && Time.realtimeSinceStartup - Scoreboard.LastRoundAt < 12f;
            float height = 40 + toasts.Count * 26;
            if (showRoster)
            {
                height += rm.Members.Count * 30
                    + (SyncedStart.StatusLine != null ? 32 : 0)
                    + (SpectatorCam.HintLine != null ? 32 : 0)
                    + (showResults ? 36 + Scoreboard.LastRound.Count * 26 : 0);
            }
            GUILayout.BeginArea(new Rect(16, 240, 320, height), UnisonTheme.Overlay);
            GUILayout.Label("R3D UNISON", UnisonTheme.OverlayHead);
            GUILayout.Space(4);
            if (!showRoster)
            {
                foreach (var toast in toasts)
                {
                    GUILayout.Label($"{toast.Name}: {toast.Text}", UnisonTheme.Label);
                }
                GUILayout.EndArea();
                GUI.matrix = previousMatrix;
                return;
            }
            if (SyncedStart.StatusLine != null)
            {
                GUILayout.Label(SyncedStart.StatusLine, UnisonTheme.LevelText);
                GUILayout.Space(4);
            }
            if (SpectatorCam.HintLine != null)
            {
                GUILayout.Label(SpectatorCam.HintLine, UnisonTheme.LevelText);
                GUILayout.Space(4);
            }
            foreach (var member in rm.Members)
            {
                GUILayout.BeginHorizontal();
                bool connected = member.IsSelf || member.P2PConnected;
                var entry = Scoreboard.GetEntry(member.Id);
                bool outOfRound = entry != null && entry.Eliminated;
                var dot = member.Dead || outOfRound ? UnisonTheme.DotDead : connected ? UnisonTheme.DotOn : UnisonTheme.DotOff;
                GUILayout.Label("●", dot);
                GUILayout.Label(member.Name, UnisonTheme.Label);
                GUILayout.FlexibleSpace();
                if (outOfRound)
                {
                    GUILayout.Label($"OUT · {entry.Progress:P0}", UnisonTheme.DeadText);
                }
                else if (member.Dead)
                {
                    GUILayout.Label($"✕ {member.Progress:P0}", UnisonTheme.DeadText);
                }
                else if (member.HasFreshStats)
                {
                    GUILayout.Label($"{member.Progress:P0} · {member.Accuracy:P1}", UnisonTheme.LevelText);
                }
                GUILayout.EndHorizontal();
            }
            if (showResults)
            {
                GUILayout.Space(6);
                GUILayout.Label($"ROUND — {Scoreboard.LastRoundWinner} wins!", UnisonTheme.OverlayHead);
                int place = 1;
                foreach (var entry in Scoreboard.LastRound)
                {
                    string detail = entry.Won ? $"{entry.Acc:P1}" : $"{entry.Progress:P0}";
                    GUILayout.Label($"{place++}. {entry.Name} — {detail}", UnisonTheme.Label);
                }
            }
            foreach (var toast in toasts)
            {
                GUILayout.Label($"{toast.Name}: {toast.Text}", UnisonTheme.Dim);
            }
            GUILayout.EndArea();

            GUI.matrix = previousMatrix;
        }
    }
}
