using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// 서버의 시뮬레이션 파트 하나를 Presentation 파트로 복제하기 위한 로컬 Pose.
    /// 모든 Transform을 보내지 않고 Inspector에서 선택한 파트만 담는다.
    /// </summary>
    public struct VehicleVisualPartSnapshot : INetworkSerializable
    {
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref LocalPosition);
            serializer.SerializeValue(ref LocalRotation);
        }

        public static VehicleVisualPartSnapshot Interpolate(
            VehicleVisualPartSnapshot from,
            VehicleVisualPartSnapshot to,
            float t)
        {
            return new VehicleVisualPartSnapshot
            {
                LocalPosition = Vector3.LerpUnclamped(
                    from.LocalPosition,
                    to.LocalPosition,
                    t),

                LocalRotation = Quaternion.SlerpUnclamped(
                    from.LocalRotation,
                    to.LocalRotation,
                    t)
            };
        }
    }

    /// <summary>
    /// 서버에서 생성하여 클라이언트가 지연 보간하는 차량 상태.
    /// 속도는 향후 표시/진단용이며 현재 보간에서 외삽에는 사용하지 않는다.
    /// </summary>
    public struct VehicleSnapshot : INetworkSerializable
    {
        public const int MaxVisualPartCount = 12;

        public uint Tick;
        public double ServerTime;

        public Vector3 Position;
        public Quaternion Rotation;

        public Vector3 Velocity;
        public Vector3 AngularVelocity;

        // NWH LightsManager.GetIntState()의 결과.
        // bit 0~7에 브레이크등, 미등, 후진등, 하향등, 상향등,
        // 좌/우 방향지시등, 추가 조명의 실제 출력 상태가 들어간다.
        public int LightState;

        public FixedList512Bytes<VehicleVisualPartSnapshot> VisualParts;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref ServerTime);

            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);

            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref AngularVelocity);
            serializer.SerializeValue(ref LightState);

            int partCount = VisualParts.Length;
            serializer.SerializeValue(ref partCount);

            if (serializer.IsReader)
            {
                VisualParts.Clear();

                int capacity = VisualParts.Capacity;

                for (int i = 0; i < partCount; i++)
                {
                    VehicleVisualPartSnapshot part = default;
                    part.NetworkSerialize(serializer);

                    // 잘못된 패킷이 와도 나머지 바이트는 읽되
                    // FixedList 용량을 넘겨 기록하지 않는다.
                    if (i < capacity && i < MaxVisualPartCount)
                    {
                        VisualParts.Add(part);
                    }
                }

                return;
            }

            for (int i = 0; i < partCount; i++)
            {
                VehicleVisualPartSnapshot part = VisualParts[i];
                part.NetworkSerialize(serializer);
            }
        }

        public static VehicleSnapshot Interpolate(
            VehicleSnapshot from,
            VehicleSnapshot to,
            double renderTime)
        {
            double duration = to.ServerTime - from.ServerTime;

            float t = duration <= 0.000001d
                ? 1f
                : Mathf.Clamp01((float)(
                    (renderTime - from.ServerTime) / duration));

            VehicleSnapshot result = new VehicleSnapshot
            {
                Tick = to.Tick,
                ServerTime = renderTime,

                Position = Vector3.LerpUnclamped(
                    from.Position,
                    to.Position,
                    t),

                Rotation = Quaternion.SlerpUnclamped(
                    from.Rotation,
                    to.Rotation,
                    t),

                Velocity = Vector3.LerpUnclamped(
                    from.Velocity,
                    to.Velocity,
                    t),

                AngularVelocity = Vector3.LerpUnclamped(
                    from.AngularVelocity,
                    to.AngularVelocity,
                    t),

                // 조명은 연속값이 아니므로 다음 Snapshot 시각에 도달할
                // 때까지 현재 확정 상태를 유지한다.
                LightState = t < 1f
                    ? from.LightState
                    : to.LightState
            };

            int partCount = Mathf.Min(
                from.VisualParts.Length,
                to.VisualParts.Length);

            partCount = Mathf.Min(partCount, MaxVisualPartCount);

            for (int i = 0; i < partCount; i++)
            {
                result.VisualParts.Add(
                    VehicleVisualPartSnapshot.Interpolate(
                        from.VisualParts[i],
                        to.VisualParts[i],
                        t));
            }

            return result;
        }
    }
}
