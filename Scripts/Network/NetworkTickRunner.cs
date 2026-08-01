using System;
using System.Collections;
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

        private Coroutine subscribeCoroutine;
        private bool subscribed;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            subscribeCoroutine = StartCoroutine(
                SubscribeWhenNetworkStarted());
        }

        private void OnDisable()
        {
            StopSubscribeCoroutine();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            StopSubscribeCoroutine();
            Unsubscribe();
        }

        private IEnumerator SubscribeWhenNetworkStarted()
        {
            while (networkManager != null &&
                   !networkManager.IsListening)
            {
                yield return null;
            }

            if (networkManager == null)
            {
                yield break;
            }

            subscribedTickSystem =
                networkManager.NetworkTickSystem;

            if (subscribedTickSystem == null)
            {
                yield break;
            }

            subscribedTickSystem.Tick += OnNetworkTick;
            subscribed = true;
            subscribeCoroutine = null;

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

        private void StopSubscribeCoroutine()
        {
            if (subscribeCoroutine == null)
            {
                return;
            }

            StopCoroutine(subscribeCoroutine);
            subscribeCoroutine = null;
        }

        private static void OnNetworkTick()
        {
            Tick?.Invoke();
        }
    }
}