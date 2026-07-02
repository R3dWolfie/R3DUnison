using UnityModManagerNet;

namespace R3DUnison
{
    public class UnisonSettings : UnityModManager.ModSettings
    {
        /// <summary>Announce levels I play as joinable public rooms.</summary>
        public bool AutoAnnounce = true;

        /// <summary>Host starting a level pulls everyone in and starts them simultaneously.</summary>
        public bool SyncedStarts = true;

        /// <summary>Host preference: room mode applied when creating a room (RoomMode enum value).</summary>
        public int RoomModePref = 0;

        /// <summary>Host preference: chart speed multiplier applied when creating a room.</summary>
        public float RoomSpeedPref = 1f;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
    }
}
