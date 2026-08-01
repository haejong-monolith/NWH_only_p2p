using Monolith.VehicleInput;
using NWH.VehiclePhysics2.Input;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// 소유 클라이언트의 로컬 차량 입력을 Network Tick마다 서버로 전송한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(VehicleInputReceiver))]
    public sealed class VehicleInputSender : NetworkBehaviour
    {
        private LocalVehicleInputReader inputReader;
        private VehicleInputReceiver inputReceiver;
        private bool tickSubscribed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            inputReceiver = GetComponent<VehicleInputReceiver>();

            if (!IsOwner)
            {
                return;
            }

            inputReader = FindFirstObjectByType<LocalVehicleInputReader>();

            if (inputReader == null)
            {
                Debug.LogError(
                    "[VehicleInputSender] LocalVehicleInputReader를 찾지 못했습니다.",
                    this);

                return;
            }

            NetworkTickRunner.Tick += OnNetworkTick;
            tickSubscribed = true;

            Debug.Log(
                $"[VehicleInputSender] 입력 전송 시작 | " +
                $"OwnerClientId={OwnerClientId}",
                this);
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeTick();
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            UnsubscribeTick();
        }

        private void UnsubscribeTick()
        {
            if (!tickSubscribed)
            {
                return;
            }

            NetworkTickRunner.Tick -= OnNetworkTick;
            tickSubscribed = false;
        }

        private void OnNetworkTick()
        {
            if (!IsSpawned || !IsOwner || inputReader == null)
            {
                return;
            }

            VehicleInputState state = inputReader.State;

            if (state == null)
            {
                return;
            }

            VehicleInputNetworkState networkInput =
                CreateNetworkState(state);

            SubmitInputServerRpc(networkInput);

            // 이번 Network Tick에 포함된 단발 입력을 소비한다.
            inputReader.ConsumeOneShotInput();
        }

        private VehicleInputNetworkState CreateNetworkState(
            VehicleInputState state)
        {
            return new VehicleInputNetworkState
            {
                Tick = GetLocalTick(),

                Steering = state.Steering,
                Throttle = state.Throttle,
                Brakes = state.Brakes,
                Clutch = state.Clutch,
                Handbrake = state.Handbrake,

                Horn = state.Horn,
                Boost = state.Boost,

                EngineStartStop = state.EngineStartStop,
                ExtraLights = state.ExtraLights,
                HighBeamLights = state.HighBeamLights,
                HazardLights = state.HazardLights,
                LeftBlinker = state.LeftBlinker,
                LowBeamLights = state.LowBeamLights,
                RightBlinker = state.RightBlinker,

                ShiftDown = state.ShiftDown,
                ShiftUp = state.ShiftUp,

                TrailerAttachDetach = state.TrailerAttachDetach,
                FlipOver = state.FlipOver,
                CruiseControl = state.CruiseControl,

                ShiftInto = state.ShiftInto
            };
        }

        private uint GetLocalTick()
        {
            if (NetworkManager == null)
            {
                return 0;
            }

            return unchecked((uint)NetworkManager.LocalTime.Tick);
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputServerRpc(
            VehicleInputNetworkState input,
            ServerRpcParams rpcParams = default)
        {
            if (inputReceiver == null)
            {
                inputReceiver = GetComponent<VehicleInputReceiver>();
            }

            inputReceiver.ReceiveInput(
                input,
                rpcParams.Receive.SenderClientId);
        }
    }
}