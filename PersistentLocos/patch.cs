using System;
using System.Reflection;
using DV;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace PersistentLocos
{
    [HarmonyPatch(typeof(StartGameData_NewCareer), "PrepareNewSaveData")]
    public static class StartGameData_NewCareerPatch
    {
        static void Postfix()
        {
            LocoSpawnState.Reset();
            Debug.Log("[PersistentLocos] New career detected – counter reset.");
        }
    }

    [HarmonyPatch(typeof(UnusedTrainCarDeleter), "AreDeleteConditionsFulfilled")]
	class Patch_AreDeleteConditionsFulfilled
	{
		static bool Prefix(TrainCar trainCar, ref bool __result)
		{
			var livery = trainCar.carLivery;
			bool isLoco = trainCar.IsLoco;
			bool isTender = CarTypes.IsTender(livery);

			if (isLoco || isTender)
			{
				__result = false;
				return false;
			}

			return true;
		}
	}
	
	[HarmonyPatch]
	class Patch_CarSpawner_SpawnCar_Blocker
	{
		static MethodBase TargetMethod()
		{
			var railTrackType = AccessTools.TypeByName("RailTrack");
			return typeof(CarSpawner).GetMethod("SpawnCar", new[] {
				typeof(GameObject), railTrackType, typeof(Vector3), typeof(Vector3),
				typeof(bool), typeof(bool)
			});
		}

		static bool Prefix(GameObject carToSpawn, bool playerSpawnedCar, ref TrainCar __result)
		{
			if (carToSpawn == null) 
				return true;

			var trainCar = carToSpawn.GetComponent<TrainCar>();
			if (trainCar == null) 
				return true;

			var livery = trainCar.carLivery;
			bool isLoco = trainCar.IsLoco;
			bool isTender = CarTypes.IsTender(livery);
			
			if (!isLoco && !isTender)
				return true;

			if (playerSpawnedCar) 
				return true;

			if (LocoSpawnState.Count >= Main.settings.LocoLimit)
			{
				Debug.Log("[PersistentLocos] Maximum locomotive limit reached – spawn blocked.");
				__result = null;
				return false;
			}

			return true;
		}
	}

    [HarmonyPatch]
	class Patch_CarSpawner_SpawnCar_Logger
	{
		static MethodBase TargetMethod()
		{
			var railTrackType = AccessTools.TypeByName("RailTrack");
			return typeof(CarSpawner).GetMethod("SpawnCar", new[] {
				typeof(GameObject), railTrackType, typeof(Vector3), typeof(Vector3),
				typeof(bool), typeof(bool)
			});
		}

		static void Postfix(TrainCar __result, bool playerSpawnedCar)
		{
			if (__result == null || !__result.IsLoco || playerSpawnedCar) return;

			LocoSpawnState.Increment();
			Debug.Log($"[PersistentLocos] Locomotive #{LocoSpawnState.Count} registered Type: {__result.carLivery?.id}, ID: {__result.ID}");
		}
	}
	
	[HarmonyPatch(typeof(SaveGameManager), "Start")]
	class Patch_SaveGameManager_Start
	{
		static void Postfix(SaveGameManager __instance)
		{
			__instance.OnInternalDataUpdate += (saveData) =>
			{
				LocoSpawnState.SaveTo(saveData);
				Debug.Log($"[PersistentLocos] Saved locomotive count to save: {LocoSpawnState.Count}");
			};
		}
	}
	
	[HarmonyPatch(typeof(StartGameData_FromSaveGame), "MakeCurrent")]
	class Patch_StartGameData_FromSaveGame_MakeCurrent
	{
		static void Postfix(StartGameData_FromSaveGame __instance)
		{
			var saveData = __instance.GetSaveGameData();
			if (saveData != null)
			{
				LocoSpawnState.LoadFrom(saveData);
				Debug.Log($"[PersistentLocos] Loaded locomotive count from save (late): {LocoSpawnState.Count}");
			}
			else
			{
				Debug.LogWarning("[PersistentLocos] SaveGameData is null – cannot load counter (late)");
			}
		}
	}
}