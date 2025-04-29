using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace PersistentLocos;

public static class Main
{
	public static bool Load(UnityModManager.ModEntry modEntry)
	{
		var harmony = new Harmony(modEntry.Info.Id);
		harmony.PatchAll();
		modEntry.Logger.Log("[PersistentLocos] Harmony patches applied.");
		return true;
	}
}
