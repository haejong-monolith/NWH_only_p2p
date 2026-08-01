using System.Collections.Generic;

namespace Blindfly.Networking
{
    /// <summary>
    /// 수신한 스냅샷을 시간순으로 보관하고 지정된 과거 시각의 상태를 보간한다.
    /// 최신 상태 이후에는 외삽하지 않고 마지막 상태를 유지한다.
    /// </summary>
    public sealed class VehicleSnapshotBuffer
    {
        private readonly List<VehicleSnapshot> snapshots;
        private readonly int capacity;

        public int Count => snapshots.Count;

        public VehicleSnapshotBuffer(int capacity)
        {
            this.capacity = capacity < 2 ? 2 : capacity;
            snapshots = new List<VehicleSnapshot>(this.capacity);
        }

        public void Clear()
        {
            snapshots.Clear();
        }

        public bool Add(VehicleSnapshot snapshot)
        {
            if (snapshots.Count > 0)
            {
                VehicleSnapshot latest = snapshots[snapshots.Count - 1];

                if (!IsNewerTick(snapshot.Tick, latest.Tick) ||
                    snapshot.ServerTime <= latest.ServerTime)
                {
                    return false;
                }
            }

            snapshots.Add(snapshot);

            if (snapshots.Count > capacity)
            {
                snapshots.RemoveAt(0);
            }

            return true;
        }

        public bool TrySample(
            double renderTime,
            out VehicleSnapshot snapshot)
        {
            snapshot = default;

            if (snapshots.Count == 0)
            {
                return false;
            }

            if (snapshots.Count == 1 ||
                renderTime <= snapshots[0].ServerTime)
            {
                snapshot = snapshots[0];
                return true;
            }

            for (int i = 1; i < snapshots.Count; i++)
            {
                VehicleSnapshot to = snapshots[i];

                if (renderTime > to.ServerTime)
                {
                    continue;
                }

                VehicleSnapshot from = snapshots[i - 1];

                snapshot = VehicleSnapshot.Interpolate(
                    from,
                    to,
                    renderTime);

                // 다음 프레임에도 from/to 쌍을 사용할 수 있도록
                // from보다 오래된 상태만 제거한다.
                if (i > 1)
                {
                    snapshots.RemoveRange(0, i - 1);
                }

                return true;
            }

            // 예측/외삽을 사용하지 않는 설계이므로 패킷 공백 동안
            // 마지막으로 확정된 서버 상태를 그대로 유지한다.
            snapshot = snapshots[snapshots.Count - 1];

            if (snapshots.Count > 2)
            {
                snapshots.RemoveRange(0, snapshots.Count - 2);
            }

            return true;
        }

        private static bool IsNewerTick(uint candidate, uint current)
        {
            return unchecked((int)(candidate - current)) > 0;
        }
    }
}
