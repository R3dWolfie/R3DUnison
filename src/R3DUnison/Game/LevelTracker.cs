using System;
using System.IO;

namespace R3DUnison.Game
{
    public class LevelPresence : IEquatable<LevelPresence>
    {
        /// <summary>Stable identity for matchmaking ("official:1-X" / "custom:&lt;id&gt;").</summary>
        public string Key;
        public string Display;
        public bool IsCustom;

        public bool Equals(LevelPresence other) => other != null && other.Key == Key;
        public override bool Equals(object obj) => Equals(obj as LevelPresence);
        public override int GetHashCode() => Key?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Frame-polled "what level is the player in right now". Deliberately patch-free:
    /// reading ADOBase/scrController state survives game updates far better than
    /// Harmony-patching lifecycle methods. Retries don't change presence (same level),
    /// which is exactly what auto-rooms want.
    /// </summary>
    public static class LevelTracker
    {
        public static LevelPresence Current { get; private set; }
        public static event Action<LevelPresence> Entered;
        public static event Action Exited;

        /// <summary>Realtime timestamp of the last presence→none transition (retry heuristics).</summary>
        public static float LastExitRealtime { get; private set; } = -1000f;

        private static bool _started;

        /// <summary>On-demand detection (used by the StartMusic gate, which can fire before the frame poll).</summary>
        public static LevelPresence TryDetect()
        {
            try
            {
                return Detect();
            }
            catch
            {
                return null;
            }
        }

        public static void Start()
        {
            if (_started) return;
            _started = true;
            Core.MainThreadDispatcher.OnFrame += Poll;
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            Core.MainThreadDispatcher.OnFrame -= Poll;
            Current = null;
        }

        private static void Poll()
        {
            LevelPresence now = null;
            string detectError = null;
            try
            {
                now = Detect();
            }
            catch (Exception e)
            {
                // scene mid-transition; treat as no presence — but never silently
                detectError = $"{e.GetType().Name}: {e.Message}";
            }
            DebugTrace(detectError);
            bool changed = Current == null ? now != null : !Current.Equals(now);
            if (!changed) return;
            if (now == null && Current != null) LastExitRealtime = UnityEngine.Time.realtimeSinceStartup;
            Current = now;
            if (now != null) Entered?.Invoke(now);
            else Exited?.Invoke();
        }

        private static string _lastTrace;

        // Logs the raw inputs of Detect() whenever they change — cheap, and it turns
        // "presence silently says none" into a readable line in the UMM log.
        private static void DebugTrace(string detectError)
        {
            string state;
            try
            {
                var c = scrController.instance;
                state = c == null
                    ? "controller=null"
                    : $"scene='{ADOBase.sceneName}' gameworld={c.gameworld} puzzle={c.isPuzzleRoom} lvl='{c.levelName}' world='{scrController.currentWorldString}' editor={ADOBase.isLevelEditor} freeroam={ADOBase.isFreeroamScene} scnGame={ADOBase.isScnGame} internal={ADOBase.isInternalLevel}";
            }
            catch (Exception e)
            {
                state = $"trace-failed {e.GetType().Name}: {e.Message}";
            }
            if (detectError != null) state += $" | DETECT ERROR: {detectError}";
            if (state == _lastTrace) return;
            _lastTrace = state;
            Main.Log($"[presence] {state}");
        }

        private static LevelPresence Detect()
        {
            var controller = scrController.instance;
            if (controller == null || !controller.gameworld) return null;
            if (ADOBase.isLevelEditor || ADOBase.isFreeroamScene) return null;

            if (ADOBase.isScnGame && !ADOBase.isInternalLevel)
            {
                // Custom level: prefer the stable id, fall back to the level folder name
                string path = ADOBase.levelPath;
                string display = null;
                if (!string.IsNullOrEmpty(path))
                {
                    display = Path.GetFileName(Path.GetDirectoryName(path));
                    if (string.IsNullOrEmpty(display)) display = Path.GetFileNameWithoutExtension(path);
                }
                string id = GCS.customLevelId;
                string key = "custom:" + (string.IsNullOrEmpty(id) ? display : id);
                if (string.IsNullOrEmpty(display)) display = string.IsNullOrEmpty(id) ? "custom level" : id;
                if (key == "custom:") return null;
                return new LevelPresence { Key = key, Display = display, IsCustom = true };
            }

            string name = controller.levelName;
            if (string.IsNullOrEmpty(name)) name = scrController.currentWorldString;
            if (string.IsNullOrEmpty(name)) return null;
            return new LevelPresence { Key = "official:" + name, Display = name, IsCustom = false };
        }
    }
}
