using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PersistentLocos
{
    /// <summary>
    /// Activates LO-related overrides only if the LocoOwnership assembly is present.
    /// If LocoOwnership is not active, this class remains completely inert (no patch, no log).
    /// </summary>
    internal static class LocoOwnership_Override
    {
        private static bool _patched;
        private static Harmony _harmony;

        /// <summary>
        /// Attempts to apply patches, but only when loAsm != null (i.e., LocoOwnership is active).
        /// </summary>
        public static void TryPatch(Assembly loAsm = null)
        {
            // Already patched? Do nothing.
            if (_patched) return;

            // If LO is not active, return immediately without any logging or side effects.
            if (loAsm == null) return;

            try
            {
                _harmony ??= new Harmony("com.chris.persistentlocos.looverride");
                // If there are LO-specific patches to add, they should be placed here.
                // Currently, initialization is enough because other LO integrations
                // are handled via reflection in other classes.

                _patched = true;

                // Optional debug log only if explicitly enabled in settings.
                if (Main.Settings?.enableLogging == true)
                    Main.Log("[PLP] LocoOwnership detected – LO override enabled.");
            }
            catch (Exception ex)
            {
                // Non-critical errors – log only if debugging is enabled.
                if (Main.Settings?.enableLogging == true)
                    Main.Log("[PLP] LocoOwnership override init failed (non-fatal): " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Helper class: finds the LO assembly and triggers patching only if it's present.
    /// </summary>
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

    /// <summary>
    /// Dummy helper to ensure calls always exist – without hard dependency on LO or Plus.Helpers.
    /// If Plus.Helpers is available, its implementation will be used instead.
    /// </summary>
    internal static class Helpers
    {
        public static void RefreshPitStopsForAllSelected()
        {
            // no-op fallback; real implementation is in PersistentLocos.Plus.Helpers
        }
    }
}
