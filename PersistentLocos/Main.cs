using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentLocos
{
    public static class Main
    {
        public static UnityModManager.ModEntry Mod;
        public static Settings Settings;
        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;

            _harmony = new Harmony("com.chris.persistentlocos");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            try
            {
                if (enabled)
                {
                    _harmony.PatchAll(Assembly.GetExecutingAssembly());
                    Log("Enabled");
                }
                else
                {
                    _harmony.UnpatchAll(_harmony.Id);
                    Log("Disabled");
                }
                return true;
            }
            catch (Exception ex)
            {
                Error("Toggle failed: " + ex);
                return false;
            }
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Settings == null) return;

            GUILayout.Space(5);
            GUILayout.Label($"Maximum number of locomotives (Default = 31) : {Settings.LocoLimit}");
            int newLimit = Mathf.RoundToInt(GUILayout.HorizontalSlider(Settings.LocoLimit, 1, 50, GUILayout.Width(480)));
            Settings.LocoLimit = Mathf.Clamp(newLimit, 1, 50);

            GUILayout.Space(5);

            // --- BEGIN: Sync persistent damage <-> blockLocomotiveFees ---
            bool prevPersistent = Settings.enablePersistentDamage;
            Settings.enablePersistentDamage = GUILayout.Toggle(Settings.enablePersistentDamage, "Enable persistent damage");
            GUILayout.Label("(Removes all locomotives from fees and from automatic maintenance)");

            if (Settings.enablePersistentDamage != prevPersistent)
            {
                // Wenn persistenter Schaden an ist, blocken wir loco fees; wenn aus, geben wir sie wieder frei.
                Settings.blockLocomotiveFees = Settings.enablePersistentDamage;
                Log($"Persistent damage {(Settings.enablePersistentDamage ? "enabled" : "disabled")} -> blockLocomotiveFees = {Settings.blockLocomotiveFees}");

                // UI sofort aktualisieren, damit Preise/Fees in der PitStop-UI korrekt sind
                try
                {
                    PersistentLocos.Plus.Helpers.RefreshPitStopsForAllSelected();
                }
                catch (Exception ex)
                {
                    Warn("PitStop UI refresh after toggle failed: " + ex.Message);
                }
            }
            // --- END: Sync persistent damage <-> blockLocomotiveFees ---

            GUILayout.Space(10);
            bool wasEnabled = Settings.enableUnownedServiceMultiplier;
            Settings.enableUnownedServiceMultiplier = GUILayout.Toggle(
                Settings.enableUnownedServiceMultiplier,
                "Apply service multiplier to unowned locomotives"
            );

            if (Settings.enableUnownedServiceMultiplier)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Service multiplier: {Settings.unownedServiceMultiplier:0.0}x");
                GUILayout.Label($"(Increases Manual Service costs for locomotives you do not own — excluding demonstrators or those purchased via LocoOwnership Mod)");
                float raw = GUILayout.HorizontalSlider(Settings.unownedServiceMultiplier, 1.0f, 5.0f, GUILayout.Width(480));
                float snapped = Mathf.Clamp((float)Math.Round(raw * 2f) / 2f, 1.0f, 5.0f);
                Settings.unownedServiceMultiplier = snapped;

                Settings.serviceCostMultiplierForNonOwned = Settings.unownedServiceMultiplier;
            }
            else if (wasEnabled)
            {
                Settings.serviceCostMultiplierForNonOwned = Settings.unownedServiceMultiplier;
            }

            GUILayout.Space(5);
            Settings.enableLogging = GUILayout.Toggle(Settings.enableLogging, "Enable debug logging");
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            try
            {
                // Alias sicher synchronisieren
                Settings.serviceCostMultiplierForNonOwned = Settings.unownedServiceMultiplier;

                // Safety: blockLocomotiveFees folgt immer persistentDamage
                Settings.blockLocomotiveFees = Settings.enablePersistentDamage;

                Settings.Save(modEntry);
                Log("Settings saved.");
            }
            catch (Exception ex)
            {
                Error("Settings save failed: " + ex);
            }
        }

        // Optional: einmalige LO-Override-Initialisierung nach Start
        static float _timeSinceStart = 0f;
        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            _timeSinceStart += dt;
            if (_timeSinceStart < 6f) return;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (!string.IsNullOrEmpty(name) && name.StartsWith("LocoOwnership", StringComparison.OrdinalIgnoreCase))
                    {
                        LocoOwnership_Override.TryPatch(asm);
                        break;
                    }
                }
            }
            catch { }
            _timeSinceStart = float.MaxValue; // nur einmal
        }

        public static void Log(string msg)
        {
            if (Settings != null && !Settings.enableLogging) return;
            Mod?.Logger.Log("[PersistentLocos] " + msg);
        }
        public static void Warn(string msg) => Mod?.Logger.Warning("[PersistentLocos] " + msg);
        public static void Error(string msg) => Mod?.Logger.Error("[PersistentLocos] " + msg);
    }
}
