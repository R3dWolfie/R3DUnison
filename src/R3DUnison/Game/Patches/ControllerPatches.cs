using HarmonyLib;

namespace R3DUnison.Game.Patches
{
    /// <summary>
    /// While spectating from the fail screen, swallow the "press any key to restart"
    /// input so a reflex tap doesn't yank the camera back — R retries explicitly.
    /// </summary>
    [HarmonyPatch(typeof(scrController), "Fail2_Update")]
    internal static class FailInputPatch
    {
        private static bool Prefix()
        {
            return !UI.SpectatorCam.HoldingFailScreen;
        }
    }
}
