using System;
using UnityModManagerNet;

namespace PersistentLocos
{
    [Serializable]
    public class Settings : UnityModManager.ModSettings
    {
        public int LocoLimit = 31;

        public bool enablePersistentDamage = true;

        public bool blockLocomotiveFees = true;
        public bool assumeNonOwnedWhenUnknown = true;
        public bool overrideLocoOwnershipFees = true;

        public bool enableUnownedServiceMultiplier = true;
        public float unownedServiceMultiplier = 2.0f;
        public float serviceCostMultiplierForNonOwned = 2.0f;

        public bool enableLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            serviceCostMultiplierForNonOwned = unownedServiceMultiplier;
            Save(this, modEntry);
        }
    }
}