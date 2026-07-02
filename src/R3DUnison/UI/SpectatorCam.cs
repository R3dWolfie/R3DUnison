using System.Linq;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// Spectator behavior, two flavors:
    /// - Dead: on the fail screen with a living roommate, the camera follows the leading
    ///   survivor; restart input is swallowed (FailInputPatch) so you keep watching — R retries.
    /// - Loaded-in (SPECTATE button): the level sits gated pre-music while the camera follows;
    ///   the next synced round releases the gate and you're playing.
    /// </summary>
    public class SpectatorCam : MonoBehaviour
    {
        public static bool HoldingFailScreen { get; private set; }
        public static string HintLine { get; private set; }

        private void Update()
        {
            if (HoldingFailScreen && Input.GetKeyDown(KeyCode.R))
            {
                HoldingFailScreen = false;
                HintLine = null;
                try
                {
                    ADOBase.RestartScene();
                }
                catch
                {
                    // scene mid-transition; the next fail input will do it
                }
            }
        }

        private void LateUpdate()
        {
            HoldingFailScreen = false;
            HintLine = null;
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || rm.Members.Count < 2) return;
            var mine = Game.LevelTracker.Current;
            if (mine == null) return;
            try
            {
                var controller = scrController.instance;
                if (controller == null) return;
                bool dead = controller.currentState == States.Fail || controller.currentState == States.Fail2;
                bool gatedSpectator = SyncedStart.Spectating;
                if (!dead && !gatedSpectator) return;

                var target = rm.Members
                    .Where(m => !m.IsSelf && !m.Dead && m.HasFreshStats && m.StatsKey == mine.Key)
                    .OrderByDescending(m => m.Progress)
                    .FirstOrDefault();
                if (target == null)
                {
                    if (gatedSpectator) HintLine = "SPECTATING · waiting for players…";
                    return;
                }
                if (dead)
                {
                    HoldingFailScreen = true;
                    HintLine = $"SPECTATING {target.Name} — press R to retry";
                }
                else
                {
                    HintLine = $"SPECTATING {target.Name} — you join the next round";
                }

                var floors = ADOBase.lm?.listFloors;
                if (floors == null || floors.Count == 0) return;
                if (!GhostOverlay.TryGetGhostPosition(target, floors, out var stationary, out var orbit, out _)) return;

                var cam = controller.camy;
                if (cam == null) return;
                Vector3 focus = (stationary + orbit) * 0.5f;
                cam.topos = new Vector2(focus.x, focus.y);
                var t = cam.transform;
                t.position = Vector3.Lerp(t.position, new Vector3(focus.x, focus.y, t.position.z), 6f * Time.deltaTime);
            }
            catch
            {
                // camera plumbing changed mid-scene — just don't spectate this frame
            }
        }
    }
}
