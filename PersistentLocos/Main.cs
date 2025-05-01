using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;

namespace PersistentLocos
{
    static class Main
    {
        public static Settings settings;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            modEntry.Logger.Log($"[PersistentLocos] Counter loaded {LocoSpawnState.Count} registered locomotives...");

            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Maximum number of locomotives:");
            settings.LocoLimit = (int)GUILayout.HorizontalSlider(settings.LocoLimit, 1, 50);
            GUILayout.Label($"Current limit: {settings.LocoLimit}");
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }
    }
}
