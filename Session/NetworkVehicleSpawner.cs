using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkVehicleSpawner : MonoBehaviour
    {
        [Header("Current Temporary Vehicle Prefab")]
        [SerializeField]
        private NetworkVehicleController defaultVehiclePrefab;

        [Header("Spawn Points")]
        [SerializeField]
        private VehicleSpawnPointRegistry spawnPointRegistry;

        private readonly Dictionary<int, ulong> slotOwners =
            new Dictionary<int, ulong>();

        private readonly HashSet<int> allocatedPlayerIndices =
            new HashSet<int>();

        public bool TryAllocatePlayerIndex(
            out int playerIndex)
        {
            playerIndex = -1;

            if (!IsServerRunning())
            {
                return false;
            }

            int candidate = 0;

            while (allocatedPlayerIndices.Contains(candidate))
            {
                candidate++;
            }

            allocatedPlayerIndices.Add(candidate);
            playerIndex = candidate;

            return true;
        }

        public void ReleasePlayerIndex(
            int playerIndex)
        {
            if (playerIndex < 0)
            {
                return;
            }

            allocatedPlayerIndices.Remove(playerIndex);
        }

        public bool TrySpawnVehicle(
            ulong clientId,
            int requestedSlotNumber,
            int vehicleTypeId,
            VehicleInputReceiver inputReceiver,
            out NetworkVehicleController vehicle,
            out int confirmedSlotNumber)
        {
            vehicle = null;
            confirmedSlotNumber = -1;

            if (!IsServerRunning())
            {
                Debug.LogWarning(
                    "[VehicleSpawner] 서버가 실행 중이 아닙니다.",
                    this);

                return false;
            }

            if (defaultVehiclePrefab == null ||
                spawnPointRegistry == null ||
                inputReceiver == null)
            {
                Debug.LogError(
                    "[VehicleSpawner] 필수 참조가 없습니다.",
                    this);

                return false;
            }

            if (!inputReceiver.IsSpawned ||
                inputReceiver.OwnerClientId != clientId)
            {
                Debug.LogWarning(
                    $"[VehicleSpawner] 유효하지 않은 입력 Receiver입니다. | " +
                    $"ClientId={clientId}",
                    this);

                return false;
            }

            if (!TryReserveSlot(
                    clientId,
                    requestedSlotNumber,
                    out confirmedSlotNumber))
            {
                Debug.LogWarning(
                    $"[VehicleSpawner] 사용 가능한 슬롯이 없습니다. | " +
                    $"ClientId={clientId}",
                    this);

                return false;
            }

            if (!spawnPointRegistry.TryGetSpawnPoint(
                    confirmedSlotNumber,
                    out Transform spawnPoint))
            {
                ReleaseSlot(clientId, confirmedSlotNumber);
                confirmedSlotNumber = -1;
                return false;
            }

            // TODO: VehicleTypeId에 맞는 프리팹 목록으로 교체한다.
            NetworkVehicleController prefab =
                defaultVehiclePrefab;

            vehicle = Instantiate(
                prefab,
                spawnPoint.position,
                spawnPoint.rotation);

            NetworkObject vehicleNetworkObject =
                vehicle.NetworkObject;

            if (vehicleNetworkObject == null)
            {
                Debug.LogError(
                    "[VehicleSpawner] 차량 프리팹에 NetworkObject가 없습니다.",
                    vehicle);

                Destroy(vehicle.gameObject);
                ReleaseSlot(clientId, confirmedSlotNumber);
                vehicle = null;
                confirmedSlotNumber = -1;

                return false;
            }

            // 서버 권위로 Spawn하고 운전자 관계를 먼저 초기화한다.
            vehicleNetworkObject.Spawn();

            if (!vehicle.TryAssignDriver(inputReceiver))
            {
                vehicleNetworkObject.Despawn(true);
                ReleaseSlot(clientId, confirmedSlotNumber);

                vehicle = null;
                confirmedSlotNumber = -1;

                return false;
            }

            // 물리 시뮬레이션은 계속 서버에서만 실행하지만,
            // IsOwner 기반의 로컬 카메라/UI 판정을 위해 NGO 소유권은
            // 실제 운전자 클라이언트에 넘긴다.
            if (vehicleNetworkObject.OwnerClientId != clientId)
            {
                vehicleNetworkObject.ChangeOwnership(clientId);
            }

            Debug.Log(
                $"[VehicleSpawner] Vehicle Spawned | " +
                $"ClientId={clientId} | " +
                $"RequestedSlot={requestedSlotNumber} | " +
                $"ConfirmedSlot={confirmedSlotNumber} | " +
                $"VehicleTypeId={vehicleTypeId} | " +
                $"VehicleObjectId={vehicle.NetworkObjectId}",
                vehicle);

            return true;
        }

        public void DespawnVehicle(
            ulong clientId,
            int slotNumber,
            NetworkVehicleController vehicle)
        {
            if (vehicle != null)
            {
                vehicle.ClearDriver(false);

                NetworkObject vehicleNetworkObject =
                    vehicle.NetworkObject;

                if (IsServerRunning() &&
                    vehicleNetworkObject != null &&
                    vehicleNetworkObject.IsSpawned)
                {
                    vehicleNetworkObject.Despawn(true);
                }
            }

            ReleaseSlot(clientId, slotNumber);
        }

        private bool TryReserveSlot(
            ulong clientId,
            int requestedSlotNumber,
            out int confirmedSlotNumber)
        {
            confirmedSlotNumber = -1;

            if (IsSlotAvailable(requestedSlotNumber))
            {
                slotOwners.Add(requestedSlotNumber, clientId);
                confirmedSlotNumber = requestedSlotNumber;
                return true;
            }

            for (int slot = 0;
                 slot < spawnPointRegistry.Count;
                 slot++)
            {
                if (!IsSlotAvailable(slot))
                {
                    continue;
                }

                slotOwners.Add(slot, clientId);
                confirmedSlotNumber = slot;
                return true;
            }

            return false;
        }

        private bool IsSlotAvailable(
            int slotNumber)
        {
            return slotNumber >= 0 &&
                   slotNumber < spawnPointRegistry.Count &&
                   !slotOwners.ContainsKey(slotNumber);
        }

        private void ReleaseSlot(
            ulong clientId,
            int slotNumber)
        {
            if (!slotOwners.TryGetValue(
                    slotNumber,
                    out ulong ownerClientId))
            {
                return;
            }

            if (ownerClientId != clientId)
            {
                return;
            }

            slotOwners.Remove(slotNumber);
        }

        private static bool IsServerRunning()
        {
            NetworkManager networkManager =
                NetworkManager.Singleton;

            return networkManager != null &&
                   networkManager.IsServer &&
                   networkManager.IsListening;
        }

        private void OnDestroy()
        {
            slotOwners.Clear();
            allocatedPlayerIndices.Clear();
        }
    }
}
