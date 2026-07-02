using System.Collections.Generic;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// On-track ghosts as real planet pairs: a stationary planet on the tile the player
    /// is on and a second planet orbiting it along the actual floor geometry (neighbor
    /// directions + isCCW give the sweep), both in the player's own planet colors
    /// (streamed in Hello). Rendered semi-transparent so they never mask your own run.
    /// </summary>
    public class GhostOverlay : MonoBehaviour
    {
        private static readonly Color[] Palette =
        {
            UnisonTheme.Green,
            new Color(0.44f, 0.66f, 0.86f),
            new Color(0.90f, 0.76f, 0.35f),
            new Color(0.71f, 0.47f, 0.84f),
            new Color(0.94f, 0.56f, 0.31f),
        };

        private readonly GUIStyle _planet = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        private readonly GUIStyle _name = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        /// <summary>Shared ghost math: where is this member on OUR copy of the track right now.</summary>
        public static bool TryGetGhostPosition(MemberState member, List<scrFloor> floors,
            out Vector3 stationary, out Vector3 orbit, out int seqOut)
        {
            stationary = orbit = default;
            seqOut = 0;
            double t = member.SongPos + (Time.realtimeSinceStartup - member.StatsAt);
            int seq = Mathf.Clamp(member.SeqId, 0, floors.Count - 1);
            while (seq + 1 < floors.Count && floors[seq + 1] != null && floors[seq + 1].entryTime <= t) seq++;
            var floor = floors[seq];
            if (floor == null) return false;

            Vector3 cur = floor.transform.position;
            Vector3 next = seq + 1 < floors.Count && floors[seq + 1] != null ? floors[seq + 1].transform.position : cur;
            Vector3 prev = seq > 0 && floors[seq - 1] != null ? floors[seq - 1].transform.position : cur - (next - cur);

            float frac = 0f;
            if (seq + 1 < floors.Count && floors[seq + 1] != null)
            {
                double e0 = floor.entryTime, e1 = floors[seq + 1].entryTime;
                if (e1 > e0) frac = Mathf.Clamp01((float)((t - e0) / (e1 - e0)));
            }

            float radius = (next - cur).magnitude;
            if (radius < 0.01f) radius = (cur - prev).magnitude;
            if (radius < 0.01f) radius = 1.5f;

            // Orbit from the direction we came from to the direction we're going,
            // swept the way the floor says the planets rotate.
            float a0 = Mathf.Atan2(prev.y - cur.y, prev.x - cur.x);
            float a1 = Mathf.Atan2(next.y - cur.y, next.x - cur.x);
            float delta;
            if (floor.isCCW)
            {
                delta = Mathf.Repeat(a1 - a0, Mathf.PI * 2f);
                if (delta < 0.001f) delta = Mathf.PI * 2f;
            }
            else
            {
                delta = -Mathf.Repeat(a0 - a1, Mathf.PI * 2f);
                if (delta > -0.001f) delta = -Mathf.PI * 2f;
            }
            float angle = a0 + delta * frac;
            stationary = cur;
            orbit = cur + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            seqOut = seq;
            return true;
        }

        private void OnGUI()
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || rm.Members.Count < 2) return;
            var mine = Game.LevelTracker.Current;
            if (mine == null)
            {
                DrawMenuGhosts(rm);
                return;
            }

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
            _name.fontSize = Mathf.RoundToInt(15f * scale);

            int paletteIndex = 0;
            foreach (var member in rm.Members)
            {
                if (member.IsSelf) continue;
                var fallback = Palette[paletteIndex++ % Palette.Length];
                if (!member.HasFreshStats || member.Dead || member.StatsKey != mine.Key) continue;
                if (!TryGetGhostPosition(member, floors, out var stationary, out var orbit, out int seq)) continue;

                Vector3 screenA = cam.WorldToScreenPoint(stationary);
                Vector3 screenB = cam.WorldToScreenPoint(orbit);
                if (screenA.z <= 0f) continue;
                float ax = screenA.x, ay = Screen.height - screenA.y;
                float bx = screenB.x, by = Screen.height - screenB.y;
                if ((ax < -80 || ax > Screen.width + 80 || ay < -80 || ay > Screen.height + 80)
                    && (bx < -80 || bx > Screen.width + 80 || by < -80 || by > Screen.height + 80)) continue;

                Color color1 = member.HasColors ? member.Color1 : fallback;
                Color color2 = member.HasColors ? member.Color2 : Color.Lerp(fallback, Color.white, 0.45f);
                // Real cloned planets render these members — we only add the name label
                bool realPlanets = Game.GhostPlanets.RealPlanets.Contains(member.Id);
                int fontSize;
                if (!realPlanets)
                {
                    Color onTile = seq % 2 == 0 ? color1 : color2;
                    Color orbiting = seq % 2 == 0 ? color2 : color1;
                    float orbitPx = new Vector2(bx - ax, by - ay).magnitude;
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(orbitPx * 0.8f), 14, Mathf.RoundToInt(96f * scale));
                    _planet.fontSize = fontSize;
                    DrawPlanet(ax, ay, onTile);
                    DrawPlanet(bx, by, orbiting);
                }
                else
                {
                    fontSize = Mathf.RoundToInt(30f * scale);
                }

                float nameW = 220f * scale;
                float nameY = ay - fontSize * 0.75f - 22f * scale;
                _name.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(ax - nameW / 2 + 1, nameY + 1, nameW, 24f * scale), member.Name, _name);
                _name.normal.textColor = new Color(color1.r, color1.g, color1.b, 0.95f);
                GUI.Label(new Rect(ax - nameW / 2, nameY, nameW, 24f * scale), member.Name, _name);
            }
        }

        // Level select is a shared-coordinate freeroam world: draw roommates' planet
        // pairs idling/moving where they actually are in the same menu.
        private void DrawMenuGhosts(RoomManager rm)
        {
            string key;
            Camera cam;
            try
            {
                if (!ADOBase.isLevelSelect) return;
                key = "menu:" + ADOBase.sceneName;
                cam = Camera.main;
            }
            catch
            {
                return;
            }
            if (cam == null) return;

            UnisonTheme.Ensure();
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            _name.fontSize = Mathf.RoundToInt(15f * scale);

            int paletteIndex = 0;
            foreach (var member in rm.Members)
            {
                if (member.IsSelf) continue;
                var fallback = Palette[paletteIndex++ % Palette.Length];
                if (!member.HasFreshStats || member.StatsKey != key) continue;

                Vector3 center = new Vector3(member.PosX, member.PosY, 0f);
                float angle = Time.time * 1.8f + paletteIndex;
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.8f;
                Vector3 screenA = cam.WorldToScreenPoint(center + offset);
                Vector3 screenB = cam.WorldToScreenPoint(center - offset);
                if (screenA.z <= 0f) continue;
                float ax = screenA.x, ay = Screen.height - screenA.y;
                float bx = screenB.x, by = Screen.height - screenB.y;
                if (ax < -80 || ax > Screen.width + 80 || ay < -80 || ay > Screen.height + 80) continue;

                Color color1 = member.HasColors ? member.Color1 : fallback;
                Color color2 = member.HasColors ? member.Color2 : Color.Lerp(fallback, Color.white, 0.45f);
                float orbitPx = new Vector2(bx - ax, by - ay).magnitude * 0.5f;
                _planet.fontSize = Mathf.Clamp(Mathf.RoundToInt(orbitPx * 0.9f), 12, Mathf.RoundToInt(70f * scale));
                DrawPlanet(ax, ay, color1);
                DrawPlanet(bx, by, color2);

                Vector3 screenC = cam.WorldToScreenPoint(center);
                float cx = screenC.x, cy = Screen.height - screenC.y;
                float nameW = 220f * scale;
                float nameY = cy - _planet.fontSize - 26f * scale;
                _name.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(cx - nameW / 2 + 1, nameY + 1, nameW, 24f * scale), member.Name, _name);
                _name.normal.textColor = new Color(color1.r, color1.g, color1.b, 0.95f);
                GUI.Label(new Rect(cx - nameW / 2, nameY, nameW, 24f * scale), member.Name, _name);
            }
        }

        private void DrawPlanet(float x, float y, Color color)
        {
            float half = _planet.fontSize;
            _planet.normal.textColor = new Color(0f, 0f, 0f, 0.5f);
            GUI.Label(new Rect(x - half + 2, y - half + 2, half * 2, half * 2), "●", _planet);
            _planet.normal.textColor = new Color(color.r, color.g, color.b, 0.85f);
            GUI.Label(new Rect(x - half, y - half, half * 2, half * 2), "●", _planet);
        }
    }
}
