using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// 서버에서 해당 NetworkPlayer의 최신 차량 입력을 보관한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class VehicleInputReceiver : NetworkBehaviour
    {
        private VehicleInputNetworkState latestInput;
        private bool hasInput;

        public bool HasInput => IsServer && hasInput;

        public VehicleInputNetworkState LatestInput
        {
            get
            {
                if (!IsServer)
                {
                    Debug.LogWarning(
                        "[VehicleInputReceiver] 서버가 아닌 곳에서 입력 버퍼를 조회했습니다.",
                        this);
                }

                return latestInput;
            }
        }

        internal void ReceiveInput(
            VehicleInputNetworkState input,
            ulong senderClientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (senderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[VehicleInputReceiver] 소유자가 아닌 클라이언트의 입력 거부 | " +
                    $"Sender={senderClientId} | Owner={OwnerClientId}",
                    this);

                return;
            }

            if (hasInput && !IsNewerTick(input.Tick, latestInput.Tick))
            {
                return;
            }

            latestInput = input;
            hasInput = true;

            // 임시 테스트 로그
            if (input.Tick % 25 == 0)
            {
                Debug.Log(
                    $"[Server Input] " +
                    $"Client={senderClientId} | " +
                    $"Tick={input.Tick} | " +
                    $"Steering={input.Steering:F2} | " +
                    $"Throttle={input.Throttle:F2} | " +
                    $"Brakes={input.Brakes:F2} | " +
                    $"Handbrake={input.Handbrake:F2} | " +
                    $"ShiftUp={input.ShiftUp} | " +
                    $"ShiftDown={input.ShiftDown}",
                    this);
            }
        }

        public bool TryGetLatestInput(
            out VehicleInputNetworkState input)
        {
            input = latestInput;
            return IsServer && hasInput;
        }

        public void ClearInput()
        {
            if (!IsServer)
            {
                return;
            }

            latestInput = default;
            hasInput = false;
        }

        private static bool IsNewerTick(
            uint candidate,
            uint current)
        {
            return unchecked((int)(candidate - current)) > 0;
        }
    }
}