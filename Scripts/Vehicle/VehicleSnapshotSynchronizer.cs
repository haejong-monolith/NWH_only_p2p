using NWH.VehiclePhysics2.Modules.Rigging;
using NWH.VehiclePhysics2;
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

        [Tooltip("서버에서 실제 조명 상태를 읽고 Client에 적용할 NWH VehicleController")]
        [SerializeField]
        private VehicleController simulationVehicleController;

        [Tooltip("NWH가 갱신하는 바퀴 등 꼭 필요한 Transform만 순서대로 지정합니다.")]
        [SerializeField]
        private Transform[] simulationVisualParts;

        [Tooltip("동기화된 바퀴 위치를 따라 Client에서 다시 계산할 NWH 서스펜션 리깅 모듈")]
        [SerializeField]
        private RiggingModuleWrapper[] simulationRiggingModules;

        [Header("Remote Client Interpolation")]

        [Min(0f)]
        [SerializeField]
        private float interpolationDelay = 0.1f;

        [Range(4, 128)]
        [SerializeField]
        private int bufferCapacity = 32;

        [Tooltip("재생 시각과 최신 Snapshot 기준 시각의 차이가 이 값을 넘으면 자동으로 재동기화합니다.")]
        [Min(0.05f)]
        [SerializeField]
        private float playbackResyncThreshold = 0.25f;

        private VehicleSnapshotBuffer snapshotBuffer;
        private bool tickSubscribed;
        private bool configurationValid;
        private double lastSentFixedTime = double.NegativeInfinity;

        // Snapshot의 ServerTime에는 Network Tick 시각이 아니라
        // Pose가 실제로 생성된 서버 물리 시각이 들어간다.
        // 첫 Snapshot의 물리 시각에 로컬 단조 시계를 고정하고,
        // 정상 상태에서는 그 기준을 유지한다. 큰 시간 오차가 감지된
        // 경우에만 최신 Snapshot 기준으로 다시 고정한다.
        private bool playbackClockInitialized;
        private double playbackStartServerTime;
        private double playbackStartRealtime;

        private ulong receivedSnapshotCount;
        private ulong acceptedSnapshotCount;
        private ulong rejectedSnapshotCount;
        private ulong missingTickCount;
        private uint latestReceivedTick;
        private bool hasReceivedTick;
        private double lastSnapshotReceivedRealtime;
        private float lastSnapshotInterval;
        private float smoothedSnapshotInterval;
        private ulong bufferUnderrunCount;
        private ulong bufferOverrunCount;
        private ulong playbackResyncCount;
        private ulong acceptedSnapshotCountAtLastResync;
        private float playbackTimeError;
        private bool bufferUnderrunActive;
        private bool bufferOverrunActive;
        private bool clientRiggingInitialized;
        private bool hasAppliedLightState;
        private int lastAppliedLightState;

        public int BufferedSnapshotCount =>
            snapshotBuffer != null ? snapshotBuffer.Count : 0;

        public int SnapshotBufferCapacity =>
            snapshotBuffer != null
                ? snapshotBuffer.Capacity
                : bufferCapacity;

        public float BufferedSnapshotTimeSpan =>
            snapshotBuffer != null
                ? Mathf.Max(0f, (float)snapshotBuffer.BufferedTimeSpan)
                : 0f;

        public float InterpolationDelay => interpolationDelay;

        public ulong ReceivedSnapshotCount => receivedSnapshotCount;

        public ulong AcceptedSnapshotCount => acceptedSnapshotCount;

        public ulong RejectedSnapshotCount => rejectedSnapshotCount;

        public ulong MissingTickCount => missingTickCount;

        public ulong BufferUnderrunCount => bufferUnderrunCount;

        public ulong BufferOverrunCount => bufferOverrunCount;

        public ulong PlaybackResyncCount => playbackResyncCount;

        public float PlaybackTimeError => playbackTimeError;

        public bool IsBufferUnderrun => bufferUnderrunActive;

        public bool IsBufferOverrun => bufferOverrunActive;

        public uint LatestReceivedTick => latestReceivedTick;

        public float LastSnapshotInterval => lastSnapshotInterval;

        public float SmoothedSnapshotInterval => smoothedSnapshotInterval;

        public float TimeSinceLastSnapshot =>
            receivedSnapshotCount == 0
                ? -1f
                : Mathf.Max(0f, (float)(
                    Time.realtimeSinceStartupAsDouble -
                    lastSnapshotReceivedRealtime));

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

            if (simulationVehicleController == null &&
                simulationRoot != null)
            {
                simulationVehicleController =
                    simulationRoot.GetComponent<VehicleController>();
            }

            if (simulationRiggingModules == null ||
                simulationRiggingModules.Length == 0)
            {
                simulationRiggingModules =
                    GetComponentsInChildren<RiggingModuleWrapper>(true);
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
                ResetDiagnostics();
                ResetLightState();
                InitializeClientRigging();
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
            ResetDiagnostics();
            ResetLightState();
            clientRiggingInitialized = false;

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
            // 단조롭게 증가하므로 정상 재생 중 시각이 뒤로 튀지 않는다.
            double elapsed =
                Time.realtimeSinceStartupAsDouble -
                playbackStartRealtime;

            if (elapsed < 0d)
            {
                elapsed = 0d;
            }

            double renderTime = playbackStartServerTime + elapsed;

            if (!snapshotBuffer.TryGetTimeRange(
                    out double oldestServerTime,
                    out double newestServerTime))
            {
                return;
            }

            EvaluatePlaybackHealth(
                ref renderTime,
                oldestServerTime,
                newestServerTime);

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
                    : Vector3.zero,

                LightState = CaptureLightState()
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

            double receivedRealtime = Time.realtimeSinceStartupAsDouble;
            receivedSnapshotCount++;

            if (receivedSnapshotCount > 1)
            {
                lastSnapshotInterval = Mathf.Max(
                    0f,
                    (float)(receivedRealtime -
                            lastSnapshotReceivedRealtime));

                smoothedSnapshotInterval = smoothedSnapshotInterval <= 0f
                    ? lastSnapshotInterval
                    : Mathf.Lerp(
                        smoothedSnapshotInterval,
                        lastSnapshotInterval,
                        0.1f);
            }

            lastSnapshotReceivedRealtime = receivedRealtime;

            if (hasReceivedTick)
            {
                uint tickDelta = unchecked(snapshot.Tick - latestReceivedTick);

                if (tickDelta > 1 && tickDelta < 0x80000000u)
                {
                    missingTickCount += tickDelta - 1;
                }

                if (tickDelta > 0 && tickDelta < 0x80000000u)
                {
                    latestReceivedTick = snapshot.Tick;
                }
            }
            else
            {
                latestReceivedTick = snapshot.Tick;
                hasReceivedTick = true;
            }

            bool added = snapshotBuffer.Add(snapshot);

            if (added)
            {
                acceptedSnapshotCount++;
            }
            else
            {
                rejectedSnapshotCount++;
            }

            if (added && !playbackClockInitialized)
            {
                playbackStartServerTime =
                    snapshot.ServerTime - interpolationDelay;

                playbackStartRealtime =
                    Time.realtimeSinceStartupAsDouble;

                playbackClockInitialized = true;
                acceptedSnapshotCountAtLastResync =
                    acceptedSnapshotCount;
            }
        }

        private void EvaluatePlaybackHealth(
            ref double renderTime,
            double oldestServerTime,
            double newestServerTime)
        {
            double desiredRenderTime =
                newestServerTime - interpolationDelay;

            if (desiredRenderTime < oldestServerTime)
            {
                desiredRenderTime = oldestServerTime;
            }

            double error = desiredRenderTime - renderTime;
            playbackTimeError = (float)error;

            bool overrun = error > playbackResyncThreshold;
            bool underrun = renderTime > newestServerTime;

            if (overrun)
            {
                if (!bufferOverrunActive)
                {
                    bufferOverrunCount++;
                }

                bufferOverrunActive = true;
                bufferUnderrunActive = false;

                TryResynchronizePlayback(
                    ref renderTime,
                    desiredRenderTime);

                return;
            }

            if (underrun)
            {
                if (!bufferUnderrunActive)
                {
                    bufferUnderrunCount++;
                }

                bufferUnderrunActive = true;
                bufferOverrunActive = false;

                if (renderTime - newestServerTime >
                    playbackResyncThreshold)
                {
                    TryResynchronizePlayback(
                        ref renderTime,
                        desiredRenderTime);
                }

                return;
            }

            bufferUnderrunActive = false;
            bufferOverrunActive = false;
        }

        private void TryResynchronizePlayback(
            ref double renderTime,
            double desiredRenderTime)
        {
            // 송신이 멈춘 동안에는 마지막 Pose를 유지한다. 새 Snapshot이
            // 들어오지 않았는데 매 임계 시간마다 재동기화 횟수만 늘어나는
            // 것을 막고, 수신이 재개된 첫 시점에 한 번만 복구한다.
            if (acceptedSnapshotCount ==
                acceptedSnapshotCountAtLastResync)
            {
                return;
            }

            playbackStartServerTime = desiredRenderTime;
            playbackStartRealtime = Time.realtimeSinceStartupAsDouble;
            acceptedSnapshotCountAtLastResync = acceptedSnapshotCount;
            playbackResyncCount++;
            playbackTimeError = 0f;
            renderTime = desiredRenderTime;
        }

        private void ResetPlaybackClock()
        {
            playbackClockInitialized = false;
            playbackStartServerTime = 0d;
            playbackStartRealtime = 0d;
        }

        private void ResetDiagnostics()
        {
            receivedSnapshotCount = 0;
            acceptedSnapshotCount = 0;
            rejectedSnapshotCount = 0;
            missingTickCount = 0;
            latestReceivedTick = 0;
            hasReceivedTick = false;
            lastSnapshotReceivedRealtime = 0d;
            lastSnapshotInterval = 0f;
            smoothedSnapshotInterval = 0f;
            bufferUnderrunCount = 0;
            bufferOverrunCount = 0;
            playbackResyncCount = 0;
            acceptedSnapshotCountAtLastResync = 0;
            playbackTimeError = 0f;
            bufferUnderrunActive = false;
            bufferOverrunActive = false;
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

            UpdateClientRigging();
            ApplyLightState(snapshot.LightState);
        }

        private int CaptureLightState()
        {
            if (simulationVehicleController == null ||
                simulationVehicleController.effectsManager == null ||
                simulationVehicleController.effectsManager.lightsManager == null)
            {
                return 0;
            }

            return simulationVehicleController
                .effectsManager
                .lightsManager
                .GetIntState();
        }

        private void ApplyLightState(int lightState)
        {
            if (simulationVehicleController == null ||
                simulationVehicleController.effectsManager == null ||
                simulationVehicleController.effectsManager.lightsManager == null ||
                hasAppliedLightState && lastAppliedLightState == lightState)
            {
                return;
            }

            simulationVehicleController
                .effectsManager
                .lightsManager
                .SetStateFromInt(lightState);

            lastAppliedLightState = lightState;
            hasAppliedLightState = true;
        }

        private void ResetLightState()
        {
            hasAppliedLightState = false;
            lastAppliedLightState = 0;
        }

        private void InitializeClientRigging()
        {
            clientRiggingInitialized = false;

            if (simulationRiggingModules == null)
            {
                return;
            }

            for (int i = 0; i < simulationRiggingModules.Length; i++)
            {
                RiggingModuleWrapper wrapper =
                    simulationRiggingModules[i];

                if (wrapper == null ||
                    wrapper.module == null ||
                    wrapper.module.bones == null)
                {
                    continue;
                }

                for (int j = 0; j < wrapper.module.bones.Count; j++)
                {
                    Bone bone = wrapper.module.bones[j];

                    if (bone != null)
                    {
                        bone.Initialize();
                    }
                }
            }

            clientRiggingInitialized = true;
        }

        private void UpdateClientRigging()
        {
            if (!clientRiggingInitialized ||
                simulationRiggingModules == null ||
                simulationRoot == null)
            {
                return;
            }

            Vector3 forward = simulationRoot.forward;
            Vector3 up = simulationRoot.up;

            for (int i = 0; i < simulationRiggingModules.Length; i++)
            {
                RiggingModuleWrapper wrapper =
                    simulationRiggingModules[i];

                if (wrapper == null ||
                    wrapper.module == null ||
                    wrapper.module.bones == null)
                {
                    continue;
                }

                for (int j = 0; j < wrapper.module.bones.Count; j++)
                {
                    Bone bone = wrapper.module.bones[j];

                    if (bone != null)
                    {
                        bone.Update(forward, up);
                    }
                }
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

            if (simulationVehicleController == null)
            {
                Debug.LogError(
                    "[VehicleSnapshot] NWH VehicleController가 필요합니다.",
                    this);

                return false;
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
