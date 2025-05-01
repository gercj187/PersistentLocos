using UnityModManagerNet;

namespace PersistentLocos
{
    public class Settings : UnityModManager.ModSettings
    {
        public int LocoLimit = 29;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
