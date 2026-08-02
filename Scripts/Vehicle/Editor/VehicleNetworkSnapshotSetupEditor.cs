using System;
using System.Collections.Generic;
using System.Linq;
using NWH.VehiclePhysics2.Modules.Rigging;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Blindfly.Networking.Editor
{
    /// <summary>
    /// 완성된 NWH 차량 Prefab에 단일 비주얼 계층 방식의
    /// Snapshot 동기화 컴포넌트와 참조를 자동 설정한다.
    /// PresentationRoot 또는 표시 복제본은 만들지 않는다.
    /// </summary>
    public static class VehicleNetworkSnapshotSetupEditor
    {
        private static readonly string[] WheelNames =
        {
            "TireFL_WheelController",
            "TireFR_WheelController",
            "TireRL_WheelController",
            "TireRR_WheelController"
        };

        // Client에서 반드시 꺼야 하는 실제 물리/입력 컴포넌트만 수집한다.
        // Camera, Light, Canvas, Audio, NetworkObject 등 표시/네트워크
        // 컴포넌트는 이 목록에 들어가면 안 된다.
        private static readonly HashSet<string>
            ServerSimulationBehaviourTypeNames = new HashSet<string>
            {
                "NWH.VehiclePhysics2.VehicleController",
                "NWH.WheelController3D.WheelController",
                "NWH.VehiclePhysics2.Damage.DamageHandler",
                "Monolith.VehicleInput.NwhVehicleInputConsumer"
            };

        [MenuItem(
            "GameObject/Blindfly/Vehicle/Validate Network Snapshot Setup",
            false,
            20)]
        private static void ValidateFromMenu()
        {
            if (!TryGetPrefabVehicleRoot(out Transform vehicleRoot, true))
            {
                return;
            }

            ValidationResult result = ValidateSource(vehicleRoot);
            LogValidation(vehicleRoot, result);
        }

        [MenuItem(
            "GameObject/Blindfly/Vehicle/Configure Network Snapshot",
            false,
            21)]
        private static void ConfigureFromMenu()
        {
            if (!TryGetPrefabVehicleRoot(out Transform vehicleRoot, true))
            {
                return;
            }

            Configure(vehicleRoot);
        }

        [MenuItem(
            "GameObject/Blindfly/Vehicle/Validate Network Snapshot Setup",
            true)]
        [MenuItem(
            "GameObject/Blindfly/Vehicle/Configure Network Snapshot",
            true)]
        private static bool ValidateMenuItem()
        {
            return PrefabStageUtility.GetCurrentPrefabStage() != null;
        }

        private static void Configure(Transform vehicleRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Vehicle Network Snapshot Setup",
                    "Play Mode에서는 설정할 수 없습니다.",
                    "확인");

                return;
            }

            ValidationResult validation = ValidateSource(vehicleRoot);

            if (!validation.IsValid)
            {
                LogValidation(vehicleRoot, validation);

                EditorUtility.DisplayDialog(
                    "Vehicle Network Snapshot Setup",
                    "설정을 중단했습니다. Console의 오류를 먼저 해결하세요.",
                    "확인");

                return;
            }

            VehicleSnapshotSynchronizer synchronizer =
                vehicleRoot.GetComponent<VehicleSnapshotSynchronizer>();

            if (synchronizer == null)
            {
                synchronizer = Undo.AddComponent<VehicleSnapshotSynchronizer>(
                    vehicleRoot.gameObject);
            }

            ServerVehicleSimulationGate gate =
                vehicleRoot.GetComponent<ServerVehicleSimulationGate>();

            if (gate == null)
            {
                gate = Undo.AddComponent<ServerVehicleSimulationGate>(
                    vehicleRoot.gameObject);
            }

            OwnerVehiclePresentationGate ownerPresentationGate =
                vehicleRoot.GetComponent<OwnerVehiclePresentationGate>();

            if (ownerPresentationGate == null)
            {
                ownerPresentationGate =
                    Undo.AddComponent<OwnerVehiclePresentationGate>(
                        vehicleRoot.gameObject);
            }

            ConfigureSynchronizer(
                vehicleRoot,
                validation.WheelRotatingTransforms,
                synchronizer);

            ConfigureGate(vehicleRoot, synchronizer, gate);
            ConfigureOwnerPresentationGate(
                validation.OwnerPresentationRoot,
                ownerPresentationGate);

            EditorUtility.SetDirty(synchronizer);
            EditorUtility.SetDirty(gate);
            EditorUtility.SetDirty(ownerPresentationGate);

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage != null)
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
            }

            Debug.Log(
                $"[VehicleNetworkSetup] 설정 완료 | " +
                $"Vehicle={vehicleRoot.name} | " +
                $"Wheels={validation.WheelRotatingTransforms.Count} | " +
                $"OwnerCameraRoot=" +
                $"{validation.OwnerPresentationRoot.name} | " +
                $"PresentationRoot=없음 | HostDelay=없음",
                vehicleRoot);

            Selection.activeGameObject = vehicleRoot.gameObject;
        }

        private static void ConfigureSynchronizer(
            Transform vehicleRoot,
            IList<Transform> wheelRotatingTransforms,
            VehicleSnapshotSynchronizer synchronizer)
        {
            Undo.RecordObject(
                synchronizer,
                "Configure Vehicle Snapshot Synchronizer");

            SerializedObject serialized = new SerializedObject(synchronizer);

            serialized.FindProperty("simulationRoot").objectReferenceValue =
                vehicleRoot;

            serialized.FindProperty("simulationRigidbody").objectReferenceValue =
                vehicleRoot.GetComponent<Rigidbody>();

            SetObjectArray(
                serialized.FindProperty("simulationVisualParts"),
                wheelRotatingTransforms
                    .Cast<UnityEngine.Object>()
                    .ToArray());

            SetObjectArray(
                serialized.FindProperty("simulationRiggingModules"),
                vehicleRoot
                    .GetComponentsInChildren<RiggingModuleWrapper>(true)
                    .Cast<UnityEngine.Object>()
                    .ToArray());

            serialized.ApplyModifiedProperties();
        }

        private static void ConfigureGate(
            Transform vehicleRoot,
            VehicleSnapshotSynchronizer synchronizer,
            ServerVehicleSimulationGate gate)
        {
            Undo.RecordObject(gate, "Configure Server Simulation Gate");

            Behaviour[] allBehaviours =
                vehicleRoot.GetComponentsInChildren<Behaviour>(true);

            Behaviour[] serverOnlyBehaviours = allBehaviours
                .Where(IsServerSimulationBehaviour)
                .ToArray();

            Rigidbody[] rigidbodies =
                vehicleRoot.GetComponentsInChildren<Rigidbody>(true);

            Collider[] colliders =
                vehicleRoot.GetComponentsInChildren<Collider>(true);

            Renderer[] renderers =
                vehicleRoot.GetComponentsInChildren<Renderer>(true);

            SerializedObject serialized = new SerializedObject(gate);

            SetObjectArray(
                serialized.FindProperty("serverOnlyBehaviours"),
                serverOnlyBehaviours
                    .Cast<UnityEngine.Object>()
                    .ToArray());

            SetObjectArray(
                serialized.FindProperty("serverOnlyRigidbodies"),
                rigidbodies.Cast<UnityEngine.Object>().ToArray());

            SetObjectArray(
                serialized.FindProperty("serverOnlyColliders"),
                colliders.Cast<UnityEngine.Object>().ToArray());

            SetObjectArray(
                serialized.FindProperty("simulationRenderers"),
                renderers.Cast<UnityEngine.Object>().ToArray());

            serialized.ApplyModifiedProperties();

            Debug.Log(
                $"[VehicleNetworkSetup] Server Only Behaviours " +
                $"{serverOnlyBehaviours.Length}개 설정 | " +
                $"Client/표시 Behaviour " +
                $"{allBehaviours.Length - serverOnlyBehaviours.Length}개 유지",
                gate);
        }

        private static void ConfigureOwnerPresentationGate(
            Transform ownerPresentationRoot,
            OwnerVehiclePresentationGate gate)
        {
            Undo.RecordObject(gate, "Configure Owner Presentation Gate");

            SerializedObject serialized = new SerializedObject(gate);

            SetObjectArray(
                serialized.FindProperty("ownerOnlyObjects"),
                new UnityEngine.Object[]
                {
                    ownerPresentationRoot.gameObject
                });

            serialized.ApplyModifiedProperties();
        }

        private static bool IsServerSimulationBehaviour(
            Behaviour behaviour)
        {
            if (behaviour == null ||
                behaviour is NetworkObject ||
                behaviour is NetworkBehaviour ||
                behaviour is Renderer)
            {
                return false;
            }

            // 파생 클래스를 사용하는 차량도 기본 NWH 타입을 기준으로 찾는다.
            for (Type type = behaviour.GetType();
                 type != null;
                 type = type.BaseType)
            {
                if (ServerSimulationBehaviourTypeNames.Contains(type.FullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static ValidationResult ValidateSource(Transform vehicleRoot)
        {
            ValidationResult result = new ValidationResult();

            if (vehicleRoot.GetComponent<NetworkObject>() == null)
            {
                result.Errors.Add("차량 루트에 NetworkObject가 없습니다.");
            }

            if (vehicleRoot.GetComponent<Rigidbody>() == null)
            {
                result.Errors.Add("차량 루트에 Rigidbody가 없습니다.");
            }

            Transform presentationRoot = FindDirectChild(
                vehicleRoot,
                "PresentationRoot");

            if (presentationRoot != null)
            {
                result.Errors.Add(
                    "기존 PresentationRoot가 있습니다. 새 구조에서는 사용하지 " +
                    "않으므로 내용을 확인한 뒤 삭제하세요.");
            }

            Transform camerasRoot = FindDirectChild(vehicleRoot, "Cameras");

            if (camerasRoot == null)
            {
                result.Errors.Add(
                    "차량 루트 바로 아래의 Cameras 오브젝트를 찾지 " +
                    "못했습니다.");
            }
            else if (camerasRoot.GetComponentsInChildren<Camera>(true)
                         .Length == 0)
            {
                result.Errors.Add(
                    "Cameras 계층 아래에 Camera 컴포넌트가 없습니다.");
            }
            else
            {
                result.OwnerPresentationRoot = camerasRoot;
            }

            for (int i = 0; i < WheelNames.Length; i++)
            {
                string wheelName = WheelNames[i];

                List<Transform> matchingWheels =
                    FindAllByName(vehicleRoot, wheelName);

                if (matchingWheels.Count != 1)
                {
                    result.Errors.Add(
                        $"{wheelName} 오브젝트가 정확히 1개여야 합니다. " +
                        $"현재 {matchingWheels.Count}개입니다.");

                    continue;
                }

                Transform rotating = FindDirectChild(
                    matchingWheels[0],
                    "Rotating");

                if (rotating == null)
                {
                    result.Errors.Add(
                        $"{wheelName}/Rotating을 찾지 못했습니다.");

                    continue;
                }

                result.WheelRotatingTransforms.Add(rotating);
            }

            return result;
        }

        private static bool TryGetPrefabVehicleRoot(
            out Transform vehicleRoot,
            bool showDialog)
        {
            vehicleRoot = null;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (stage == null)
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Vehicle Network Snapshot Setup",
                        "차량 Prefab을 Prefab Mode로 여세요.",
                        "확인");
                }

                return false;
            }

            vehicleRoot = stage.prefabContentsRoot.transform;
            return vehicleRoot != null;
        }

        private static Transform FindDirectChild(
            Transform parent,
            string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static List<Transform> FindAllByName(
            Transform root,
            string targetName)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(item => item.name == targetName)
                .ToList();
        }

        private static void SetObjectArray(
            SerializedProperty property,
            UnityEngine.Object[] values)
        {
            if (property == null)
            {
                throw new InvalidOperationException(
                    "필요한 SerializedProperty를 찾지 못했습니다. " +
                    "런타임 스크립트 버전을 확인하세요.");
            }

            property.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue =
                    values[i];
            }
        }

        private static void LogValidation(
            Transform vehicleRoot,
            ValidationResult result)
        {
            if (result.IsValid)
            {
                Debug.Log(
                    $"[VehicleNetworkSetup] 검증 통과 | " +
                    $"Vehicle={vehicleRoot.name} | " +
                    $"Wheels={result.WheelRotatingTransforms.Count}",
                    vehicleRoot);

                return;
            }

            for (int i = 0; i < result.Errors.Count; i++)
            {
                Debug.LogError(
                    $"[VehicleNetworkSetup] {result.Errors[i]}",
                    vehicleRoot);
            }
        }

        private sealed class ValidationResult
        {
            public readonly List<string> Errors = new List<string>();

            public readonly List<Transform> WheelRotatingTransforms =
                new List<Transform>();

            public Transform OwnerPresentationRoot;

            public bool IsValid => Errors.Count == 0;
        }
    }
}
