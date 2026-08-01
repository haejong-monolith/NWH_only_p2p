using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// 차량의 로컬 소유자에게만 필요한 카메라 계층을 활성화한다.
    /// 원격 차량과 Dedicated Server에서는 해당 계층을 비활성화한다.
    ///
    /// 순수 Client에서는 서버 물리 게이트가 NWH VehicleController를
    /// 비활성화하면서 CameraChanger가 모든 실제 Camera 오브젝트를 끈다.
    /// 네트워크 역할 적용이 끝난 다음 선택된 Camera 한 개를 복구한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class OwnerVehiclePresentationGate : NetworkBehaviour
    {
        private const string CameraChangerTypeName =
            "NWH.Common.Cameras.CameraChanger";

        private const string CurrentCameraIndexFieldName =
            "currentCameraIndex";

        [Header("Owner-only Presentation")]

        [Tooltip("로컬 소유 차량에서만 활성화할 Cameras 등의 루트 오브젝트")]
        [SerializeField]
        private GameObject[] ownerOnlyObjects = Array.Empty<GameObject>();

        private readonly Dictionary<GameObject, bool>
            originalGameObjectActiveSelf =
                new Dictionary<GameObject, bool>();

        private readonly Dictionary<Camera, bool>
            originalCameraEnabled =
                new Dictionary<Camera, bool>();

        private readonly Dictionary<AudioListener, bool>
            originalAudioListenerEnabled =
                new Dictionary<AudioListener, bool>();

        private bool[] originalOwnerObjectActiveSelf;
        private Coroutine delayedApplyCoroutine;

        private void Awake()
        {
            NormalizeArrays();
            CaptureOriginalState();

            // Network Spawn 이전에 원격 차량의 Camera나 AudioListener가
            // 잠시 활성화되는 것을 막는다.
            SetOwnerPresentationActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnership();
            QueueDelayedOwnershipApply();
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            ApplyOwnership();
            QueueDelayedOwnershipApply();
        }

        public override void OnLostOwnership()
        {
            CancelDelayedOwnershipApply();
            SetOwnerPresentationActive(false);
            base.OnLostOwnership();
        }

        public override void OnNetworkDespawn()
        {
            CancelDelayedOwnershipApply();
            RestoreOriginalState();
            base.OnNetworkDespawn();
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsClient || !IsOwner)
            {
                return;
            }

            // NWH VehicleController.OnDisable 이벤트가 뒤늦게 모든
            // Camera 자식을 꺼도 로컬 소유 차량에는 항상 렌더링 가능한
            // Camera 한 개가 남도록 복구한다.
            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                GameObject ownerOnlyObject = ownerOnlyObjects[i];

                if (ownerOnlyObject == null)
                {
                    continue;
                }

                if (!ownerOnlyObject.activeSelf)
                {
                    ownerOnlyObject.SetActive(true);
                }

                if (!HasActiveRenderingCamera(ownerOnlyObject))
                {
                    ActivatePreferredCamera(ownerOnlyObject);
                }
            }
        }

        private void ApplyOwnership()
        {
            bool isLocalOwner = IsSpawned && IsClient && IsOwner;

            SetOwnerPresentationActive(isLocalOwner);

            if (!isLocalOwner)
            {
                return;
            }

            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                ActivatePreferredCamera(ownerOnlyObjects[i]);
            }
        }

        private void QueueDelayedOwnershipApply()
        {
            CancelDelayedOwnershipApply();
            delayedApplyCoroutine =
                StartCoroutine(ApplyOwnershipNextFrame());
        }

        private IEnumerator ApplyOwnershipNextFrame()
        {
            yield return null;
            delayedApplyCoroutine = null;
            ApplyOwnership();
        }

        private void CancelDelayedOwnershipApply()
        {
            if (delayedApplyCoroutine == null)
            {
                return;
            }

            StopCoroutine(delayedApplyCoroutine);
            delayedApplyCoroutine = null;
        }

        private void SetOwnerPresentationActive(bool activeForOwner)
        {
            if (originalOwnerObjectActiveSelf == null)
            {
                return;
            }

            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                GameObject ownerOnlyObject = ownerOnlyObjects[i];

                if (ownerOnlyObject != null)
                {
                    ownerOnlyObject.SetActive(
                        activeForOwner &&
                        originalOwnerObjectActiveSelf[i]);
                }
            }
        }

        private static bool HasActiveRenderingCamera(GameObject root)
        {
            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ActivatePreferredCamera(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);

            if (cameras.Length == 0)
            {
                return;
            }

            int preferredIndex = GetPreferredCameraIndex(root, cameras.Length);

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];

                if (camera == null)
                {
                    continue;
                }

                bool shouldEnable = i == preferredIndex;
                camera.gameObject.SetActive(shouldEnable);
                camera.enabled = shouldEnable;

                AudioListener listener =
                    camera.GetComponent<AudioListener>();

                if (listener != null)
                {
                    listener.enabled = shouldEnable;
                }
            }
        }

        private static int GetPreferredCameraIndex(
            GameObject root,
            int cameraCount)
        {
            MonoBehaviour[] behaviours =
                root.GetComponents<MonoBehaviour>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];

                if (behaviour == null ||
                    behaviour.GetType().FullName != CameraChangerTypeName)
                {
                    continue;
                }

                FieldInfo indexField = behaviour.GetType().GetField(
                    CurrentCameraIndexFieldName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

                if (indexField != null &&
                    indexField.GetValue(behaviour) is int index)
                {
                    return Mathf.Clamp(index, 0, cameraCount - 1);
                }
            }

            return 0;
        }

        private void CaptureOriginalState()
        {
            originalOwnerObjectActiveSelf =
                new bool[ownerOnlyObjects.Length];

            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                GameObject ownerOnlyObject = ownerOnlyObjects[i];

                originalOwnerObjectActiveSelf[i] =
                    ownerOnlyObject != null &&
                    ownerOnlyObject.activeSelf;

                if (ownerOnlyObject == null)
                {
                    continue;
                }

                Camera[] cameras =
                    ownerOnlyObject.GetComponentsInChildren<Camera>(true);

                for (int cameraIndex = 0;
                     cameraIndex < cameras.Length;
                     cameraIndex++)
                {
                    Camera camera = cameras[cameraIndex];

                    if (camera == null)
                    {
                        continue;
                    }

                    originalGameObjectActiveSelf[camera.gameObject] =
                        camera.gameObject.activeSelf;

                    originalCameraEnabled[camera] = camera.enabled;

                    AudioListener listener =
                        camera.GetComponent<AudioListener>();

                    if (listener != null)
                    {
                        originalAudioListenerEnabled[listener] =
                            listener.enabled;
                    }
                }
            }
        }

        private void RestoreOriginalState()
        {
            foreach (KeyValuePair<GameObject, bool> pair in
                     originalGameObjectActiveSelf)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            foreach (KeyValuePair<Camera, bool> pair in
                     originalCameraEnabled)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }

            foreach (KeyValuePair<AudioListener, bool> pair in
                     originalAudioListenerEnabled)
            {
                if (pair.Key != null)
                {
                    pair.Key.enabled = pair.Value;
                }
            }

            if (originalOwnerObjectActiveSelf == null)
            {
                return;
            }

            for (int i = 0; i < ownerOnlyObjects.Length; i++)
            {
                if (ownerOnlyObjects[i] != null)
                {
                    ownerOnlyObjects[i].SetActive(
                        originalOwnerObjectActiveSelf[i]);
                }
            }
        }

        private void NormalizeArrays()
        {
            if (ownerOnlyObjects == null)
            {
                ownerOnlyObjects = Array.Empty<GameObject>();
            }
        }
    }
}
