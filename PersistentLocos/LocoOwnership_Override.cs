using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PersistentLocos
{
    internal static class LocoOwnership_Override
    {
        private static bool _patched;
        private static Harmony _harmony;

        public static void TryPatch(Assembly loAsm = null)
        {
            if (_patched) return;

            if (loAsm == null) return;

            try
            {
                _harmony ??= new Harmony("com.chris.persistentlocos.looverride");

                _patched = true;

                if (Main.Settings?.enableLogging == true)
                    Main.Log(" LocoOwnership detected – LO override enabled.");
            }
            catch (Exception ex)
            {
                if (Main.Settings?.enableLogging == true)
                    Main.Log(" LocoOwnership not installed: " + ex.Message);
            }
        }
    }
	
    internal static class LOPatcher
    {
        public static void Init()
        {
            LocoOwnership_Override.TryPatch(FindLoAsm());
        }

        public static void TryPatch(object _ = null)
        {
            LocoOwnership_Override.TryPatch(FindLoAsm());
        }

        private static Assembly FindLoAsm()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name;
                if (!string.IsNullOrEmpty(n) && n.StartsWith("LocoOwnership", StringComparison.OrdinalIgnoreCase))
                    return asm;
            }
            return null;
        }
    }
	
    internal static class Helpers
    {
        public static void RefreshPitStopsForAllSelected(){}
    }
}
