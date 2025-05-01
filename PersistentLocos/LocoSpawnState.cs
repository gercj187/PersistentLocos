using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentLocos
{
    public static class LocoSpawnState
    {
        private static string SavePath => Path.Combine(UnityModManager.modsPath, "PersistentLocos", "locoState.json");

        private static int _count = 0;
        public static int Count => _count;

        public static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    string json = File.ReadAllText(SavePath);
                    _count = JsonConvert.DeserializeObject<int>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[PersistentLocos] Failed to load counter: {ex}");
                _count = 0;
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_count, Formatting.Indented);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception ex)
            {
                Debug.Log($"[PersistentLocos] Failed to save counter: {ex}");
            }
        }

        public static void Reset()
        {
            _count = 0;
            Save();
        }

        public static void Increment()
        {
            _count++;
            Save();
        }
    }
}
