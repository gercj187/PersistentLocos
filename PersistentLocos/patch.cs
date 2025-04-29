using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentLocos
{
	[HarmonyPatch(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled")]
	class Patch_AreDeleteConditionsFulfilled
	{
		static bool Prefix(TrainCar trainCar, ref bool __result)
		{
			if (trainCar.IsLoco)
			{
				__result = false; // Lokomotiven dürfen NICHT gelöscht werden
				return false;     // Originalfunktion überspringen
			}
			return true; // Für Waggons normale Prüfung ausführen
		}
	}
}
