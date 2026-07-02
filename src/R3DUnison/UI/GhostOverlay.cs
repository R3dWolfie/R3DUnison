using System.Collections.Generic;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// On-track ghost markers: a colored orb + name at the tile each room member is
    /// on right now, driven by their streamed seqID/song position and interpolated
    /// between floor entry times so it glides. Same level + shared floor list means
    /// their tile positions are valid in *your* scene. Visible whenever their part of
    /// the track is on your screen.
    /// </summary>
    public class GhostOverlay : MonoBehaviour
    {
        private static readonly Color[] Palette =
        {
            UnisonTheme.Green,
            new Color(0.44f, 0.66f, 0.86f),  // blue
            new Color(0.90f, 0.76f, 0.35f),  // gold
            new Color(0.71f, 0.47f, 0.84f),  // purple
            new Color(0.94f, 0.56f, 0.31f),  // orange
        };

        private readonly GUIStyle _dot = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        private readonly GUIStyle _name = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        private void OnGUI()
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || rm.Members.Count < 2) return;
            var mine = Game.LevelTracker.Current;
            if (mine == null) return;

            List<scrFloor> floors;
            Camera cam;
            try
            {
                floors = ADOBase.lm?.listFloors;
                cam = Camera.main;
            }
            catch
            {
                return;
            }
            if (floors == null || floors.Count == 0 || cam == null) return;

            UnisonTheme.Ensure();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            _dot.fontSize = Mathf.RoundToInt(34f * scale);
            _name.fontSize = Mathf.RoundToInt(15f * scale);

            int paletteIndex = 0;
            foreach (var member in rm.Members)
            {
                if (member.IsSelf) continue;
                var color = Palette[paletteIndex++ % Palette.Length];
                if (!member.HasFreshStats || member.Dead || member.StatsKey != mine.Key) continue;

                // Extrapolate their song position from the last packet so the ghost glides
                double t = member.SongPos + (Time.realtimeSinceStartup - member.StatsAt);
                int seq = Mathf.Clamp(member.SeqId, 0, floors.Count - 1);
                while (seq + 1 < floors.Count && floors[seq + 1] != null && floors[seq + 1].entryTime <= t) seq++;
                var floor = floors[seq];
                if (floor == null) continue;

                Vector3 from = floor.transform.position;
                Vector3 to = from;
                float frac = 0f;
                if (seq + 1 < floors.Count && floors[seq + 1] != null)
                {
                    to = floors[seq + 1].transform.position;
                    double e0 = floor.entryTime, e1 = floors[seq + 1].entryTime;
                    if (e1 > e0) frac = Mathf.Clamp01((float)((t - e0) / (e1 - e0)));
                }
                Vector3 world = Vector3.Lerp(from, to, frac);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;
                float x = screen.x;
                float y = Screen.height - screen.y;
                if (x < -60 || x > Screen.width + 60 || y < -60 || y > Screen.height + 60) continue;

                float dotHalf = 30f * scale;
                _dot.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(x - dotHalf + 2, y - dotHalf + 2, dotHalf * 2, dotHalf * 2), "●", _dot);
                _dot.normal.textColor = color;
                GUI.Label(new Rect(x - dotHalf, y - dotHalf, dotHalf * 2, dotHalf * 2), "●", _dot);

                float nameW = 220f * scale;
                float nameY = y + 20f * scale;
                _name.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(x - nameW / 2 + 1, nameY + 1, nameW, 24f * scale), member.Name, _name);
                _name.normal.textColor = color;
                GUI.Label(new Rect(x - nameW / 2, nameY, nameW, 24f * scale), member.Name, _name);
            }
        }
    }
}
