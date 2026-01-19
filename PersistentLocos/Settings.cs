using System;
using UnityModManagerNet;

namespace PersistentLocos
{
    [Serializable]
    public class Settings : UnityModManager.ModSettings
    {
        public int LocoLimit = 31;

        public bool enablePersistentDamage = true;
        
        public bool assumeNonOwnedWhenUnknown = true;

        // --- Unowned multiplier ---
        public bool enableUnownedServiceMultiplier = true;
        public float unownedServiceMultiplier = 1.5f;

        // --- Repair Without License ---
        public bool enableRepairWithoutLicense = true;
        public float repairWithoutLicenseMultiplier = 2.0f;

        public bool enableLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}