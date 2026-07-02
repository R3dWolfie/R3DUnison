using System.Linq;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// Spectate on death: while you're on the fail screen and someone in your room is
    /// still alive in the same level, the camera glides to the leading survivor's ghost
    /// so you watch their run instead of your corpse. Ends the moment you retry (scene
    /// reload gives the camera back to the game).
    /// </summary>
    public class SpectatorCam : MonoBehaviour
    {
        private void LateUpdate()
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || rm.Members.Count < 2) return;
            var mine = Game.LevelTracker.Current;
            if (mine == null) return;
            try
            {
                var controller = scrController.instance;
                if (controller == null) return;
                bool dead = controller.currentState == States.Fail || controller.currentState == States.Fail2;
                if (!dead) return;

                var target = rm.Members
                    .Where(m => !m.IsSelf && !m.Dead && m.HasFreshStats && m.StatsKey == mine.Key)
                    .OrderByDescending(m => m.Progress)
                    .FirstOrDefault();
                if (target == null) return;
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
