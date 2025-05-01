using System;
using System.Reflection;
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
            if (trainCar.IsLoco)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    class Patch_CarSpawner_SpawnCar
    {
        static MethodBase TargetMethod()
        {
            var railTrackType = AccessTools.TypeByName("RailTrack");
            return typeof(CarSpawner).GetMethod("SpawnCar", new[] {
                typeof(GameObject), railTrackType, typeof(Vector3), typeof(Vector3),
                typeof(bool), typeof(bool)
            });
        }

        static bool Prefix(GameObject carToSpawn, bool playerSpawnedCar)
        {
            if (carToSpawn == null) return true;

            var trainCar = carToSpawn.GetComponent<TrainCar>();
            if (trainCar == null || !trainCar.IsLoco) return true;

            if (playerSpawnedCar)
                return true;

            if (LocoSpawnState.Count >= Main.settings.LocoLimit)
            {
                Debug.Log("[PersistentLocos] Maximum locomotive limit reached – spawn blocked.");
                return false;
            }

            LocoSpawnState.Increment();
            Debug.Log($"[PersistentLocos] Locomotive #{LocoSpawnState.Count} registered: {trainCar.carLivery?.id}");
            return true;
        }
    }
}
