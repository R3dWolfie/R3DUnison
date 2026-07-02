using System;
using HarmonyLib;

namespace R3DUnison.Game.Patches
{
    /// <summary>
    /// The synced-start gate. StartMusic is the moment a loaded level goes live
    /// (music schedules → Get Ready countdown → run); deferring it holds the level
    /// silently at the start until the room says GO.
    /// </summary>
    [HarmonyPatch(typeof(scrConductor), nameof(scrConductor.StartMusic))]
    internal static class ConductorStartMusicPatch
    {
        private static bool Prefix(scrConductor __instance, Action onComplete, Action onSongScheduled)
        {
            try
            {
                return Session.SyncedStart.OnStartMusic(__instance, onComplete, onSongScheduled);
            }
            catch (Exception e)
            {
                Main.LogError($"StartMusic gate threw — letting the level run: {e}");
                return true;
            }
        }
    }
}
