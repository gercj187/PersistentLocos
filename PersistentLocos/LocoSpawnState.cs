using System;
using System.IO;
using DV;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentLocos
{
    public static class LocoSpawnState
    {
        private static int _count = 0;
        public static int Count => _count;

        private const string CountSaveKey = "PersistentLocos_LocoCount";
        private const string LimitSaveKey = "PersistentLocos_LocoLimit";

        public static void LoadFrom(SaveGameData saveData)
        {
            int? maybeCount = saveData.GetInt(CountSaveKey);
            _count = maybeCount ?? 0;

            int? maybeLimit = saveData.GetInt(LimitSaveKey);
            if (maybeLimit.HasValue)
            {
                Main.settings.LocoLimit = maybeLimit.Value;
                Debug.Log($"[PersistentLocos] Loaded LocoLimit from save: {Main.settings.LocoLimit}");
            }
            else
            {
                Debug.LogWarning("[PersistentLocos] No LocoLimit saved – using default or settings value");
            }

            Debug.Log($"[PersistentLocos] Loaded locomotive count from save: {_count}");
        }

        public static void SaveTo(SaveGameData saveData)
        {
            saveData.SetInt(CountSaveKey, _count);
            saveData.SetInt(LimitSaveKey, Main.settings.LocoLimit);

            //Debug.Log($"[PersistentLocos] Saved locomotive count: {_count}");
            //Debug.Log($"[PersistentLocos] Saved LocoLimit: {Main.settings.LocoLimit}");
        }

        public static void Reset()
        {
            _count = 0;
        }

        public static void Increment()
        {
            _count++;
        }
    }
}
