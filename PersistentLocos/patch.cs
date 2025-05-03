using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using DV;
using DV.Utils;
using DV.Player;
using DV.Common;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.UserManagement;
using DV.Scenarios.Common;
using DV.Localization.Debug;
using DV.JObjectExtstensions;
using DV.ThingTypes.TransitionHelpers;
using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace PersistentLocos
{
    [HarmonyPatch(typeof(StartGameData_NewCareer), "PrepareNewSaveData")]
    public static class StartGameData_NewCareerPatch
    {
        static void Postfix()
        {
            LocoSpawnState.Reset();
            Debug.Log("[PersistentLocos] New career detected – counter reset.");
			Debug.Log($"[PersistentLocos] Starting new game. LocoLimit (from settings): {Main.settings.LocoLimit}");
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
				//Debug.Log("[PersistentLocos] Maximum locomotive limit reached – spawn blocked.");
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
                //Debug.Log($"[PersistentLocos] Saved locomotive count to save: {LocoSpawnState.Count}");
                //Debug.Log($"[PersistentLocos] Saved LocoLimit to save: {Main.settings.LocoLimit}");
			};
		}
	}
		
	[HarmonyPatch]
	public static class Patch_StationLocoSpawnerDistance
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(SaveGameManager), "Start");
		}

		static void Postfix()
		{
			CoroutineDispatcher.Instance.RunCoroutine(AdjustSpawnerDistances());
		}

		static IEnumerator AdjustSpawnerDistances()
		{
			yield return new WaitUntil(() => AStartGameData.carsAndJobsLoadingFinished);
			yield return new WaitForSeconds(5f);

			var spawners = GameObject.FindObjectsOfType<StationLocoSpawner>();
			foreach (var spawner in spawners)
			{
				spawner.spawnLocoPlayerSqrDistanceFromTrack = 625000000f; // 25
			}

			//Debug.Log($"[PersistentLocos] Temporarily set spawn distance to 25km on {spawners.Length} StationLocoSpawner(s).");

			yield return new WaitForSeconds(5f);

			foreach (var spawner in spawners)
			{
				spawner.spawnLocoPlayerSqrDistanceFromTrack = 25000000f; // 5 km
			}

			//Debug.Log($"[PersistentLocos] Reset spawn distance to 5km on {spawners.Length} StationLocoSpawner(s).");
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