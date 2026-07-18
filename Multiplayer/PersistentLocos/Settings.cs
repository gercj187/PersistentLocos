using System;
using UnityModManagerNet;

namespace PersistentLocos
{
    [Serializable]
    public class Settings : UnityModManager.ModSettings
    {
        public int LocoLimit = 31;

        public bool enablePersistentDamage = true;
		
        public bool enableUnownedServiceMultiplier = true;
        public float unownedServiceMultiplier = 1.5f;

        public bool enableRepairWithoutLicense = true;
        public float repairWithoutLicenseMultiplier = 2.0f;
		public bool enableLogging = false;

        public Settings Clone()
        {
            return new Settings
            {
                LocoLimit = LocoLimit,
                enablePersistentDamage = enablePersistentDamage,
                enableUnownedServiceMultiplier = enableUnownedServiceMultiplier,
                unownedServiceMultiplier = unownedServiceMultiplier,
                enableRepairWithoutLicense = enableRepairWithoutLicense,
                repairWithoutLicenseMultiplier = repairWithoutLicenseMultiplier,
                enableLogging = enableLogging
            };
        }

        public void CopyFrom(Settings source)
        {
            if (source == null)
                return;

            LocoLimit = source.LocoLimit;
            enablePersistentDamage = source.enablePersistentDamage;
            enableUnownedServiceMultiplier = source.enableUnownedServiceMultiplier;
            unownedServiceMultiplier = source.unownedServiceMultiplier;
            enableRepairWithoutLicense = source.enableRepairWithoutLicense;
            repairWithoutLicenseMultiplier = source.repairWithoutLicenseMultiplier;
            enableLogging = source.enableLogging;
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
			
            if (PL_Multiplayer.IsClient)
                return;

            Save(this,modEntry);
        }
    }
}