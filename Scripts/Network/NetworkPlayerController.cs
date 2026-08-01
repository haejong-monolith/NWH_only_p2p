using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(VehicleInputReceiver))]
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        private readonly NetworkVariable<PlayerConnectionData> playerConnectionData =
            new NetworkVariable<PlayerConnectionData>(
                PlayerConnectionData.Unassigned);

        private readonly NetworkVariable<NetworkObjectReference> controlledVehicleReference =
            new NetworkVariable<NetworkObjectReference>();

        private VehicleInputReceiver inputReceiver;
        private NetworkVehicleSpawner vehicleSpawner;
        private NetworkVehicleController controlledVehicle;
        private Coroutine spawnRequestCoroutine;

        public ulong ClientId => OwnerClientId;
        public PlayerConnectionData ConnectionData => playerConnectionData.Value;
        public NetworkVehicleController ControlledVehicle => controlledVehicle;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            inputReceiver = GetComponent<VehicleInputReceiver>();

            controlledVehicleReference.OnValueChanged +=
                OnControlledVehicleChanged;

            ResolveControlledVehicle(controlledVehicleReference.Value);

            if (IsOwner)
            {
                spawnRequestCoroutine =
                    StartCoroutine(RequestSpawnWhenReady());
            }
        }

        public override void OnNetworkDespawn()
        {
            controlledVehicleReference.OnValueChanged -=
                OnControlledVehicleChanged;

            if (spawnRequestCoroutine != null)
            {
                StopCoroutine(spawnRequestCoroutine);
                spawnRequestCoroutine = null;
            }

            if (IsServer)
            {
                PlayerConnectionData confirmedData =
                    playerConnectionData.Value;

                if (vehicleSpawner == null)
                {
                    vehicleSpawner =
                        FindFirstObjectByType<NetworkVehicleSpawner>();
                }

                if (vehicleSpawner != null)
                {
                    if (controlledVehicle != null)
                    {
                        vehicleSpawner.DespawnVehicle(
                            OwnerClientId,
                            confirmedData.SlotNumber,
                            controlledVehicle);
                    }

                    vehicleSpawner.ReleasePlayerIndex(
                        confirmedData.PlayerIndex);
                }
                else if (controlledVehicle != null)
                {
                    controlledVehicle.ClearDriver(false);
                }
            }

            controlledVehicle = null;

            Debug.Log(
                $"[NetworkPlayer] Despawned | " +
                $"ObjectId={NetworkObjectId} | " +
                $"OwnerClientId={OwnerClientId}",
                this);

            base.OnNetworkDespawn();
        }

        private IEnumerator RequestSpawnWhenReady()
        {
            while (IsSpawned && IsOwner)
            {
                if (vehicleSpawner == null)
                {
                    vehicleSpawner =
                        FindFirstObjectByType<NetworkVehicleSpawner>();
                }

                if (vehicleSpawner != null)
                {
                    RequestVehicleSpawnServerRpc(
                        LocalPlayerConnection.Data);

                    spawnRequestCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            spawnRequestCoroutine = null;
        }

        private void OnControlledVehicleChanged(
            NetworkObjectReference previousValue,
            NetworkObjectReference currentValue)
        {
            ResolveControlledVehicle(currentValue);
        }

        private void ResolveControlledVehicle(
            NetworkObjectReference vehicleReference)
        {
            if (!vehicleReference.TryGet(
                    out NetworkObject vehicleObject))
            {
                controlledVehicle = null;
                return;
            }

            controlledVehicle =
                vehicleObject.GetComponent<NetworkVehicleController>();

            if (controlledVehicle == null)
            {
                Debug.LogError(
                    $"[NetworkPlayer] 지정된 NetworkObject에 " +
                    $"{nameof(NetworkVehicleController)}가 없습니다. | " +
                    $"ObjectId={vehicleObject.NetworkObjectId}",
                    this);

                return;
            }

            Debug.Log(
                $"[NetworkPlayer] Controlled Vehicle Resolved | " +
                $"ClientId={OwnerClientId} | " +
                $"VehicleObjectId={vehicleObject.NetworkObjectId} | " +
                $"IsOwner={IsOwner}",
                this);
        }

        [ServerRpc]
        private void RequestVehicleSpawnServerRpc(
            PlayerConnectionData requestedData,
            ServerRpcParams rpcParams = default)
        {
            if (!IsServer ||
                controlledVehicle != null ||
                playerConnectionData.Value.PlayerIndex >= 0)
            {
                return;
            }

            ulong senderClientId =
                rpcParams.Receive.SenderClientId;

            if (senderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[NetworkPlayer] 잘못된 Spawn 요청입니다. | " +
                    $"Sender={senderClientId} | " +
                    $"Owner={OwnerClientId}",
                    this);

                return;
            }

            if (vehicleSpawner == null)
            {
                vehicleSpawner =
                    FindFirstObjectByType<NetworkVehicleSpawner>();
            }

            if (vehicleSpawner == null)
            {
                Debug.LogError(
                    "[NetworkPlayer] NetworkVehicleSpawner를 찾지 못했습니다.",
                    this);

                return;
            }

            if (!vehicleSpawner.TryAllocatePlayerIndex(
                    out int allocatedPlayerIndex))
            {
                Debug.LogWarning(
                    $"[NetworkPlayer] PlayerIndex 할당 실패 | " +
                    $"ClientId={OwnerClientId}",
                    this);

                return;
            }

            PlayerConnectionData confirmedData = requestedData;

            confirmedData.PlayerIndex =
                allocatedPlayerIndex;

            if (!vehicleSpawner.TrySpawnVehicle(
                    OwnerClientId,
                    confirmedData.SlotNumber,
                    confirmedData.VehicleTypeId,
                    inputReceiver,
                    out NetworkVehicleController spawnedVehicle,
                    out int confirmedSlot))
            {
                vehicleSpawner.ReleasePlayerIndex(
                    allocatedPlayerIndex);

                return;
            }

            confirmedData.SlotNumber =
                confirmedSlot;

            controlledVehicle =
                spawnedVehicle;

            controlledVehicleReference.Value =
                new NetworkObjectReference(
                    spawnedVehicle.NetworkObject);

            playerConnectionData.Value =
                confirmedData;

            Debug.Log(
                $"[NetworkPlayer] Vehicle Assigned | " +
                $"ClientId={OwnerClientId} | " +
                $"PlayerIndex={confirmedData.PlayerIndex} | " +
                $"Slot={confirmedData.SlotNumber} | " +
                $"VehicleTypeId={confirmedData.VehicleTypeId} | " +
                $"VehicleObjectId={spawnedVehicle.NetworkObjectId}",
                this);
        }
    }
}
