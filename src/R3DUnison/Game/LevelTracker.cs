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

        private static bool _started;

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
            try
            {
                now = Detect();
            }
            catch
            {
                // scene mid-transition; treat as no presence
            }
            bool changed = Current == null ? now != null : !Current.Equals(now);
            if (!changed) return;
            Current = now;
            if (now != null) Entered?.Invoke(now);
            else Exited?.Invoke();
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
