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

        private AudioSource _audio;
        private static int _targetIndex;

        /// <summary>Clear static spectate state on mod disable so a re-enable starts clean.</summary>
        public static void ResetState()
        {
            HoldingFailScreen = false;
            HintLine = null;
            _targetIndex = 0;
        }

        // Stable, sorted list of who you can watch right now (alive, same level, not you).
        private static System.Collections.Generic.List<Session.MemberState> Candidates(Session.RoomManager rm, string key)
        {
            return rm.Members
                .Where(m => !m.IsSelf && !m.Dead && m.HasFreshStats && m.StatsKey == key)
                .OrderBy(m => m.Id)
                .ToList();
        }

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
        }

        // Best-effort synced song while spectating: play the level's own clip from our
        // AudioSource, seeked to the target's streamed position, re-seek on drift.
        private void UpdateSpectateAudio(Session.MemberState target)
        {
            try
            {
                var conductor = ADOBase.conductor;
                var clip = conductor != null && conductor.song != null ? conductor.song.clip : null;
                if (clip == null)
                {
                    if (_audio.isPlaying) _audio.Stop();
                    return;
                }
                float pos = Mathf.Clamp((float)(target.SongPos + (Time.realtimeSinceStartup - target.StatsAt)), 0f, clip.length - 0.15f);
                if (pos <= 0.05f) return; // they haven't started yet
                if (_audio.clip != clip || !_audio.isPlaying)
                {
                    _audio.clip = clip;
                    _audio.pitch = conductor.song.pitch;
                    _audio.volume = conductor.song.volume;
                    _audio.time = pos;
                    _audio.Play();
                }
                else if (Mathf.Abs(_audio.time - pos) > 0.4f)
                {
                    _audio.time = pos;
                }
            }
            catch
            {
                // clip not ready — silence this frame
            }
        }

        private void StopSpectateAudio()
        {
            if (_audio != null && _audio.isPlaying) _audio.Stop();
        }

        private void Update()
        {
            // Cycle spectate target with the arrow keys (works while dead or autoplay-spectating).
            var rm = RoomManager.Instance;
            bool spectating = rm != null && rm.InRoom && (SyncedStart.AutoSpectating || HoldingFailScreen);
            if (spectating)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) _targetIndex++;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) _targetIndex--;
            }
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
            if (rm == null || !rm.InRoom || rm.Members.Count < 2)
            {
                StopSpectateAudio();
                return;
            }
            var mine = Game.LevelTracker.Current;
            if (mine == null)
            {
                StopSpectateAudio();
                return;
            }
            try
            {
                var controller = scrController.instance;
                if (controller == null) return;
                bool dead = controller.currentState == States.Fail || controller.currentState == States.Fail2;
                bool autoSpec = SyncedStart.AutoSpectating;
                if (!dead && !autoSpec)
                {
                    StopSpectateAudio();
                    return;
                }

                // Pick the selected player (arrow keys cycle _targetIndex; wraps).
                var candidates = Candidates(rm, mine.Key);
                if (candidates.Count == 0)
                {
                    StopSpectateAudio();
                    HintLine = autoSpec ? "SPECTATING — waiting for players…" : null;
                    if (dead) HoldingFailScreen = true;
                    return;
                }
                _targetIndex = ((_targetIndex % candidates.Count) + candidates.Count) % candidates.Count;
                var target = candidates[_targetIndex];
                string switchHint = candidates.Count > 1 ? "  ◄ ► switch" : "";

                if (dead)
                {
                    HoldingFailScreen = true;
                    HintLine = $"SPECTATING {target.Name} — R to retry{switchHint}";
                }
                else
                {
                    HintLine = $"SPECTATING {target.Name}{switchHint}";
                }

                // While dead the level is frozen (no music) so play the target's synced audio;
                // during autoplay the game already plays it.
                if (dead) UpdateSpectateAudio(target);
                else StopSpectateAudio();

                // Follow the selected player's ghost so the camera tracks THEM (not the autoplay
                // planet / your frozen corpse). Set both the smoothing target and the position.
                var floors = ADOBase.lm?.listFloors;
                if (floors == null || floors.Count == 0) return;
                if (!GhostOverlay.TryGetGhostPosition(target, floors, out var stationary, out var orbit, out _)) return;

                var cam = controller.camy;
                if (cam == null) return;
                Vector3 focus = (stationary + orbit) * 0.5f;
                cam.topos = new Vector2(focus.x, focus.y);
                var t = cam.transform;
                float lerp = autoSpec ? 10f : 6f;
                t.position = Vector3.Lerp(t.position, new Vector3(focus.x, focus.y, t.position.z), lerp * Time.deltaTime);
            }
            catch
            {
                // camera plumbing changed mid-scene — just don't spectate this frame
            }
        }
    }
}
