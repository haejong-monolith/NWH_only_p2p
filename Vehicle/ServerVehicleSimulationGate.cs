using System;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    /// <summary>
    /// NWH 물리 계층은 Server에서만 실행한다.
    /// Host와 순수 Client는 같은 원본 Renderer 계층을 표시하고,
    /// Dedicated Server에서는 Renderer를 숨긴다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ServerVehicleSimulationGate : NetworkBehaviour
    {
        [Header("Server-only Simulation")]

        [Tooltip("VehicleController, NwhVehicleInputConsumer 등 서버에서만 실행할 컴포넌트")]
        [SerializeField]
        private Behaviour[] serverOnlyBehaviours = Array.Empty<Behaviour>();

        [SerializeField]
        private Rigidbody[] serverOnlyRigidbodies = Array.Empty<Rigidbody>();

        [SerializeField]
        private Collider[] serverOnlyColliders = Array.Empty<Collider>();

        [Header("Rendering")]

        [Tooltip("Host와 Client가 표시하고 Dedicated Server에서는 숨길 원본 차량 Renderer")]
        [SerializeField]
        private Renderer[] simulationRenderers = Array.Empty<Renderer>();

        private bool[] behaviourEnabled;
        private bool[] rigidbodyKinematic;
        private bool[] rigidbodyDetectCollisions;
        private RigidbodyInterpolation[] rigidbodyInterpolation;
        private bool[] colliderEnabled;
        private bool[] rendererEnabled;

        private void Awake()
        {
            NormalizeArrays();
            CaptureOriginalState();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyNetworkRole();
        }

        public override void OnNetworkDespawn()
        {
            RestoreOriginalState();
            base.OnNetworkDespawn();
        }

        private void ApplyNetworkRole()
        {
            bool runSimulation = IsServer;
            bool showVehicle = IsClient;

            for (int i = 0; i < serverOnlyBehaviours.Length; i++)
            {
                Behaviour behaviour = serverOnlyBehaviours[i];

                if (CanToggleAsServerOnly(behaviour))
                {
                    behaviour.enabled =
                        runSimulation && behaviourEnabled[i];
                }
            }

            for (int i = 0; i < serverOnlyRigidbodies.Length; i++)
            {
                Rigidbody body = serverOnlyRigidbodies[i];

                if (body == null)
                {
                    continue;
                }

                if (runSimulation)
                {
                    body.isKinematic = rigidbodyKinematic[i];
                    body.detectCollisions =
                        rigidbodyDetectCollisions[i];
                    body.interpolation = rigidbodyInterpolation[i];
                }
                else
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.detectCollisions = false;
                    body.isKinematic = true;
                    body.interpolation = RigidbodyInterpolation.None;
                }
            }

            for (int i = 0; i < serverOnlyColliders.Length; i++)
            {
                Collider vehicleCollider = serverOnlyColliders[i];

                if (vehicleCollider != null)
                {
                    vehicleCollider.enabled =
                        runSimulation && colliderEnabled[i];
                }
            }

            // Host: true, 순수 Client: true, Dedicated Server: false
            for (int i = 0; i < simulationRenderers.Length; i++)
            {
                Renderer vehicleRenderer = simulationRenderers[i];

                if (vehicleRenderer != null)
                {
                    vehicleRenderer.enabled =
                        showVehicle && rendererEnabled[i];
                }
            }
        }

        private void CaptureOriginalState()
        {
            behaviourEnabled = new bool[serverOnlyBehaviours.Length];

            for (int i = 0; i < serverOnlyBehaviours.Length; i++)
            {
                Behaviour behaviour = serverOnlyBehaviours[i];

                behaviourEnabled[i] =
                    CanToggleAsServerOnly(behaviour) &&
                    behaviour.enabled;
            }

            rigidbodyKinematic =
                new bool[serverOnlyRigidbodies.Length];

            rigidbodyDetectCollisions =
                new bool[serverOnlyRigidbodies.Length];

            rigidbodyInterpolation =
                new RigidbodyInterpolation[serverOnlyRigidbodies.Length];

            for (int i = 0; i < serverOnlyRigidbodies.Length; i++)
            {
                Rigidbody body = serverOnlyRigidbodies[i];

                if (body == null)
                {
                    continue;
                }

                rigidbodyKinematic[i] = body.isKinematic;
                rigidbodyDetectCollisions[i] =
                    body.detectCollisions;
                rigidbodyInterpolation[i] = body.interpolation;
            }

            colliderEnabled = new bool[serverOnlyColliders.Length];

            for (int i = 0; i < serverOnlyColliders.Length; i++)
            {
                colliderEnabled[i] =
                    serverOnlyColliders[i] != null &&
                    serverOnlyColliders[i].enabled;
            }

            rendererEnabled = new bool[simulationRenderers.Length];

            for (int i = 0; i < simulationRenderers.Length; i++)
            {
                rendererEnabled[i] =
                    simulationRenderers[i] != null &&
                    simulationRenderers[i].enabled;
            }
        }

        private void NormalizeArrays()
        {
            if (serverOnlyBehaviours == null)
            {
                serverOnlyBehaviours = Array.Empty<Behaviour>();
            }

            if (serverOnlyRigidbodies == null)
            {
                serverOnlyRigidbodies = Array.Empty<Rigidbody>();
            }

            if (serverOnlyColliders == null)
            {
                serverOnlyColliders = Array.Empty<Collider>();
            }

            if (simulationRenderers == null)
            {
                simulationRenderers = Array.Empty<Renderer>();
            }
        }

        private void RestoreOriginalState()
        {
            if (behaviourEnabled == null)
            {
                return;
            }

            for (int i = 0; i < serverOnlyBehaviours.Length; i++)
            {
                Behaviour behaviour = serverOnlyBehaviours[i];

                if (CanToggleAsServerOnly(behaviour))
                {
                    behaviour.enabled = behaviourEnabled[i];
                }
            }

            for (int i = 0; i < serverOnlyRigidbodies.Length; i++)
            {
                Rigidbody body = serverOnlyRigidbodies[i];

                if (body == null)
                {
                    continue;
                }

                body.isKinematic = rigidbodyKinematic[i];
                body.detectCollisions =
                    rigidbodyDetectCollisions[i];
                body.interpolation = rigidbodyInterpolation[i];
            }

            for (int i = 0; i < serverOnlyColliders.Length; i++)
            {
                if (serverOnlyColliders[i] != null)
                {
                    serverOnlyColliders[i].enabled =
                        colliderEnabled[i];
                }
            }

            for (int i = 0; i < simulationRenderers.Length; i++)
            {
                if (simulationRenderers[i] != null)
                {
                    simulationRenderers[i].enabled =
                        rendererEnabled[i];
                }
            }
        }

        private bool CanToggleAsServerOnly(Behaviour behaviour)
        {
            // NetworkObject나 다른 NetworkBehaviour가 실수로 배열에 들어가도
            // 비활성화하지 않는다. 네트워크 수명 주기는 NGO가 관리해야 한다.
            return behaviour != null &&
                   behaviour != this &&
                   !(behaviour is NetworkObject) &&
                   !(behaviour is NetworkBehaviour);
        }
    }
}
