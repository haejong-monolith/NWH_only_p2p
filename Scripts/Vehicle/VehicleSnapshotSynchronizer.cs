using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// Server의 차량 물리 결과를 Network Tick마다 전송한다.
    /// Host는 authoritative 차량을 즉시 표시하고,
    /// 순수 Client만 수신한 Snapshot을 지연 보간한다.
    /// </summary>
    // NWH CameraMouseDrag가 LateUpdate에서 차량 Transform을 읽기 전에
    // 원격 Snapshot Pose를 먼저 적용해야 한다. 카메라보다 늦게 적용하면
    // 차량의 자식인 카메라가 한 프레임에 두 번 회전하여 조향 중 떨린다.
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class VehicleSnapshotSynchronizer : NetworkBehaviour
    {
        [Header("Authoritative Server Simulation")]

        [SerializeField]
        private Transform simulationRoot;

        [SerializeField]
        private Rigidbody simulationRigidbody;

        [Tooltip("NWH가 갱신하는 바퀴 등 꼭 필요한 Transform만 순서대로 지정합니다.")]
        [SerializeField]
        private Transform[] simulationVisualParts;

        [Header("Remote Client Interpolation")]

        [Min(0f)]
        [SerializeField]
        private float interpolationDelay = 0.1f;

        [Range(4, 128)]
        [SerializeField]
        private int bufferCapacity = 32;

        private VehicleSnapshotBuffer snapshotBuffer;
        private bool tickSubscribed;
        private bool configurationValid;
        private double lastSentFixedTime = double.NegativeInfinity;

        // Snapshot의 ServerTime에는 Network Tick 시각이 아니라
        // Pose가 실제로 생성된 서버 물리 시각이 들어간다.
        // 첫 Snapshot의 물리 시각에 로컬 단조 시계를 한 번 고정한 뒤,
        // 화면 재생 시각은 절대로 뒤로 가지 않도록 별도로 진행한다.
        private bool playbackClockInitialized;
        private double playbackStartServerTime;
        private double playbackStartRealtime;

        public int BufferedSnapshotCount =>
            snapshotBuffer != null ? snapshotBuffer.Count : 0;

        public float InterpolationDelay => interpolationDelay;

        public bool IsInterpolatingRemoteClient =>
            IsSpawned && IsClient && !IsServer;

        private void Awake()
        {
            if (simulationRoot == null)
            {
                simulationRoot = transform;
            }

            if (simulationRigidbody == null && simulationRoot != null)
            {
                simulationRigidbody =
                    simulationRoot.GetComponent<Rigidbody>();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            configurationValid = ValidateConfiguration();

            if (!configurationValid)
            {
                enabled = false;
                return;
            }

            // Host는 서버 물리 결과를 직접 표시하므로 버퍼가 필요 없다.
            if (IsClient && !IsServer)
            {
                snapshotBuffer = new VehicleSnapshotBuffer(bufferCapacity);
                ResetPlaybackClock();
            }

            if (IsServer)
            {
                lastSentFixedTime = double.NegativeInfinity;
                NetworkTickRunner.Tick += OnServerNetworkTick;
                tickSubscribed = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeTick();
            snapshotBuffer?.Clear();
            snapshotBuffer = null;
            ResetPlaybackClock();

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            UnsubscribeTick();
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            if (!configurationValid ||
                !IsSpawned ||
                !IsClient ||
                IsServer ||
                snapshotBuffer == null ||
                NetworkManager == null ||
                !playbackClockInitialized)
            {
                return;
            }

            // NGO ServerTime을 프레임마다 다시 읽지 않는다.
            // realtimeSinceStartup은 Client의 시간 동기화 보정과 무관하게
            // 단조롭게 증가하므로 Snapshot 재생이 뒤로 튀지 않는다.
            double elapsed =
                Time.realtimeSinceStartupAsDouble -
                playbackStartRealtime;

            if (elapsed < 0d)
            {
                elapsed = 0d;
            }

            double renderTime = playbackStartServerTime + elapsed;

            if (!snapshotBuffer.TrySample(
                    renderTime,
                    out VehicleSnapshot snapshot))
            {
                return;
            }

            ApplySnapshot(snapshot);
        }

        private void OnServerNetworkTick()
        {
            if (!IsSpawned || !IsServer || NetworkManager == null)
            {
                return;
            }

            // 렌더 프레임이 잠시 느려지면 NGO가 한 프레임 안에서
            // 여러 Network Tick을 따라잡을 수 있다. 그때 동일한 물리
            // Pose를 서로 다른 시각의 Snapshot으로 반복 전송하면
            // Client 보간 결과가 '정지 후 점프'하는 형태가 된다.
            double currentFixedTime = Time.fixedTimeAsDouble;

            if (currentFixedTime <= lastSentFixedTime)
            {
                return;
            }

            lastSentFixedTime = currentFixedTime;

            VehicleSnapshot snapshot = CaptureSnapshot(currentFixedTime);
            PushSnapshotClientRpc(snapshot);
        }

        private VehicleSnapshot CaptureSnapshot(double physicsTime)
        {
            // Server Rigidbody는 Interpolate를 사용할 수 있으므로
            // Transform Pose에는 렌더 보간값이 섞일 수 있다.
            // 네트워크 Snapshot은 authoritative 물리 Pose를 직접 읽는다.
            Vector3 authoritativePosition = simulationRigidbody != null
                ? simulationRigidbody.position
                : simulationRoot.position;

            Quaternion authoritativeRotation = simulationRigidbody != null
                ? simulationRigidbody.rotation
                : simulationRoot.rotation;

            VehicleSnapshot snapshot = new VehicleSnapshot
            {
                Tick = unchecked((uint)NetworkManager.ServerTime.Tick),

                // 이 Pose는 마지막 FixedUpdate에서 생성되었으므로
                // Network Tick 시각이 아니라 같은 물리 스텝의 시각을
                // 기록해야 위치/회전 보간 속도가 흔들리지 않는다.
                ServerTime = physicsTime,

                Position = authoritativePosition,
                Rotation = authoritativeRotation,

                Velocity = simulationRigidbody != null
                    ? simulationRigidbody.velocity
                    : Vector3.zero,

                AngularVelocity = simulationRigidbody != null
                    ? simulationRigidbody.angularVelocity
                    : Vector3.zero
            };

            int partCount = Mathf.Min(
                simulationVisualParts.Length,
                VehicleSnapshot.MaxVisualPartCount);

            for (int i = 0; i < partCount; i++)
            {
                Transform part = simulationVisualParts[i];

                snapshot.VisualParts.Add(
                    new VehicleVisualPartSnapshot
                    {
                        LocalPosition = part.localPosition,
                        LocalRotation = part.localRotation
                    });
            }

            return snapshot;
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void PushSnapshotClientRpc(VehicleSnapshot snapshot)
        {
            // Host도 ClientRpc 호출 대상이지만 authoritative Transform을
            // 덮어쓰지 않고 즉시 표시해야 하므로 수신 결과를 버린다.
            if (!IsClient || IsServer || snapshotBuffer == null)
            {
                return;
            }

            bool added = snapshotBuffer.Add(snapshot);

            if (added && !playbackClockInitialized)
            {
                playbackStartServerTime =
                    snapshot.ServerTime - interpolationDelay;

                playbackStartRealtime =
                    Time.realtimeSinceStartupAsDouble;

                playbackClockInitialized = true;
            }
        }

        private void ResetPlaybackClock()
        {
            playbackClockInitialized = false;
            playbackStartServerTime = 0d;
            playbackStartRealtime = 0d;
        }

        private void ApplySnapshot(VehicleSnapshot snapshot)
        {
            simulationRoot.SetPositionAndRotation(
                snapshot.Position,
                snapshot.Rotation);

            int partCount = Mathf.Min(
                snapshot.VisualParts.Length,
                simulationVisualParts.Length);

            for (int i = 0; i < partCount; i++)
            {
                Transform target = simulationVisualParts[i];
                VehicleVisualPartSnapshot part =
                    snapshot.VisualParts[i];

                target.localPosition = part.LocalPosition;
                target.localRotation = part.LocalRotation;
            }
        }

        private bool ValidateConfiguration()
        {
            if (simulationRoot == null)
            {
                Debug.LogError(
                    "[VehicleSnapshot] Simulation Root가 필요합니다.",
                    this);

                return false;
            }

            if (simulationVisualParts == null)
            {
                simulationVisualParts = new Transform[0];
            }

            if (simulationVisualParts.Length >
                VehicleSnapshot.MaxVisualPartCount)
            {
                Debug.LogError(
                    $"[VehicleSnapshot] 동기화 파트는 최대 " +
                    $"{VehicleSnapshot.MaxVisualPartCount}개입니다.",
                    this);

                return false;
            }

            for (int i = 0; i < simulationVisualParts.Length; i++)
            {
                if (simulationVisualParts[i] == null)
                {
                    Debug.LogError(
                        $"[VehicleSnapshot] 파트 {i}의 참조가 비어 있습니다.",
                        this);

                    return false;
                }
            }

            return true;
        }

        private void UnsubscribeTick()
        {
            if (!tickSubscribed)
            {
                return;
            }

            NetworkTickRunner.Tick -= OnServerNetworkTick;
            tickSubscribed = false;
        }
    }
}
