using DV.Booklets;
using DV.Common;
using DV.JObjectExtstensions;
using DV.Localization.Debug;
using DV.Logic.Job;
using DV.Player;
using DV.Scenarios.Common;
using DV.ThingTypes.TransitionHelpers;
using DV.ThingTypes;
using DV.UserManagement;
using DV.Utils;
using DV.ServicePenalty;
using DV;
using HarmonyLib;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityModManagerNet;
using System.IO;
using System.Text;

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
            Debug.Log($"[PersistentLocos] Starting new game. LocoLimit (from settings): {Main.Settings.LocoLimit}");
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

            if (LocoSpawnState.Count >= Main.Settings.LocoLimit)
            {
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
            if (__result.brakeSystem != null && __result.brakeSystem.hasHandbrake)
            {
                __result.brakeSystem.SetHandbrakePosition(1f);
                Debug.Log($"[PersistentLocos] Handbrake fully applied for locomotive ID: {__result.ID}");
            }
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
                spawner.spawnLocoPlayerSqrDistanceFromTrack = 625000000f;
            }

            yield return new WaitForSeconds(5f);

            foreach (var spawner in spawners)
            {
                spawner.spawnLocoPlayerSqrDistanceFromTrack = 25000000f;
            }
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
	
    public class CoroutineDispatcher : MonoBehaviour
    {
        private static CoroutineDispatcher _instance;

        public static CoroutineDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("PersistentLocos_CoroutineDispatcher");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineDispatcher>();
                }
                return _instance;
            }
        }

        public void RunCoroutine(IEnumerator coroutine)
        {
            StartCoroutine(coroutine);
        }
    }

    public static class LocoSpawnState
    {
        private static int _count = 0;
        public static int Count => _count;

        private const string CountSaveKey = "PersistentLocos_LocoCount";
        private const string LimitSaveKey = "PersistentLocos_LocoLimit";

        public static void LoadFrom(SaveGameData saveData)
        {
            int? maybeCount = saveData.GetInt(CountSaveKey);
            _count = maybeCount ?? 0;

            int? maybeLimit = saveData.GetInt(LimitSaveKey);
            if (maybeLimit.HasValue)
            {
                Main.Settings.LocoLimit = maybeLimit.Value;
                Debug.Log($"[PersistentLocos] Loaded LocoLimit from save: {Main.Settings.LocoLimit}");
            }
            else
            {
                Debug.LogWarning("[PersistentLocos] No LocoLimit saved – using default or settings value");
            }

            Debug.Log($"[PersistentLocos] Loaded locomotive count from save: {_count}");
        }

        public static void SaveTo(SaveGameData saveData)
        {
            saveData.SetInt(CountSaveKey, _count);
            saveData.SetInt(LimitSaveKey, Main.Settings.LocoLimit);
        }

        public static void Reset()
        {
            _count = 0;
        }

        public static void Increment()
        {
            _count++;
        }
    }
}

namespace PersistentLocos.Plus
{
	[HarmonyPatch(typeof(LocoDebtController), nameof(LocoDebtController.RegisterLocoDebtTracker))]
	internal static class LocoDebtController_RegisterLocoDebtTracker_BlockWhenPersistentDamage
	{
		[HarmonyPrefix]
		private static bool Prefix(LocoDebtController __instance, TrainCar car, LocoDebtTrackerBase locoDebtTracker)
		{
			if (!PersistentLocos.Main.Settings.enablePersistentDamage)
				return true;
			try
			{
				locoDebtTracker?.TurnOffDebtSources();
			}
			catch { }

			if (PersistentLocos.Main.Settings.enableLogging)
				PersistentLocos.Main.Log("LocoDebtController.RegisterLocoDebtTracker blocked (persistent damage).");

			return false;
		}
	}
	
	[HarmonyPatch(typeof(LocoDebtController), nameof(LocoDebtController.StageLocoDebtOnLocoDestroy))]
	internal static class LocoDebtController_StageOnDestroy_BlockWhenPersistentDamage
	{
		[HarmonyPrefix]
		private static bool Prefix(LocoDebtController __instance, LocoDebtTrackerBase locoDebtTrackerToStage)
		{
			if (!PersistentLocos.Main.Settings.enablePersistentDamage)
				return true;

			try
			{
				int idx = __instance.trackedLocosDebts.FindIndex(d => d.locoDebtTracker == locoDebtTrackerToStage);
				if (idx != -1)
				{
					var existing = __instance.trackedLocosDebts[idx];
					__instance.trackedLocosDebts.RemoveAt(idx);

					SingletonBehaviour<CareerManagerDebtController>.Instance.UnregisterDebt(existing);

					existing.UpdateDebtState();
				}

				locoDebtTrackerToStage?.TurnOffDebtSources();
			}
			catch { }

			if (PersistentLocos.Main.Settings.enableLogging)
				PersistentLocos.Main.Log("LocoDebtController.StageLocoDebtOnLocoDestroy blocked (persistent damage).");

			return false;
		}
	}
	
	[HarmonyPatch(typeof(LocoDebtController), nameof(LocoDebtController.LoadDestroyedLocosDebtsSaveData))]
	internal static class LocoDebtController_LoadDestroyedDebts_ClearWhenPersistentDamage
	{
		[HarmonyPostfix]
		private static void Postfix(LocoDebtController __instance)
		{
			if (!PersistentLocos.Main.Settings.enablePersistentDamage)
				return;

			try
			{
				__instance.ClearLocoDebts();
			}
			catch { }

			if (PersistentLocos.Main.Settings.enableLogging)
				PersistentLocos.Main.Log("Cleared staged loco debts from save (persistent damage).");
		}
	}
	
    [HarmonyPatch]
    internal static class LocoOwnership_IsDebtClearForBuy_Override
    {
        private static MethodBase _target;

        static bool Prepare()
        {
            var debtHandlingType = AccessTools.TypeByName(
                "LocoOwnership.OwnershipHandler.DebtHandling"
            );

            if (debtHandlingType == null)
            {
                PersistentLocos.Main.Log("DebtHandling type not found (LocoOwnership not installed?)");
                return false;
            }

            _target = AccessTools.Method(debtHandlingType, "IsDebtClearForBuy");

            return _target != null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => _target;

        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (PersistentLocos.Main.Settings.enablePersistentDamage)
            {
                __result = true;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    internal static class LocoDebt_GetTotalPrice_Overrides
    {
        private static readonly List<MethodBase> _targets = new();
        private static bool _notifiedOnce;

        static bool Prepare()
        {
            _targets.Clear();
            var baseT = AccessTools.TypeByName("DV.ServicePenalty.DisplayableDebt");
            if (baseT == null) return false;

            var asm = baseT.Assembly;
            foreach (var t in asm.GetTypes())
            {
                if (t == null || t == baseT) continue;
                if (!baseT.IsAssignableFrom(t)) continue;

                var n = (t.FullName ?? t.Name).ToLowerInvariant();
                if (!(n.Contains("loco") || n.Contains("tender"))) continue;

                var m = AccessTools.Method(t, "GetTotalPrice", Type.EmptyTypes);
                if (m == null || m.IsAbstract) continue;
                if (m.DeclaringType == t) _targets.Add(m);
            }

            if (_targets.Count == 0)
            {
                if (PersistentLocos.Main.Settings.enableLogging && !_notifiedOnce)
                {
                    PersistentLocos.Main.Log("LocoDebt_GetTotalPrice_Overrides: no targets (OK for this build).");
                    _notifiedOnce = true;
                }
                return false;
            }

            if (PersistentLocos.Main.Settings.enableLogging)
                PersistentLocos.Main.Log("Neutralizing GetTotalPrice() for " + _targets.Count + " loco-debt override(s).");
            return true;
        }

        [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() => _targets;
        [HarmonyPrefix]
		static bool Prefix(ref float __result)
		{
			if (!PersistentLocos.Main.Settings.enablePersistentDamage)
				return true;
			__result = 0f;
			return false;
		}
    }

    internal static class Ownership
    {
        private static bool _init;
        private static Assembly _loAsm;

        private static MethodInfo _isOwned_TrainCar;
        private static object     _isOwned_TrainCarTarget;
        private static MethodInfo _isOwned_Guid; 
        private static object     _isOwned_GuidTarget;

        private static readonly List<MemberInfo> _ownedGuidCollections = new();
        private static readonly List<MemberInfo> _ownedCarCollections  = new();

        private static Type TrainCarT => AccessTools.TypeByName("TrainCar") ?? AccessTools.TypeByName("DV.TrainCar");
        private static bool _loggedLoMissingOnce;

        private static void Init()
        {
            if (_init) return;

            try
            {
                _loAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => (a.FullName ?? "").StartsWith("LocoOwnership", StringComparison.OrdinalIgnoreCase));

                if (_loAsm != null)
                {
                    var tOwnedLocos =
                        _loAsm.GetType("LocoOwnership.OwnedLocos")
                        ?? _loAsm.GetTypes().FirstOrDefault(t => (t.FullName ?? "").IndexOf("OwnedLocos", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (tOwnedLocos != null)
                    {
                        _isOwned_TrainCar = AccessTools.Method(tOwnedLocos, "IsOwned", new[] { TrainCarT });
                        _isOwned_Guid     = AccessTools.Method(tOwnedLocos, "IsOwned", new[] { typeof(string) });

                        object instance = GetSingletonInstance(tOwnedLocos);
                        if (_isOwned_TrainCar != null && !_isOwned_TrainCar.IsStatic) _isOwned_TrainCarTarget = instance;
                        if (_isOwned_Guid     != null && !_isOwned_Guid.IsStatic)     _isOwned_GuidTarget     = instance;

                        foreach (var m in tOwnedLocos.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance))
                        {
                            var (isGuidCol, isCarCol) = ClassifyOwnedCollectionMember(m);
                            if (isGuidCol && IsStaticMember(m)) _ownedGuidCollections.Add(m);
                            if (isCarCol  && IsStaticMember(m)) _ownedCarCollections.Add(m);
                        }
                    }

                    if (Main.Settings.enableLogging)
                        Main.Log("LocoOwnership ready.");
                }
                else
                {
                    if (Main.Settings.enableLogging && !_loggedLoMissingOnce)
                    {
                        Main.Log("LocoOwnership assembly not found – falling back to internal providers.");
                        _loggedLoMissingOnce = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.enableLogging)
                    Main.Log("Ownership init error (non-fatal): " + ex);
            }

            _init = true;
        }

        private static bool HasTrueBoolMember(object obj, params string[] names)
        {
            if (obj == null) return false;
            var t = obj.GetType();
            foreach (var nm in names)
            {
                var p = AccessTools.Property(t, nm);
                if (p != null && p.CanRead && p.PropertyType == typeof(bool))
                    if ((bool)p.GetValue(obj)) return true;

                var f = AccessTools.Field(t, nm);
                if (f != null && f.FieldType == typeof(bool))
                    if ((bool)f.GetValue(obj)) return true;
            }
            return false;
        }

        private static bool IsRestorationOrPlayerSpawned(object car)
        {
            try
            {
                if (HasTrueBoolMember(car,
                    "playerSpawnedCar","PlayerSpawnedCar",
                    "uniqueCar","UniqueCar",
                    "isUnique","IsUnique"))
                    return true;

                var lrcT = AccessTools.TypeByName("DV.LocoRestoration.LocoRestorationController")
                       ?? AccessTools.TypeByName("LocoRestorationController");
                if (lrcT != null)
                {
                    var m = AccessTools.Method(lrcT, "GetForTrainCar", new[] { TrainCarT });
                    if (m != null)
                    {
                        var res = m.Invoke(null, new[] { car });
                        if (res != null) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool TryIsOwned(object trainCar, out bool owned)
        {
            owned = false;
            if (trainCar == null) return false;
            Init();

            try
            {
                if (IsRestorationOrPlayerSpawned(trainCar))
                {
                    owned = true;
                    if (Main.Settings.enableLogging)
                        Main.Log("Ownership: restoration/playerSpawned/unique -> OWNED");
                    return true;
                }

                if (_isOwned_TrainCar != null)
                {
                    var r = _isOwned_TrainCar.Invoke(_isOwned_TrainCarTarget, new[] { trainCar });
                    if (r is bool b1)
                    {
                        if (!b1 && IsRestorationOrPlayerSpawned(trainCar))
                        {
                            owned = true;
                            if (Main.Settings.enableLogging)
                                Main.Log("Ownership: LocoOwnership=false but restoration/playerSpawned/unique -> OWNED");
                            return true;
                        }
                        owned = b1;
                        return true;
                    }
                }

                var guid = Helpers.GetCarGuid(trainCar);

                if (!string.IsNullOrEmpty(guid) && _isOwned_Guid != null)
                {
                    var r = _isOwned_Guid.Invoke(_isOwned_GuidTarget, new object[] { guid });
                    if (r is bool b2)
                    {
                        if (!b2 && IsRestorationOrPlayerSpawned(trainCar))
                        {
                            owned = true;
                            if (Main.Settings.enableLogging)
                                Main.Log("Ownership: LocoOwnership(false by guid) but restoration/playerSpawned/unique -> OWNED");
                            return true;
                        }
                        owned = b2;
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(guid) && _ownedGuidCollections.Count > 0)
                {
                    foreach (var m in _ownedGuidCollections)
                        if (MemberContainsGuid(m, guid)) { owned = true; return true; }
                }

                if (_ownedCarCollections.Count > 0)
                {
                    foreach (var m in _ownedCarCollections)
                        if (MemberContainsCar(m, trainCar, guid)) { owned = true; return true; }
                }

                if (IsRestorationOrPlayerSpawned(trainCar))
                {
                    owned = true;
                    if (Main.Settings.enableLogging)
                        Main.Log("Ownership: fallback restoration/playerSpawned/unique -> OWNED");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                if (Main.Settings.enableLogging)
                    Main.Log("TryIsOwned error (non-fatal): " + ex);
                return false;
            }
        }

        private static object GetSingletonInstance(Type t)
        {
            var p = t.GetProperty("Instance", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
                 ?? t.GetProperty("Current",  BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if (p != null && p.CanRead) return p.GetValue(null);

            var f = t.GetField("Instance", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
                 ?? t.GetField("instance", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if (f != null) return f.GetValue(null);

            return null;
        }

        private static bool IsStaticMember(MemberInfo m) =>
            (m as FieldInfo)?.IsStatic
            ?? (m as PropertyInfo)?.GetGetMethod(true)?.IsStatic
            ?? false;

        private static (bool isGuidCol, bool isCarCol) ClassifyOwnedCollectionMember(MemberInfo m)
        {
            try
            {
                Type mt = m is FieldInfo f ? f.FieldType :
                          m is PropertyInfo p ? p.PropertyType : null;
                if (mt == null) return (false, false);

                var name = m.Name.ToLowerInvariant();
                bool looksOwned = name.Contains("owned") || name.Contains("saved") || name.Contains("cache");
                bool looksLoco  = name.Contains("loco") || name.Contains("engine") || name.Contains("car");
                if (!(looksOwned && looksLoco)) return (false, false);

                if (ImplementsEnumerableOf<string>(mt)) return (true, false);
                if (ImplementsEnumerableOf(TrainCarT, mt)) return (false, true);
                if (IsDictionaryWithKey<string>(mt)) return (true, false);
            }
            catch { }
            return (false, false);
        }

        private static bool MemberContainsGuid(MemberInfo m, string guid)
        {
            try
            {
                var obj = Helpers.GetStaticMemberValue(m);
                if (obj == null) return false;

                if (obj is IDictionary dict)
                {
                    foreach (var k in dict.Keys)
                        if (string.Equals(k?.ToString(), guid, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (obj is IEnumerable en)
                {
                    foreach (var v in en)
                        if (string.Equals(v?.ToString(), guid, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool MemberContainsCar(MemberInfo m, object car, string guid)
        {
            try
            {
                var obj = Helpers.GetStaticMemberValue(m);
                if (obj == null) return false;

                if (obj is IEnumerable en)
                {
                    foreach (var v in en)
                    {
                        if (ReferenceEquals(v, car)) return true;
                        if (v != null && !string.IsNullOrEmpty(guid))
                        {
                            var g2 = Helpers.GetCarGuid(v);
                            if (!string.IsNullOrEmpty(g2) && string.Equals(g2, guid, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool ImplementsEnumerableOf<T>(Type t)
        {
            if (t == null) return false;
            return typeof(IEnumerable<T>).IsAssignableFrom(t)
                || t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && i.GetGenericArguments()[0] == typeof(T));
        }
        private static bool ImplementsEnumerableOf(Type elem, Type t)
        {
            if (t == null || elem == null) return false;
            return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && i.GetGenericArguments()[0] == elem);
        }
        private static bool IsDictionaryWithKey<TKey>(Type t)
        {
            if (t == null) return false;
            return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>) && i.GetGenericArguments()[0] == typeof(TKey));
        }
    }

    internal static class UiPriceState
    {
        private static readonly Dictionary<object, float> _originalPerUnit = new Dictionary<object, float>(ReferenceEqualityComparer.Instance);

        public static bool IsBoosted(object data) => data != null && _originalPerUnit.ContainsKey(data);
        public static void MarkBoosted(object data, float original)
        {
            if (data == null) return;
            _originalPerUnit[data] = original;
        }
        public static bool TryGetOriginal(object data, out float original) =>
            _originalPerUnit.TryGetValue(data, out original);
        public static void Clear(object data)
        {
            if (data == null) return;
            _originalPerUnit.Remove(data);
        }
    }

    [HarmonyPatch]
    internal static class PitStop_UI_Prices_After_UpdateIndependent
    {
        private static MethodBase _target;

        static bool Prepare()
        {
            var t = AccessTools.TypeByName("PitStopIndicators");
            if (t == null) { return false; }

            var trainCarT = AccessTools.TypeByName("TrainCar") ?? AccessTools.TypeByName("DV.TrainCar");
            _target = AccessTools.Method(t, "UpdateIndependentPrices", new[] { trainCarT })
                   ?? t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       .FirstOrDefault(m => m.Name == "UpdateIndependentPrices");
            return _target != null;
        }

        [HarmonyTargetMethod] static MethodBase TargetMethod() => _target;

        [HarmonyPostfix]
        static void Postfix(object __instance, object __0 /* selectedCar */)
        {
            Helpers.ApplyUiPriceBoostForIndicators(__instance, __0);
        }
    }

    [HarmonyPatch]
    internal static class PitStop_UI_Prices_After_UpdateDependingOnType
    {
        private static readonly List<MethodBase> _targets = new();

        static bool Prepare()
        {
            var t = AccessTools.TypeByName("PitStopIndicators");
            if (t == null) return false;

            _targets.Clear();
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "UpdatePricesDependingOnLocoType") continue;
                if (m.GetParameters().Length >= 1) _targets.Add(m);
            }

            if (_targets.Count == 0) return false;
            return true;
        }

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => _targets;

        [HarmonyPostfix]
        static void Postfix(object __instance, object __0 /* selectedCar */)
        {
            Helpers.ApplyUiPriceBoostForIndicators(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(PitStopStation), "DisplayLatestCarParamsReport")]
	internal static class PitStop_UI_Prices_After_DisplayLatestReport
	{
		[HarmonyPostfix]
		static void Postfix(object __instance)
		{
			try
			{
				var pitstop = AccessTools.Field(__instance.GetType(), "pitstop")?.GetValue(__instance);
				var indicators = AccessTools.Field(__instance.GetType(), "locoResourceModules")?.GetValue(__instance);
				var selectedCar = pitstop != null ? AccessTools.Property(pitstop.GetType(), "SelectedCar")?.GetValue(pitstop) : null;
				if (indicators != null && selectedCar != null)
					Helpers.ApplyUiPriceBoostForIndicators(indicators, selectedCar);
			}
			catch { }
		}
	}
	
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    internal static class Helpers
    {
        public static bool IsTrainCarType(Type t) =>
            t != null && (t.FullName == "TrainCar" || t.FullName == "DV.TrainCar");

        public static bool IsLocomotive(object trainCar)
        {
            try
            {
                var t = trainCar.GetType();
                var prop = AccessTools.Property(t, "carType") ?? AccessTools.Property(t, "CarType");
                var fld  = AccessTools.Field(t, "carType")    ?? AccessTools.Field(t, "CarType");

                object val = null;
                if (prop != null) val = prop.GetValue(trainCar);
                else if (fld != null) val = fld.GetValue(trainCar);
                if (val == null) return false;

                var name = val.ToString().ToLowerInvariant();
                if (name.Contains("loco")) return true;
                if (name.Contains("de2") || name.Contains("de6") || name.Contains("dm3") || name.Contains("s060") || name.Contains("s282"))
                    return true;
            }
            catch { }
            return false;
        }

        public static bool HasManualServiceLicense()
		{
			return SingletonBehaviour<LicenseManager>.Instance.IsGeneralLicenseAcquired(GeneralLicenseType.ManualService.ToV2());
		}

        public static double GetEffectiveServiceMultiplier(object trainCar)
		{
			PersistentLocos.Main.Log(
				$"[DBG] enableUnowned={Main.Settings.enableUnownedServiceMultiplier}, " +
				$"assumeNonOwnedWhenUnknown={Main.Settings.assumeNonOwnedWhenUnknown}, " +
				$"unownedMult={Main.Settings.unownedServiceMultiplier}"
			);

			double mult = 1d;

			bool hasManualService = SingletonBehaviour<LicenseManager>.Instance.IsGeneralLicenseAcquired(GeneralLicenseType.ManualService.ToV2());
						
			if (Main.Settings.enableRepairWithoutLicense && !hasManualService)
			{
				mult *= Math.Max(1d, (double)Main.Settings.repairWithoutLicenseMultiplier);
			}

			try
			{
				bool owned;
				bool known = PersistentLocos.Plus.Ownership.TryIsOwned(trainCar, out owned);
				bool applyUnowned =
					PersistentLocos.Main.Settings.enableUnownedServiceMultiplier &&	((known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown));

				if (applyUnowned)
					mult *= Math.Max(1d, (double)PersistentLocos.Main.Settings.unownedServiceMultiplier);
			}
			catch { }

			PersistentLocos.Main.Log($" FINAL mult = {mult}");
			return mult;
		}

        public static object ResolveCarFromCashRegister(object cashInstance)
        {
            if (cashInstance == null) return null;

            try
            {
                var data = GetCashRegisterData(cashInstance);
                if (data != null)
                {
                    var carFld = AccessTools.Field(data.GetType(), "car");
                    var carVal = carFld?.GetValue(data);
                    if (carVal != null) return carVal;
                }
            }
            catch { }

            foreach (var nm in new[] { "trainCar","car","loco","targetCar","selectedCar","currentCar" })
            {
                try
                {
                    var f = AccessTools.Field(cashInstance.GetType(), nm);
                    if (f != null)
                    {
                        var v = f.GetValue(cashInstance);
                        if (v != null) return v;
                    }
                    var p = AccessTools.Property(cashInstance.GetType(), nm);
                    if (p != null && p.CanRead)
                    {
                        var v = p.GetValue(cashInstance);
                        if (v != null) return v;
                    }
                }
                catch { }
            }

            return null;
        }

        public static object GetCashRegisterData(object cashInstance)
        {
            if (cashInstance == null) return null;
            var t = cashInstance.GetType();
            var dataProp = AccessTools.Property(t, "Data") ?? AccessTools.Property(AccessTools.TypeByName("CashRegisterModule"), "Data");
            return dataProp?.GetValue(cashInstance);
        }

        public static string GetCarGuid(object trainCar)
        {
            if (trainCar == null) return null;

            foreach (var nm in new[] { "CarGUID","carGuid","GUID","Guid","guid","Id","ID" })
            {
                var p = AccessTools.Property(trainCar.GetType(), nm);
                if (p != null && p.CanRead && p.PropertyType == typeof(string))
                {
                    var s = p.GetValue(trainCar) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                var f = AccessTools.Field(trainCar.GetType(), nm);
                if (f != null && f.FieldType == typeof(string))
                {
                    var s = f.GetValue(trainCar) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }

            foreach (var holder in new[] { "logicCar","LogicCar","car","Car","data","Data" })
            {
                var o = AccessTools.Property(trainCar.GetType(), holder)?.GetValue(trainCar)
                     ?? AccessTools.Field(trainCar.GetType(), holder)?.GetValue(trainCar);
                if (o == null) continue;

                foreach (var nm in new[] { "ID","Id","Guid","GUID","guid" })
                {
                    var p = AccessTools.Property(o.GetType(), nm);
                    if (p != null && p.CanRead && p.PropertyType == typeof(string))
                    {
                        var s = p.GetValue(o) as string;
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                    var f = AccessTools.Field(o.GetType(), nm);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        var s = f.GetValue(o) as string;
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }

            return null;
        }

        public static object GetStaticMemberValue(MemberInfo m)
        {
            if (m is FieldInfo f) return f.IsStatic ? f.GetValue(null) : null;
            if (m is PropertyInfo p) return (p.GetGetMethod(true)?.IsStatic ?? false) ? p.GetValue(null) : null;
            return null;
        }

        public static IFormatProvider GetLocCC()
        {
            try
            {
                var t = AccessTools.TypeByName("DV.Localization.LocalizationAPI");
                var p = AccessTools.Property(t, "CC");
                var cc = p?.GetValue(null) as IFormatProvider;
                return cc ?? CultureInfo.InvariantCulture;
            }
            catch { return CultureInfo.InvariantCulture; }
        }
		
		[ThreadStatic]
        private static bool _inUiRewrite;
		
        private sealed class BoostEntry
        {
            public string guid;
            public float  mult;
            public float  lastApplyTime;
        }

        private static readonly Dictionary<object, BoostEntry> _indicatorsState =
            new Dictionary<object, BoostEntry>(ReferenceEqualityComparer.Instance);

        private static string TryGetCarGuidForCache(object trainCar)
        {
            try { return GetCarGuid(trainCar) ?? "<no-guid>"; }
            catch { return "<err>"; }
        }

        private static bool ShouldApplyForIndicators(object indicatorsInstance, string guid, float mult)
        {
            if (!_indicatorsState.TryGetValue(indicatorsInstance, out var e))
            {
                _indicatorsState[indicatorsInstance] = new BoostEntry { guid = guid, mult = mult, lastApplyTime = Time.time };
                return true;
            }

            if (e.guid == guid && Mathf.Abs(e.mult - mult) < 0.001f)
            {
                if (Time.time - e.lastApplyTime < 0.5f) return false;
            }

            e.guid = guid;
            e.mult = mult;
            e.lastApplyTime = Time.time;
            return true;
        }

        public static void ApplyUiPriceBoostForIndicators(object indicatorsInstance, object trainCar)
        {
            try
            {
                if (indicatorsInstance == null || trainCar == null) return;
                if (!IsLocomotive(trainCar)) return;

                if (_inUiRewrite) return;
                _inUiRewrite = true;

                float mult = (float)GetEffectiveServiceMultiplier(trainCar);
                string g = TryGetCarGuidForCache(trainCar);

                if (!ShouldApplyForIndicators(indicatorsInstance, g, mult))
                {
                    _inUiRewrite = false;
                    return;
                }

                var modulesFld = AccessTools.Field(indicatorsInstance.GetType(), "resourceModules");
                var modulesArr = modulesFld?.GetValue(indicatorsInstance) as Array;
                if (modulesArr == null) { _inUiRewrite = false; return; }

                for (int i = 0; i < modulesArr.Length; i++)
                {
                    var mod = modulesArr.GetValue(i);
                    if (mod == null) continue;

                    var data = GetCashRegisterData(mod);
                    if (data == null) continue;

                    var ppuField = AccessTools.Field(data.GetType(), "pricePerUnit");
                    if (ppuField == null || ppuField.FieldType != typeof(float)) continue;

                    float current = (float)ppuField.GetValue(data);

                    if (mult > 1f)
                    {
                        if (!UiPriceState.TryGetOriginal(data, out var original))
                        {
                            UiPriceState.MarkBoosted(data, current);
                            original = current;
                        }
                        ppuField.SetValue(data, original * mult);
                    }
                    else
                    {
                        if (UiPriceState.TryGetOriginal(data, out var original))
                        {
                            ppuField.SetValue(data, original);
                            UiPriceState.Clear(data);
                        }
                    }

                    WriteModuleTextsFromData(mod, data);
                }

                if (PersistentLocos.Main.Settings.enableLogging)
                    PersistentLocos.Main.Log($"PitStop UI price {(mult > 1f ? "boost" : "normalize")} applied (guarded).");
            }
            catch (Exception ex)
            {
                if (PersistentLocos.Main.Settings.enableLogging)
                    PersistentLocos.Main.Log("ApplyUiPriceBoostForIndicators error (non-fatal): " + ex.Message);
            }
            finally
            {
                _inUiRewrite = false;
            }
        }

        public static void WriteModuleTextsFromData(object module, object data)
        {
            try
            {
                var ppuField   = AccessTools.Field(data.GetType(), "pricePerUnit");
                var unitsField = AccessTools.Field(data.GetType(), "unitsToBuy");
                if (ppuField == null) return;

                float ppuNow = (float)ppuField.GetValue(data);
                float units  = unitsField != null ? (float)unitsField.GetValue(data) : 0f;
                double total = ppuNow * units;

                var priceTxtObj = AccessTools.Field(module.GetType(), "pricePerUnitText")?.GetValue(module);
                var totalTxtObj = AccessTools.Field(module.GetType(), "totalPriceText")?.GetValue(module);
                var textPropPpu = priceTxtObj != null ? AccessTools.Property(priceTxtObj.GetType(), "text") : null;
                var textPropTot = totalTxtObj != null ? AccessTools.Property(totalTxtObj.GetType(), "text") : null;
                var cc = GetLocCC();

                if (textPropPpu != null && priceTxtObj != null)
                    textPropPpu.SetValue(priceTxtObj, "$" + ppuNow.ToString("N2", cc));

                if (textPropTot != null && totalTxtObj != null)
                    textPropTot.SetValue(totalTxtObj, "$" + ((total >= 0) ? total : 0d).ToString("N2", cc));
            }
            catch { }
        }

        public static void RefreshPitStopUiForCar(object trainCar)
        {
            try
            {
                var pstType = AccessTools.TypeByName("PitStopStation");
                if (pstType == null || trainCar == null) return;

                var stations = UnityEngine.Object.FindObjectsOfType(pstType);
                foreach (var st in stations)
                {
                    var pitstop = AccessTools.Field(pstType, "pitstop")?.GetValue(st);
                    if (pitstop == null) continue;

                    var isInPitM = AccessTools.Method(pitstop.GetType(), "IsCarInPitStop", Type.EmptyTypes);
                    var isInPit = isInPitM != null && (bool)isInPitM.Invoke(pitstop, null);

                    var selectedCar = AccessTools.Property(pitstop.GetType(), "SelectedCar")?.GetValue(pitstop);
                    if (!isInPit || selectedCar == null || !ReferenceEquals(selectedCar, trainCar)) continue;

                    var indicators = AccessTools.Field(pstType, "locoResourceModules")?.GetValue(st);
                    var selectedLivery = AccessTools.Property(selectedCar.GetType(), "carLivery")?.GetValue(selectedCar);
                    var indT = indicators?.GetType();

                    var updType = AccessTools.Method(indT, "UpdatePricesDependingOnLocoType", new[] { selectedCar.GetType(), selectedLivery?.GetType() });
                    if (updType == null)
                        updType = indT.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                                      .FirstOrDefault(m => m.Name == "UpdatePricesDependingOnLocoType" && m.GetParameters().Length >= 1);

                    var updIndep = AccessTools.Method(indT, "UpdateIndependentPrices", new[] { selectedCar.GetType() })
                                  ?? indT.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                                         .FirstOrDefault(m => m.Name == "UpdateIndependentPrices");

                    updType?.Invoke(indicators, new object[] { selectedCar, selectedLivery });
                    updIndep?.Invoke(indicators, new object[] { selectedCar });

                    ApplyUiPriceBoostForIndicators(indicators, selectedCar);

                    var dispLatest = AccessTools.Method(pstType, "DisplayLatestCarParamsReport", new[] { typeof(bool) });
                    dispLatest?.Invoke(st, new object[] { false });

                    if (PersistentLocos.Main.Settings.enableLogging)
                        PersistentLocos.Main.Log("PitStop UI refresh after ownership change (target car).");
                }
            }
            catch (Exception ex)
            {
                if (PersistentLocos.Main.Settings.enableLogging)
                    PersistentLocos.Main.Log("RefreshPitStopUiForCar error (non-fatal): " + ex);
            }
        }

        public static void RefreshPitStopsForAllSelected()
        {
            try
            {
                var pstType = AccessTools.TypeByName("PitStopStation");
                if (pstType == null) return;

                var stations = UnityEngine.Object.FindObjectsOfType(pstType);
                foreach (var st in stations)
                {
                    var pitstop = AccessTools.Field(pstType, "pitstop")?.GetValue(st);
                    var indicators = AccessTools.Field(pstType, "locoResourceModules")?.GetValue(st);
                    if (pitstop == null || indicators == null) continue;

                    var isInPitM = AccessTools.Method(pitstop.GetType(), "IsCarInPitStop", Type.EmptyTypes);
                    var isInPit = isInPitM != null && (bool)isInPitM.Invoke(pitstop, null);
                    if (!isInPit) continue;

                    var selectedCar = AccessTools.Property(pitstop.GetType(), "SelectedCar")?.GetValue(pitstop);
                    if (selectedCar == null) continue;

                    var selectedLivery = AccessTools.Property(selectedCar.GetType(), "carLivery")?.GetValue(selectedCar);
                    var indT = indicators.GetType();

                    var updType = AccessTools.Method(indT, "UpdatePricesDependingOnLocoType", new[] { selectedCar.GetType(), selectedLivery?.GetType() });
                    if (updType == null)
                        updType = indT.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                                      .FirstOrDefault(m => m.Name == "UpdatePricesDependingOnLocoType" && m.GetParameters().Length >= 1);

                    var updIndep = AccessTools.Method(indT, "UpdateIndependentPrices", new[] { selectedCar.GetType() })
                                  ?? indT.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                                         .FirstOrDefault(m => m.Name == "UpdateIndependentPrices");

                    updType?.Invoke(indicators, new object[] { selectedCar, selectedLivery });
                    updIndep?.Invoke(indicators, new object[] { selectedCar });

                    ApplyUiPriceBoostForIndicators(indicators, selectedCar);

                    var dispLatest = AccessTools.Method(pstType, "DisplayLatestCarParamsReport", new[] { typeof(bool) });
                    dispLatest?.Invoke(st, new object[] { false });
                }

                if (PersistentLocos.Main.Settings.enableLogging)
                    PersistentLocos.Main.Log("PitStop UI refresh after ownership change (all selected).");
            }
            catch (Exception ex)
            {
                if (PersistentLocos.Main.Settings.enableLogging)
                    PersistentLocos.Main.Log("RefreshPitStopsForAllSelected error (non-fatal): " + ex);
            }
        }
    }
	
	[HarmonyPatch(typeof(CashRegisterModule), nameof(CashRegisterModule.GetTotalPrice))]
	internal static class CashRegister_GetTotalPrice_WithMultiplier
	{
		[HarmonyPostfix]
		static void Postfix(CashRegisterModule __instance, ref double __result)
		{
			var car = Helpers.ResolveCarFromCashRegister(__instance) as TrainCar;
			if (car == null || !Helpers.IsLocomotive(car)) return;

			double totalMult = 1.0;

			if (Main.Settings.enableUnownedServiceMultiplier)
			{
				bool isOwned = true;
				PersistentLocos.Plus.Ownership.TryIsOwned(car, out isOwned);

				if (!isOwned)
				{
					totalMult *= Main.Settings.unownedServiceMultiplier;
				}
			}

			if (Main.Settings.enableRepairWithoutLicense)
			{
				bool hasLicense = SingletonBehaviour<LicenseManager>.Instance.IsGeneralLicenseAcquired(DV.ThingTypes.GeneralLicenseType.ManualService.ToV2());
				
				if (!hasLicense)
				{
					totalMult *= Main.Settings.repairWithoutLicenseMultiplier;
				}
			}

			if (totalMult > 1.01)
			{
				__result *= totalMult;
				
				if (Main.Settings.enableLogging)
					Main.Log($"[PriceCheck] {car.ID}: Mult {totalMult:0.##}x -> Endpreis: {__result:0.00}");
			}
		}
	}
	
	[HarmonyPatch(typeof(PitStop), "Awake")]
	public static class PitStop_Awake_Patch
	{
		static void Postfix(PitStop __instance)
		{
			if (Main.Settings.enableRepairWithoutLicense)
			{
				AccessTools.Field(typeof(PitStop), "isManualServiceLicenseAcquired").SetValue(__instance, true);

				try {
					var textObj = AccessTools.Field(typeof(PitStop), "manualServiceText").GetValue(__instance);
					var title = (string)AccessTools.Property(typeof(PitStop), "MANUAL_SERVICE_TITLE").GetValue(__instance);
					if (textObj != null) 
						AccessTools.Property(textObj.GetType(), "text").SetValue(textObj, title);
				} catch {}
			}
		}
	}

	[HarmonyPatch(typeof(PitStop), "OnTriggerEnter")]
	public static class PitStop_OnTriggerEnter_Patch
	{
		static void Prefix(PitStop __instance)
		{
			if (Main.Settings.enableRepairWithoutLicense)
				AccessTools.Field(typeof(PitStop), "isManualServiceLicenseAcquired").SetValue(__instance, true);
		}
	}
}
