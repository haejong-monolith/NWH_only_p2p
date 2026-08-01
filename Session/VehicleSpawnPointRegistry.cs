using UnityEngine;

namespace Blindfly.Networking
{
    [DisallowMultipleComponent]
    public sealed class VehicleSpawnPointRegistry : MonoBehaviour
    {
        [SerializeField]
        private Transform[] spawnPoints;

        public int Count =>
            spawnPoints?.Length ?? 0;

        public bool TryGetSpawnPoint(
            int slotNumber,
            out Transform spawnPoint)
        {
            spawnPoint = null;

            if (spawnPoints == null ||
                slotNumber < 0 ||
                slotNumber >= spawnPoints.Length)
            {
                return false;
            }

            spawnPoint =
                spawnPoints[slotNumber];

            return spawnPoint != null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (spawnPoints == null)
            {
                return;
            }

            for (int i = 0;
                 i < spawnPoints.Length;
                 i++)
            {
                if (spawnPoints[i] != null)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[VehicleSpawnPointRegistry] Slot {i}의 Transform이 비어 있습니다.",
                    this);
            }
        }
#endif
    }
}
