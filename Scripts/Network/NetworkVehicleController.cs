using Monolith.VehicleInput;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// 서버 차량의 운전자 관계와 NWH 입력 적용을 관리한다.
    /// NWH 입력 소비 및 물리 시뮬레이션은 Server에서만 실행한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkVehicleController : NetworkBehaviour
    {
        public const ulong NoDriverClientId = ulong.MaxValue;

        private readonly NetworkVariable<ulong> driverClientId =
            new NetworkVariable<ulong>(NoDriverClientId);

        private readonly VehicleInputState runtimeInput =
            new VehicleInputState();

        private NwhVehicleInputConsumer inputConsumer;
        private VehicleInputReceiver inputReceiver;

        private uint lastAppliedInputTick;
        private bool hasAppliedInputTick;

        public ulong DriverClientId => driverClientId.Value;

        public bool HasDriver =>
            driverClientId.Value != NoDriverClientId;

        public bool IsDrivenBy(ulong clientId)
        {
            return HasDriver && driverClientId.Value == clientId;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            inputConsumer = GetComponent<NwhVehicleInputConsumer>();

            if (IsServer)
            {
                if (inputConsumer == null)
                {
                    Debug.LogError(
                        "[NetworkVehicle] NwhVehicleInputConsumer가 없습니다.",
                        this);

                    enabled = false;
                    return;
                }

                inputConsumer.Bind(runtimeInput);
            }
            else
            {
                // 클라이언트는 NWH 입력/물리를 실행하지 않는다.
                inputConsumer?.Unbind();
            }

            Debug.Log(
                $"[NetworkVehicle] Spawned | " +
                $"ObjectId={NetworkObjectId} | " +
                $"IsServer={IsServer} | " +
                $"Driver={FormatDriverId(driverClientId.Value)}",
                this);
        }

        public override void OnNetworkDespawn()
        {
            inputReceiver = null;
            runtimeInput.ResetAll();
            hasAppliedInputTick = false;

            inputConsumer?.Unbind();

            base.OnNetworkDespawn();
        }

        public bool TryAssignDriver(VehicleInputReceiver receiver)
        {
            if (!IsServer)
            {
                Debug.LogWarning(
                    "[NetworkVehicle] 클라이언트에서 운전자 배정을 시도했습니다.",
                    this);

                return false;
            }

            if (receiver == null || !receiver.IsSpawned)
            {
                Debug.LogWarning(
                    "[NetworkVehicle] 유효하지 않은 입력 Receiver입니다.",
                    this);

                return false;
            }

            if (HasDriver)
            {
                return false;
            }

            inputReceiver = receiver;
            driverClientId.Value = receiver.OwnerClientId;
            hasAppliedInputTick = false;

            Debug.Log(
                $"[NetworkVehicle] Driver Assigned | " +
                $"VehicleObjectId={NetworkObjectId} | " +
                $"DriverClientId={driverClientId.Value} | " +
                $"PlayerObjectId={receiver.NetworkObjectId}",
                this);

            return true;
        }

        public void ClearDriver(bool replicateDriverChange = true)
        {
            if (!IsServer)
            {
                return;
            }

            inputReceiver?.ClearInput();

            runtimeInput.ResetAll();
            hasAppliedInputTick = false;

            Debug.Log(
                $"[NetworkVehicle] Driver Cleared | " +
                $"VehicleObjectId={NetworkObjectId} | " +
                $"PreviousDriver={FormatDriverId(driverClientId.Value)}",
                this);

            inputReceiver = null;

            if (replicateDriverChange &&
                IsSpawned &&
                NetworkManager != null &&
                NetworkManager.IsListening)
            {
                driverClientId.Value = NoDriverClientId;
            }
        }

        public bool TryGetDriverInput(
            out VehicleInputNetworkState input)
        {
            input = default;

            if (!IsServer || inputReceiver == null || !HasDriver)
            {
                return false;
            }

            return inputReceiver.TryGetLatestInput(out input);
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            if (!HasDriver ||
                !TryGetDriverInput(out VehicleInputNetworkState input))
            {
                runtimeInput.ResetAll();
                hasAppliedInputTick = false;
                return;
            }

            if (hasAppliedInputTick &&
                input.Tick == lastAppliedInputTick)
            {
                // 같은 네트워크 입력을 여러 렌더 프레임에서 적용해도
                // 단발 입력은 한 번만 소비되도록 한다.
                runtimeInput.ResetOneShot();
                return;
            }

            CopyInput(input, runtimeInput);

            lastAppliedInputTick = input.Tick;
            hasAppliedInputTick = true;
        }

        private static void CopyInput(
            VehicleInputNetworkState source,
            VehicleInputState destination)
        {
            destination.Steering = source.Steering;
            destination.Throttle = source.Throttle;
            destination.Brakes = source.Brakes;
            destination.Clutch = source.Clutch;
            destination.Handbrake = source.Handbrake;

            destination.Horn = source.Horn;
            destination.Boost = source.Boost;

            destination.ShiftUp = source.ShiftUp;
            destination.ShiftDown = source.ShiftDown;
            destination.ShiftInto = source.ShiftInto;

            destination.EngineStartStop = source.EngineStartStop;

            destination.LeftBlinker = source.LeftBlinker;
            destination.RightBlinker = source.RightBlinker;
            destination.LowBeamLights = source.LowBeamLights;
            destination.HighBeamLights = source.HighBeamLights;
            destination.HazardLights = source.HazardLights;
            destination.ExtraLights = source.ExtraLights;

            destination.TrailerAttachDetach =
                source.TrailerAttachDetach;

            destination.CruiseControl = source.CruiseControl;
            destination.FlipOver = source.FlipOver;
        }

        private static string FormatDriverId(ulong clientId)
        {
            return clientId == NoDriverClientId
                ? "None"
                : clientId.ToString();
        }
    }
}
