using System;
using System.IO;
using DV;
using UnityEngine;
using UnityModManagerNet;

namespace PersistentLocos
{
    public static class LocoSpawnState
	{
		private static int _count = 0;
		public static int Count => _count;

		private const string SaveKey = "PersistentLocos_LocoCount";

		public static void LoadFrom(SaveGameData saveData)
		{
			int? maybeValue = saveData.GetInt(SaveKey);
			_count = maybeValue.HasValue ? maybeValue.Value : 0;
		}

		public static void SaveTo(SaveGameData saveData)
		{
			saveData.SetInt(SaveKey, _count);
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
