using UnityModManagerNet;

namespace R3DUnison
{
    public class UnisonSettings : UnityModManager.ModSettings
    {
        /// <summary>Announce levels I play as joinable public rooms.</summary>
        public bool AutoAnnounce = true;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
    }
}
