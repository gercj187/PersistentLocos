// File: Multiplayer.cs
// Separate Multiplayer version of PersistentLocos

using System;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using UnityEngine;

namespace PersistentLocos
{
	// ============================================================
	// PACKETS
	// ============================================================

	/// <summary>
	/// Client ist vollständig geladen und fordert den aktuellen
	/// Zustand des Hosts an.
	/// </summary>
	public class ServerBoundPersistentLocosReadyPacket : IPacket
	{
		public bool Ready { get; set; }
	}

	/// <summary>
	/// Spielrelevante Einstellungen des Hosts.
	/// </summary>
	public class ClientBoundPersistentLocosSettingsPacket : IPacket
	{
		public int LocoLimit { get; set; }

		public bool EnablePersistentDamage { get; set; }

		public bool EnableUnownedServiceMultiplier { get; set; }

		public float UnownedServiceMultiplier { get; set; }

		public bool EnableRepairWithoutLicense { get; set; }

		public float RepairWithoutLicenseMultiplier { get; set; }
	}

	/// <summary>
	/// Absoluter Lokomotivzähler des Hosts.
	/// </summary>
	public class ClientBoundPersistentLocosStatePacket : IPacket
	{
		public int LocoCount { get; set; }
	}

	// ============================================================
	// CENTRAL MULTIPLAYER API
	// ============================================================

	internal static class PL_Multiplayer
	{
		private static GameObject _runtimeObject;

		/// <summary>
		/// true, wenn dieses Spiel der Multiplayer-Host ist.
		/// </summary>
		public static bool IsHost
		{
			get
			{
				try
				{
					return
						MultiplayerAPI.Instance != null &&
						MultiplayerAPI.Server != null &&
						MultiplayerAPI.Instance.IsHost;
				}
				catch
				{
					return false;
				}
			}
		}

		/// <summary>
		/// true, wenn dieses Spiel als Client verbunden ist.
		/// </summary>
		public static bool IsClient
		{
			get
			{
				try
				{
					return
						MultiplayerAPI.Instance != null &&
						MultiplayerAPI.Client != null &&
						!MultiplayerAPI.Instance.IsHost;
				}
				catch
				{
					return false;
				}
			}
		}

		public static bool IsMultiplayer =>
			IsHost || IsClient;

		/// <summary>
		/// Die Multiplayer-Version darf Weltzustand nur im
		/// Singleplayer/Fallback oder auf dem Host verändern.
		/// </summary>
		public static bool CanModifyWorld =>
			!IsMultiplayer || IsHost;

		/// <summary>
		/// Clients dürfen keinen lokalen PersistentLocos-Zähler
		/// aus ihrem Savegame laden oder hineinschreiben.
		/// </summary>
		public static bool CanUseSaveData =>
			!IsMultiplayer || IsHost;

		public static void Initialize()
		{
			if (_runtimeObject != null)
			{
				return;
			}

			_runtimeObject =
				new GameObject(
					"PersistentLocos_Multiplayer");

			UnityEngine.Object.DontDestroyOnLoad(
				_runtimeObject);

			_runtimeObject.AddComponent<
				PersistentLocosMPClient>();

			_runtimeObject.AddComponent<
				PersistentLocosMPServer>();

			Main.Log(
				"[MP] Multiplayer runtime created.");
		}

		/// <summary>
		/// Wird nach jeder Änderung des Host-Lokzählers aufgerufen.
		/// </summary>
		public static void NotifyLocoCountChanged()
		{
			if (!IsHost)
			{
				return;
			}

			PersistentLocosMPServer.Instance?
				.BroadcastLocoCount();
		}

		/// <summary>
		/// Wird nach dem Speichern der UMM-Einstellungen aufgerufen.
		/// </summary>
		public static void NotifySettingsChanged()
		{
			if (!IsHost)
			{
				return;
			}

			PersistentLocosMPServer.Instance?
				.BroadcastSettings();
		}
	}

	// ============================================================
	// CLIENT
	// ============================================================

	internal class PersistentLocosMPClient : MonoBehaviour
	{
		public static PersistentLocosMPClient Instance
		{
			get;
			private set;
		}

		private IClient _client;

		private bool _registered;
		private bool _wasClient;
		private bool _localStateCleared;
		private bool _snapshotReceived;

		private float _nextReadyRequestTime;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else if (Instance != this)
			{
				Destroy(this);
				return;
			}

			RefreshConnectionState();
		}

		private void Update()
		{
			RefreshConnectionState();

			if (!_registered)
			{
				TryRegisterClient();
			}

			TrySendReadyPacket();
		}

		private void RefreshConnectionState()
		{
			bool isClientNow =
				PL_Multiplayer.IsClient;

			IClient currentClient =
				MultiplayerAPI.Client;

			bool clientChanged =
				!ReferenceEquals(
					_client,
					currentClient);

			bool roleChanged =
				_wasClient != isClientNow;

			if (clientChanged)
			{
				_client = currentClient;

				_registered = false;
				_localStateCleared = false;
				_snapshotReceived = false;
				_nextReadyRequestTime = 0f;

				Main.Log(
					"[MP] Client connection changed. " +
					"Registration state reset.");
			}

			if (roleChanged)
			{
				_wasClient = isClientNow;

				_localStateCleared = false;
				_snapshotReceived = false;
				_nextReadyRequestTime = 0f;

				Main.Log(
					isClientNow
						? "[MP] Entered multiplayer as client."
						: "[MP] Left multiplayer client state.");
			}

			/*
			 * Ein Client darf seinen lokalen Singleplayer-Zähler
			 * nicht in die Host-Sitzung übernehmen.
			 */
			if (isClientNow &&
				!_localStateCleared)
			{
				LocoSpawnState.SetFromHost(0);

				_localStateCleared = true;

				Main.Log(
					"[MP] Local locomotive counter cleared. " +
					"Waiting for host state.");
			}

			if (!isClientNow)
			{
				_localStateCleared = false;
				_snapshotReceived = false;
				_nextReadyRequestTime = 0f;
			}
		}

		private void TryRegisterClient()
		{
			if (_registered)
			{
				return;
			}

			_client =
				MultiplayerAPI.Client;

			if (_client == null)
			{
				return;
			}

			_client.RegisterPacket<
				ClientBoundPersistentLocosSettingsPacket>(
					OnSettingsReceived);

			_client.RegisterPacket<
				ClientBoundPersistentLocosStatePacket>(
					OnStateReceived);

			_registered = true;

			Main.Log(
				"[MP] Client packet handlers registered.");
		}

		private void TrySendReadyPacket()
		{
			if (!_registered ||
				_client == null ||
				!PL_Multiplayer.IsClient ||
				_snapshotReceived)
			{
				return;
			}

			if (!AStartGameData.carsAndJobsLoadingFinished)
			{
				return;
			}

			if (Time.unscaledTime <
				_nextReadyRequestTime)
			{
				return;
			}

			_client.SendPacketToServer(
				new ServerBoundPersistentLocosReadyPacket
				{
					Ready = true
				},
				reliable: true);

			_nextReadyRequestTime =
				Time.unscaledTime + 2f;

			Main.Log(
				"[MP] Host settings and state requested.");
		}

		private void OnSettingsReceived(
			ClientBoundPersistentLocosSettingsPacket packet)
		{
			if (packet == null ||
				Main.Settings == null)
			{
				return;
			}

			/*
			 * In der separaten Multiplayer-Version übernimmt der
			 * Client für die laufende Sitzung direkt die Hostwerte.
			 *
			 * OnSaveGUI wird dabei nicht aufgerufen, daher werden
			 * die Werte nicht automatisch in settings.xml gespeichert.
			 */
			Main.Settings.LocoLimit =
				Mathf.Clamp(
					packet.LocoLimit,
					1,
					50);

			Main.Settings.enablePersistentDamage =
				packet.EnablePersistentDamage;

			Main.Settings.enableUnownedServiceMultiplier =
				packet.EnableUnownedServiceMultiplier;

			Main.Settings.unownedServiceMultiplier =
				Mathf.Max(
					1f,
					packet.UnownedServiceMultiplier);

			Main.Settings.enableRepairWithoutLicense =
				packet.EnableRepairWithoutLicense;

			Main.Settings.repairWithoutLicenseMultiplier =
				Mathf.Max(
					1f,
					packet.RepairWithoutLicenseMultiplier);

			PersistentLocos.Plus
				.ServiceMultiplierCache.ClearAll();

			try
			{
				PersistentLocos.Plus.Helpers
					.RefreshPitStopsForAllSelected();
			}
			catch
			{
				// Ein offener Pitstop ist nicht garantiert vorhanden.
			}

			Main.Log(
				"[MP] Host settings applied.");
		}

		private void OnStateReceived(
			ClientBoundPersistentLocosStatePacket packet)
		{
			if (packet == null)
			{
				return;
			}

			LocoSpawnState.SetFromHost(
				Math.Max(
					0,
					packet.LocoCount));

			_snapshotReceived = true;

			Main.Log(
				$"[MP] Host locomotive count applied: " +
				$"{LocoSpawnState.Count}");
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}

			_client = null;
			_registered = false;
			_wasClient = false;
			_localStateCleared = false;
			_snapshotReceived = false;
			_nextReadyRequestTime = 0f;
		}
	}

	// ============================================================
	// SERVER / HOST
	// ============================================================

	internal class PersistentLocosMPServer : MonoBehaviour
	{
		public static PersistentLocosMPServer Instance
		{
			get;
			private set;
		}

		private IServer _server;

		private bool _registered;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else if (Instance != this)
			{
				Destroy(this);
				return;
			}

			TryRegisterServer();
		}

		private void Update()
		{
			if (!_registered)
			{
				TryRegisterServer();
			}
		}

		private void TryRegisterServer()
		{
			if (_registered)
			{
				return;
			}

			_server =
				MultiplayerAPI.Server;

			if (_server == null)
			{
				return;
			}

			_server.RegisterPacket<
				ServerBoundPersistentLocosReadyPacket>(
					OnClientReady);

			_registered = true;

			Main.Log(
				"[MP] Server packet handlers registered.");
		}

		private void OnClientReady(
			ServerBoundPersistentLocosReadyPacket packet,
			IPlayer sender)
		{
			if (packet == null ||
				sender == null ||
				!packet.Ready)
			{
				return;
			}

			SendSettingsToPlayer(sender);
			SendStateToPlayer(sender);

			Main.Log(
				"[MP] Host settings and locomotive count " +
				"sent to client.");
		}

		private static
			ClientBoundPersistentLocosSettingsPacket
			CreateSettingsPacket()
		{
			Settings settings =
				Main.Settings;

			return new
				ClientBoundPersistentLocosSettingsPacket
				{
					LocoLimit =
						settings?.LocoLimit ?? 31,

					EnablePersistentDamage =
						settings?.enablePersistentDamage
						?? true,

					EnableUnownedServiceMultiplier =
						settings?.enableUnownedServiceMultiplier
						?? true,

					UnownedServiceMultiplier =
						settings?.unownedServiceMultiplier
						?? 1.5f,

					EnableRepairWithoutLicense =
						settings?.enableRepairWithoutLicense
						?? true,

					RepairWithoutLicenseMultiplier =
						settings?.repairWithoutLicenseMultiplier
						?? 2f
				};
		}

		private static
			ClientBoundPersistentLocosStatePacket
			CreateStatePacket()
		{
			return new
				ClientBoundPersistentLocosStatePacket
				{
					LocoCount =
						LocoSpawnState.Count
				};
		}

		private void SendSettingsToPlayer(
			IPlayer player)
		{
			if (_server == null ||
				player == null)
			{
				return;
			}

			_server.SendPacketToPlayer(
				CreateSettingsPacket(),
				player,
				reliable: true);
		}

		private void SendStateToPlayer(
			IPlayer player)
		{
			if (_server == null ||
				player == null)
			{
				return;
			}

			_server.SendPacketToPlayer(
				CreateStatePacket(),
				player,
				reliable: true);
		}

		public void BroadcastSettings()
		{
			if (!_registered ||
				_server == null ||
				!PL_Multiplayer.IsHost)
			{
				return;
			}

			_server.SendPacketToAll(
				CreateSettingsPacket(),
				reliable: true,
				excludeSelf: true);

			Main.Log(
				"[MP] Host settings broadcast.");
		}

		public void BroadcastLocoCount()
		{
			if (!_registered ||
				_server == null ||
				!PL_Multiplayer.IsHost)
			{
				return;
			}

			_server.SendPacketToAll(
				CreateStatePacket(),
				reliable: true,
				excludeSelf: true);

			Main.Log(
				$"[MP] Locomotive count broadcast: " +
				$"{LocoSpawnState.Count}");
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}

			_server = null;
			_registered = false;
		}
	}
}