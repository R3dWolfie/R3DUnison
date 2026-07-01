using HarmonyLib;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace R3DUnison
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod { get; private set; }
        public static bool Enabled { get; private set; }

        private static Harmony _harmony;

        // UMM entry point (Info.json EntryMethod)
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
            return true;
        }

        private static UnityEngine.GameObject _windowGo;

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (value)
            {
                _harmony = new Harmony(modEntry.Info.Id);
                _harmony.PatchAll(typeof(Main).Assembly);
                Core.MainThreadDispatcher.Ensure();
                Session.RoomManager.Init();
                _windowGo = new UnityEngine.GameObject("R3DUnison.MultiplayerWindow");
                UnityEngine.Object.DontDestroyOnLoad(_windowGo);
                _windowGo.AddComponent<UI.MultiplayerWindow>();
                SceneManager.sceneLoaded += OnSceneLoaded;
                Log("Enabled.");
            }
            else
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                if (_windowGo != null)
                {
                    UnityEngine.Object.Destroy(_windowGo);
                    _windowGo = null;
                }
                Session.RoomManager.Shutdown();
                _harmony?.UnpatchAll(modEntry.Info.Id);
                _harmony = null;
                Log("Disabled.");
            }
            return true;
        }

        private static void OnGui(UnityModManager.ModEntry modEntry)
        {
            var rm = Session.RoomManager.Instance;
            string state = rm == null ? "off" : !rm.SteamReady ? "waiting for Steam" : rm.InRoom ? $"in room ({rm.Members.Count} players)" : "idle";
            UnityEngine.GUILayout.Label($"R3D Unison {modEntry.Info.Version} — press F9 in-game for the multiplayer window. State: {state}");
        }

        // M0 smoke signal: proves we're alive inside the engine across scene changes
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log($"Scene loaded: {scene.name} ({mode})");
        }

        public static void Log(string message) => Mod?.Logger.Log(message);
        public static void LogError(string message) => Mod?.Logger.Error(message);
    }
}
