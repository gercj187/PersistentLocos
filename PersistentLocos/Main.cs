using System;
using System.Reflection;
using System.Collections;
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
		private static bool _warmUpStarted = false;
		private static bool _runtimeRepairWithoutLicense;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
			
			_runtimeRepairWithoutLicense  = Settings.enableRepairWithoutLicense;

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = OnUpdate;

            _harmony = new Harmony("com.chris.persistentlocos");
            return true;
        }
		
		private static bool RestartRequired()
		{
			return
			  Settings.enableRepairWithoutLicense != _runtimeRepairWithoutLicense;
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

			Settings.enablePersistentDamage = GUILayout.Toggle(Settings.enablePersistentDamage,"Enable persistent damage");
			GUILayout.Label("(Removes all locomotives from fees and from automatic maintenance)");

            GUILayout.Space(10);
            Settings.enableUnownedServiceMultiplier = GUILayout.Toggle(Settings.enableUnownedServiceMultiplier,"Apply service multiplier to unowned locomotives");

            if (Settings.enableUnownedServiceMultiplier)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Service multiplier: {Settings.unownedServiceMultiplier:0.0}x");
                GUILayout.Label($"(Increases Manual Service costs for locomotives you do not own — excluding demonstrators or those purchased via LocoOwnership Mod)");
                float raw = GUILayout.HorizontalSlider(Settings.unownedServiceMultiplier, 1.0f, 5.0f, GUILayout.Width(480));
                float snapped = Mathf.Clamp((float)Math.Round(raw * 2f) / 2f, 1.0f, 5.0f);
                Settings.unownedServiceMultiplier = snapped;
            }

            GUILayout.Space(10);
			
			Settings.enableRepairWithoutLicense = GUILayout.Toggle(Settings.enableRepairWithoutLicense,"Repair Without License");

			if (Settings.enableRepairWithoutLicense)
			{
				GUILayout.Space(5);
				GUILayout.Label($"Price multiplier (No Manual Service license): {Settings.repairWithoutLicenseMultiplier:0.0}x");
				float raw2 = GUILayout.HorizontalSlider(Settings.repairWithoutLicenseMultiplier,1.5f,10.0f,GUILayout.Width(480));
				float snapped2 = Mathf.Clamp((float)Math.Round(raw2 * 2f) / 2f,1.5f,10.0f);
				Settings.repairWithoutLicenseMultiplier = snapped2;
			}
			
			if (RestartRequired())
			{
				GUILayout.Space(10);

				var oldColor = GUI.backgroundColor;
				GUI.backgroundColor = new Color(1.5f, 0.05f, 0.05f);

				GUIStyle restartStyle = new GUIStyle(GUI.skin.button);
				restartStyle.fontSize = 14;
				restartStyle.alignment = TextAnchor.MiddleCenter;
				restartStyle.fontStyle = FontStyle.Bold;
				restartStyle.normal.textColor = Color.white;
				restartStyle.hover.textColor  = Color.white;
				restartStyle.active.textColor = Color.white;

				GUILayout.Button("PLEASE RESTART DERAIL VALLEY!",restartStyle,GUILayout.Height(32),GUILayout.Width(480));

				GUI.backgroundColor = oldColor;
			}

            GUILayout.Space(10);
            Settings.enableLogging = GUILayout.Toggle(Settings.enableLogging, "Enable debug logging");
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
			Settings.Save(modEntry);
			Log("Settings saved.");
        }

        static float _timeSinceStart = 0f;
        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
			if (!_warmUpStarted && Time.timeSinceLevelLoad > 2f)
			{
				_warmUpStarted = true;
				CoroutineDispatcher.Instance.RunCoroutine(WarmUpCoroutine());
			}
			
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
            _timeSinceStart = float.MaxValue;
        }

        public static void Log(string msg)
        {
            if (Settings != null && !Settings.enableLogging) return;
            Mod?.Logger.Log("[PersistentLocos] " + msg);
        }
        public static void Warn(string msg) => Mod?.Logger.Warning("[Warning] " + msg);
        public static void Error(string msg) => Mod?.Logger.Error("[ERROR] " + msg);
		
		private static IEnumerator WarmUpCoroutine()
		{
			yield return new WaitForSeconds(0.3f);

			try
			{
				PersistentLocos.Plus.Ownership.TryIsOwned(null, out _);

				AccessTools.TypeByName("PitStopStation");
				AccessTools.TypeByName("PitStopIndicators");
				AccessTools.TypeByName("CashRegisterModule");

				PersistentLocos.Plus.Helpers.HasManualServiceLicense();
			}
			catch { }

			if (Settings.enableLogging)
				Log("Warm-Up completed");
		}
    }
}
