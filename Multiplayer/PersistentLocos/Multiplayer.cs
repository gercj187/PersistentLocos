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

	public class ServerBoundPersistentLocosReadyPacket : IPacket
	{
		public bool Ready { get; set; }
	}

	public class ClientBoundPersistentLocosSettingsPacket : IPacket
	{
		public int LocoLimit { get; set; }
		public bool EnablePersistentDamage { get; set; }
		public bool EnableUnownedServiceMultiplier { get; set; }
		public float UnownedServiceMultiplier { get; set; }
		public bool EnableRepairWithoutLicense { get; set; }
		public float RepairWithoutLicenseMultiplier { get; set; }
	}

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

		public static bool IsMultiplayer => IsHost || IsClient;
		public static bool CanModifyWorld => !IsMultiplayer || IsHost;
		public static bool CanUseSaveData => !IsMultiplayer || IsHost;
		public static void Initialize()
		{
			if (_runtimeObject != null)
			{
				return;
			}

			_runtimeObject =new GameObject("PersistentLocos_Multiplayer");
			UnityEngine.Object.DontDestroyOnLoad(_runtimeObject);
			_runtimeObject.AddComponent<PersistentLocosMPClient>();
			_runtimeObject.AddComponent<PersistentLocosMPServer>();

			Main.Log("[MP] Multiplayer runtime created.");
		}

		public static void NotifyLocoCountChanged()
		{
			if (!IsHost)
			{
				return;
			}

			PersistentLocosMPServer.Instance?
				.BroadcastLocoCount();
		}

		public static void NotifySettingsChanged()
		{
			if (!IsHost)
			{
				return;
			}

			PersistentLocosMPServer.Instance?.BroadcastSettings();
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
			bool isClientNow = PL_Multiplayer.IsClient;
			IClient currentClient = MultiplayerAPI.Client;
			bool clientChanged = !ReferenceEquals(_client,currentClient);
			bool roleChanged = _wasClient != isClientNow;

			if (clientChanged)
            {
                if (_client != null)
                {
                    Main.RestoreLocalSettings();
                }

                _client = currentClient;

                _registered = false;
                _localStateCleared = false;
                _snapshotReceived = false;
                _nextReadyRequestTime = 0f;

                Main.Log(
                    "[MP] Client connection changed. " +
                    "Local settings restored and registration " +
                    "state reset.");
            }

			if (roleChanged)
            {
                bool wasClientBefore =_wasClient;
                _wasClient =isClientNow;

                _localStateCleared = false;
                _snapshotReceived = false;
                _nextReadyRequestTime = 0f;

                if (wasClientBefore && !isClientNow)
                {
                    Main.RestoreLocalSettings();
                }

                Main.Log(isClientNow
                        ? "[MP] Entered multiplayer as client."
                        : "[MP] Left multiplayer client state. " +
                          "Local settings restored.");
            }

			if (isClientNow && !_localStateCleared)
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

		private void OnSettingsReceived(ClientBoundPersistentLocosSettingsPacket packet)
        {
            if (packet == null)
                return;

            Settings hostSettings = new Settings
			{
				LocoLimit = Mathf.Clamp(packet.LocoLimit,1,50),
				enablePersistentDamage = packet.EnablePersistentDamage,
				enableUnownedServiceMultiplier = packet.EnableUnownedServiceMultiplier,
				unownedServiceMultiplier = Mathf.Clamp(packet.UnownedServiceMultiplier,1f,5f),
				enableRepairWithoutLicense = packet.EnableRepairWithoutLicense,
				repairWithoutLicenseMultiplier = Mathf.Clamp(packet.RepairWithoutLicenseMultiplier,1.5f,10f),
				enableLogging = Main.LocalSettings != null &&Main.LocalSettings.enableLogging
			};

            Main.UseTemporaryHostSettings(hostSettings);
            PersistentLocos.Plus.ServiceMultiplierCache.ClearAll();

            try
            {
                PersistentLocos.Plus.Helpers.RefreshPitStopsForAllSelected();
            }
            catch
            { }

            Main.Log("[MP] Temporary host settings applied.");
        }

		private void OnStateReceived(ClientBoundPersistentLocosStatePacket packet)
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
            Main.RestoreLocalSettings();

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