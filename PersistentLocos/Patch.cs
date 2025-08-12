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
                //Debug.Log($"[PersistentLocos] Saved locomotive count to save: {LocoSpawnState.Count}");
                //Debug.Log($"[PersistentLocos] Saved LocoLimit to save: {Main.Settings.LocoLimit}");
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


namespace PersistentLocos {

// ---- Embedded from original CoroutineDispatcher.cs ----
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

// ---- Embedded from original LocoSpawnState.cs ----
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

            //Debug.Log($"[PersistentLocos] Saved locomotive count: {_count}");
            //Debug.Log($"[PersistentLocos] Saved LocoLimit: {Main.Settings.LocoLimit}");
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

// PersistentLocosPlus/Patch.cs  (v1.10.2)

namespace PersistentLocos.Plus
{
    // ============================================================
    // A) DV: Lokomotiv-Gebühren neutralisieren (nur Loks)
    // ============================================================
    [HarmonyPatch]
    internal static class LocoDebt_GetTotalPrice_Overrides
    {
        private static readonly List<MethodBase> _targets = new();

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
                PersistentLocos.Main.Warn("LocoDebt_GetTotalPrice_Overrides: no targets.");
                return false;
            }

            PersistentLocos.Main.Log("Neutralizing GetTotalPrice() for " + _targets.Count + " loco-debt override(s).");
            return true;
        }

        [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() => _targets;

        [HarmonyPrefix]
        static bool Prefix(ref float __result)
        {
            if (!PersistentLocos.Main.Settings.blockLocomotiveFees) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class LocoDebt_IsPayable_Overrides
    {
        private static readonly List<MethodBase> _targets = new();

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

                var getter = AccessTools.PropertyGetter(t, "IsPayable");
                if (getter == null || getter.IsAbstract) continue;
                if (getter.DeclaringType == t) _targets.Add(getter);
            }

            if (_targets.Count == 0)
            {
                PersistentLocos.Main.Warn("LocoDebt_IsPayable_Overrides: no targets.");
                return false;
            }

            PersistentLocos.Main.Log("Neutralizing IsPayable for " + _targets.Count + " loco-debt override(s).");
            return true;
        }

        [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() => _targets;

        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (!PersistentLocos.Main.Settings.blockLocomotiveFees) return true;
            __result = false;
            return false;
        }
    }

    // ============================================================
    // B) Ownership-Resolver (Restoration/playerSpawned/unique => owned)
    // ============================================================
    internal static class Ownership
    {
        private static bool _init;
        private static Assembly _loAsm;

        private static MethodInfo _isOwned_TrainCar; // bool IsOwned(TrainCar)
        private static object     _isOwned_TrainCarTarget; // Instanz falls nicht statisch
        private static MethodInfo _isOwned_Guid;     // bool IsOwned(string)
        private static object     _isOwned_GuidTarget;

        private static readonly List<MemberInfo> _ownedGuidCollections = new();
        private static readonly List<MemberInfo> _ownedCarCollections  = new();

        private static Type TrainCarT => AccessTools.TypeByName("TrainCar") ?? AccessTools.TypeByName("DV.TrainCar");

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

                    Main.Log("[PLP] LO ownership resolver ready: " +
                             $"{(_isOwned_TrainCar!=null?"IsOwned(TrainCar) ":"")}{(_isOwned_Guid!=null?"IsOwned(Guid) ":"")}" +
                             $" GCols={_ownedGuidCollections.Count} CCols={_ownedCarCollections.Count}");
                }
                else
                {
                    Main.Warn("[PLP] LocoOwnership assembly not found – relying on other providers (PersistentLocos?)");
                }

                // Fallback: PersistentLocos-API?
                var asmTC = TrainCarT?.Assembly;
                if (asmTC != null && _isOwned_TrainCar == null)
                {
                    foreach (var t in asmTC.GetTypes())
                    {
                        var full = t.FullName ?? "";
                        if (full.IndexOf("PersistentLocos", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        foreach (var name in new[] { "IsOwned", "IsRegistered", "IsPlayerOwned", "IsMine", "IsTracked" })
                        {
                            var m = AccessTools.Method(t, name, new[] { TrainCarT });
                            if (m != null && m.IsStatic && m.ReturnType == typeof(bool))
                            {
                                _isOwned_TrainCar = m;
                                _isOwned_TrainCarTarget = null;
                                Main.Log($"[PLP] Ownership resolver: {t.Name}.{name}(TrainCar)");
                                break;
                            }
                        }
                        if (_isOwned_TrainCar != null) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Warn("Ownership init error: " + ex);
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
                {
                    if ((bool)p.GetValue(obj)) return true;
                }
                var f = AccessTools.Field(t, nm);
                if (f != null && f.FieldType == typeof(bool))
                {
                    if ((bool)f.GetValue(obj)) return true;
                }
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
                // Restoration / playerSpawned / unique -> owned
                if (IsRestorationOrPlayerSpawned(trainCar))
                {
                    owned = true;
                    Main.Log("[PLP] Ownership: restoration/playerSpawned/unique -> treat as OWNED");
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
                            Main.Log("[PLP] Ownership: LO=false but restoration/playerSpawned/unique -> OWNED");
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
                            Main.Log("[PLP] Ownership: LO(false by guid) but restoration/playerSpawned/unique -> OWNED");
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
                    Main.Log("[PLP] Ownership: fallback restoration/playerSpawned/unique -> OWNED");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Main.Warn("TryIsOwned error: " + ex);
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

    // ============================================================
    // C) Manueller Service: Multiplikator (UI + Kasse) – konsistent
    // ============================================================

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

    [HarmonyPatch(typeof(CashRegisterModule), nameof(CashRegisterModule.GetTotalPrice))]
    internal static class CashRegister_Base_GetTotalPrice_Patch
    {
        [HarmonyPostfix]
        static void Postfix(CashRegisterModule __instance, ref double __result)
        {
            try
            {
                if (__result <= 0d) return;

                var car = Helpers.ResolveCarFromCashRegister(__instance);
                if (car == null || !Helpers.IsLocomotive(car)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(car, out owned);
                bool shouldMult = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                var data = Helpers.GetCashRegisterData(__instance);
                if (UiPriceState.IsBoosted(data)) return;

                if (!shouldMult) return;

                double mult = Math.Max(1d, (double)PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);
                __result *= mult;
                PersistentLocos.Main.Log($"CashReg++ [Base.GetTotalPrice] x{mult} -> {__result}");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("CashRegister Base GetTotalPrice patch error: " + ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class CashRegister_AllOverrides_GetTotalPrice_Patch
    {
        private static readonly List<MethodBase> _targets = new();

        static bool Prepare()
        {
            _targets.Clear();

            var baseCashT = AccessTools.TypeByName("CashRegisterModule");
            if (baseCashT == null) return false;
            var asm = baseCashT.Assembly;

            foreach (var t in asm.GetTypes())
            {
                if (t == null) continue;
                if (!baseCashT.IsAssignableFrom(t)) continue;

                var m = AccessTools.Method(t, "GetTotalPrice", Type.EmptyTypes);
                if (m == null || m.IsAbstract) continue;
                if (m is MethodInfo mi && mi.ReturnType != typeof(double)) continue;
                if (m.DeclaringType == t) _targets.Add(m);
            }

            if (_targets.Count == 0) return false;
            PersistentLocos.Main.Log($"CashRegister.GetTotalPrice override targets: {_targets.Count}");
            return true;
        }

        [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() => _targets;

        [HarmonyPostfix]
        static void Postfix(object __instance, ref double __result, MethodBase __originalMethod)
        {
            try
            {
                if (__result <= 0d) return;

                var car = Helpers.ResolveCarFromCashRegister(__instance);
                if (car == null || !Helpers.IsLocomotive(car)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(car, out owned);
                bool shouldMult = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                var data = Helpers.GetCashRegisterData(__instance);
                if (UiPriceState.IsBoosted(data)) return;

                if (!shouldMult) return;

                double mult = Math.Max(1d, (double)PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);
                __result *= mult;
                PersistentLocos.Main.Log($"CashReg++ [{__originalMethod?.DeclaringType?.Name}.GetTotalPrice] x{mult} -> {__result}");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("CashRegister Override GetTotalPrice patch error: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(CashRegisterModule), nameof(CashRegisterModule.GetAllNonZeroPurchaseData))]
    internal static class CashRegister_Base_GetAllNonZeroPurchaseData_Patch
    {
        [HarmonyPostfix]
        static void Postfix(CashRegisterModule __instance, ref IReadOnlyList<CashRegisterModule.CashRegisterModuleData> __result)
        {
            try
            {
                if (__result == null || __result.Count == 0) return;

                var car = Helpers.ResolveCarFromCashRegister(__instance);
                if (car == null || !Helpers.IsLocomotive(car)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(car, out owned);
                bool shouldMult = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                var data = Helpers.GetCashRegisterData(__instance);
                if (UiPriceState.IsBoosted(data)) return;

                if (!shouldMult) return;

                float mult = Math.Max(1f, PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);

                var newList = new List<CashRegisterModule.CashRegisterModuleData>(__result.Count);
                foreach (var d in __result)
                {
                    if (d == null) continue;
                    var copy = new CashRegisterModule.CashRegisterModuleData(d, copyUnitsToBuy: true, copyPrice: true);
                    copy.pricePerUnit *= mult;
                    newList.Add(copy);
                }
                __result = newList;
                PersistentLocos.Main.Log($"CashRegData++ [Base.GetAllNonZeroPurchaseData] x{mult} (prices adjusted)");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("CashRegister Base GetAllNonZeroPurchaseData patch error: " + ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class CashRegister_AllOverrides_GetAllNonZeroPurchaseData_Patch
    {
        private static readonly List<MethodBase> _targets = new();

        static bool Prepare()
        {
            _targets.Clear();

            var baseCashT = AccessTools.TypeByName("CashRegisterModule");
            if (baseCashT == null) return false;
            var asm = baseCashT.Assembly;

            foreach (var t in asm.GetTypes())
            {
                if (t == null) continue;
                if (!baseCashT.IsAssignableFrom(t)) continue;

                var m = AccessTools.Method(t, "GetAllNonZeroPurchaseData", Type.EmptyTypes);
                if (m == null || m.IsAbstract) continue;
                if (m.DeclaringType == t) _targets.Add(m);
            }

            if (_targets.Count == 0) return false;
            PersistentLocos.Main.Log($"CashRegister.GetAllNonZeroPurchaseData override targets: {_targets.Count}");
            return true;
        }

        [HarmonyTargetMethods] static IEnumerable<MethodBase> TargetMethods() => _targets;

        [HarmonyPostfix]
        static void Postfix(object __instance, ref IReadOnlyList<CashRegisterModule.CashRegisterModuleData> __result, MethodBase __originalMethod)
        {
            try
            {
                if (__result == null || __result.Count == 0) return;

                var car = Helpers.ResolveCarFromCashRegister(__instance);
                if (car == null || !Helpers.IsLocomotive(car)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(car, out owned);
                bool shouldMult = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                var data = Helpers.GetCashRegisterData(__instance);
                if (UiPriceState.IsBoosted(data)) return;

                if (!shouldMult) return;

                float mult = Math.Max(1f, PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);

                var newList = new List<CashRegisterModule.CashRegisterModuleData>(__result.Count);
                foreach (var d in __result)
                {
                    if (d == null) continue;
                    var copy = new CashRegisterModule.CashRegisterModuleData(d, copyUnitsToBuy: true, copyPrice: true);
                    copy.pricePerUnit *= mult;
                    newList.Add(copy);
                }
                __result = newList;
                PersistentLocos.Main.Log($"CashRegData++ [{__originalMethod?.DeclaringType?.Name}.GetAllNonZeroPurchaseData] x{mult} (prices adjusted)");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("CashRegister Override GetAllNonZeroPurchaseData patch error: " + ex.Message);
            }
        }
    }

    // ============================================================
    // D) PitStop-UI: Preisschilder (pro Modul) anheben / zurücksetzen
    // ============================================================
    [HarmonyPatch]
    internal static class PitStop_UI_Prices_After_UpdateIndependent
    {
        private static MethodBase _target;

        static bool Prepare()
        {
            var t = AccessTools.TypeByName("PitStopIndicators");
            if (t == null) { PersistentLocos.Main.Warn("PitStopIndicators not found."); return false; }

            var trainCarT = AccessTools.TypeByName("TrainCar") ?? AccessTools.TypeByName("DV.TrainCar");
            _target = AccessTools.Method(t, "UpdateIndependentPrices", new[] { trainCarT })
                   ?? t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       .FirstOrDefault(m => m.Name == "UpdateIndependentPrices");
            if (_target == null) { PersistentLocos.Main.Warn("UpdateIndependentPrices not found."); return false; }
            return true;
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
            if (t == null)
            {
                PersistentLocos.Main.Warn("PitStopIndicators not found.");
                return false;
            }

            _targets.Clear();
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "UpdatePricesDependingOnLocoType") continue;
                if (m.GetParameters().Length >= 1) _targets.Add(m);
            }

            if (_targets.Count == 0)
            {
                PersistentLocos.Main.Warn("No UpdatePricesDependingOnLocoType overload found — skipping.");
                return false;
            }
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

    [HarmonyPatch(typeof(LocoResourceModule), "UpdateResourcePricePerUnit")]
    internal static class LocoResourceModule_UpdateResourcePricePerUnit_Patch
    {
        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var car = Helpers.ResolveCarFromCashRegister(__instance);
                if (car == null || !Helpers.IsLocomotive(car)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(car, out owned);
                bool shouldBoost = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                var data = Helpers.GetCashRegisterData(__instance);
                if (data == null) return;

                var ppuField   = AccessTools.Field(data.GetType(), "pricePerUnit");
                if (ppuField == null) return;

                float currentPpu = (float)ppuField.GetValue(data);
                float mult = Math.Max(1f, PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);

                if (shouldBoost)
                {
                    if (!UiPriceState.TryGetOriginal(data, out var original))
                    {
                        UiPriceState.MarkBoosted(data, currentPpu);
                        original = currentPpu;
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

                Helpers.WriteModuleTextsFromData(__instance, data);
                PersistentLocos.Main.Log("Per-unit text refresh via UpdateResourcePricePerUnit.");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("LocoResourceModule.UpdateResourcePricePerUnit patch error: " + ex.Message);
            }
        }
    }

    // ============================================================
    // E) Helpers
    // ============================================================
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

        // Car aus (CashRegisterModule oder LocoResourceModule) ermitteln
        public static object ResolveCarFromCashRegister(object cashInstance)
        {
            if (cashInstance == null) return null;

            // 1) Über Data.car
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

            // 2) Fallbacks
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

        // zentraler UI-Boost / -Revert inkl. Text-Neuschreibung
        public static void ApplyUiPriceBoostForIndicators(object indicatorsInstance, object trainCar)
        {
            try
            {
                if (indicatorsInstance == null || trainCar == null) return;
                if (!IsLocomotive(trainCar)) return;

                bool owned;
                bool known = Ownership.TryIsOwned(trainCar, out owned);
                bool shouldBoost = (known && !owned) || (!known && PersistentLocos.Main.Settings.assumeNonOwnedWhenUnknown);

                float mult = Math.Max(1f, PersistentLocos.Main.Settings.serviceCostMultiplierForNonOwned);

                var modulesFld = AccessTools.Field(indicatorsInstance.GetType(), "resourceModules");
                var modulesArr = modulesFld?.GetValue(indicatorsInstance) as Array;
                if (modulesArr == null) return;

                for (int i = 0; i < modulesArr.Length; i++)
                {
                    var mod = modulesArr.GetValue(i);
                    if (mod == null) continue;

                    var data = GetCashRegisterData(mod);
                    if (data == null) continue;

                    var ppuField = AccessTools.Field(data.GetType(), "pricePerUnit");
                    if (ppuField == null || ppuField.FieldType != typeof(float)) continue;

                    float current = (float)ppuField.GetValue(data);

                    if (shouldBoost)
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

                    // Texte IMMER neu schreiben (per-unit + total)
                    WriteModuleTextsFromData(mod, data);
                }

                PersistentLocos.Main.Log($"PitStop UI price {(shouldBoost ? "boost" : "normalize")} applied (with text refresh).");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("ApplyUiPriceBoostForIndicators error: " + ex.Message);
            }
        }

        // Schreibt pricePerUnitText & totalPriceText auf Basis von Data.pricePerUnit/unitsToBuy
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

        // ==== Refresh-Hilfen, von LO-Postfixen nutzbar ====

        // Refresh NUR für PitStopStationen, die genau dieses Car selektiert haben
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

                    PersistentLocos.Main.Log("[PLP] PitStop UI refresh after ownership change (target car).");
                }
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("RefreshPitStopUiForCar error: " + ex);
            }
        }

        // Refresh für ALLE PitStops, die gerade ein Car selektiert haben (failsafe)
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

                PersistentLocos.Main.Log("[PLP] PitStop UI refresh after ownership change (all selected).");
            }
            catch (Exception ex)
            {
                PersistentLocos.Main.Warn("RefreshPitStopsForAllSelected error: " + ex);
            }
        }
    }
}