using UnityModManagerNet;

namespace PersistentLocos
{
    public class Settings : UnityModManager.ModSettings
    {
        public int LocoLimit = 30;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
