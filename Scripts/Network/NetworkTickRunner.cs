using System;
using Unity.Netcode;
using UnityEngine;

namespace Blindfly.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class NetworkTickRunner : MonoBehaviour
    {
        public static event Action Tick;

        private NetworkManager networkManager;
        private NetworkTickSystem subscribedTickSystem;

        private bool subscribed;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            RefreshSubscription();
        }

        private void Update()
        {
            RefreshSubscription();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void RefreshSubscription()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (networkManager == null ||
                !networkManager.IsListening)
            {
                Unsubscribe();
                return;
            }

            NetworkTickSystem currentTickSystem =
                networkManager.NetworkTickSystem;

            if (currentTickSystem == null)
            {
                Unsubscribe();
                return;
            }

            if (subscribed &&
                ReferenceEquals(
                    subscribedTickSystem,
                    currentTickSystem))
            {
                return;
            }

            // Shutdown 후 같은 NetworkManager로 재접속하면 TickSystem의
            // 수명 주기가 새로 시작된다. 기존 구독을 제거하고 현재
            // TickSystem에 다시 연결해야 로컬 입력 전송이 재개된다.
            Unsubscribe();

            subscribedTickSystem = currentTickSystem;
            subscribedTickSystem.Tick += OnNetworkTick;
            subscribed = true;

            Debug.Log(
                $"[NetworkTickRunner] Tick 구독 완료 | " +
                $"TickRate={networkManager.NetworkConfig.TickRate}",
                this);
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (subscribedTickSystem != null)
            {
                subscribedTickSystem.Tick -= OnNetworkTick;
            }

            subscribedTickSystem = null;
            subscribed = false;
        }

        private static void OnNetworkTick()
        {
            Tick?.Invoke();
        }
    }
}
