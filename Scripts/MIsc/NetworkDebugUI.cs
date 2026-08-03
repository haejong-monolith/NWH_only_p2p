using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Blindfly.Networking;

public class NetworkDebugUI : MonoBehaviour
{
    [Header("Default Connection")]
    [SerializeField] private string serverAddress = "127.0.0.1";
    [SerializeField] private ushort serverPort = 7777;

    private string portText = "7777";
    private string statusMessage = "Not started";

    private Rect windowRect = new Rect(20f, 20f, 380f, 560f);
    private Vector2 scrollPosition;
    private VehicleSnapshotSynchronizer[] snapshotSynchronizers =
        new VehicleSnapshotSynchronizer[0];
    private float nextDiagnosticsRefreshTime;

    private void Awake()
    {
        portText = serverPort.ToString();
    }

    private void OnGUI()
    {
        windowRect = GUI.Window(
            GetInstanceID(),
            windowRect,
            DrawWindow,
            "Network Debug"
        );
    }

    private void DrawWindow(int windowId)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.Space(5f);

        DrawCurrentStatus();

        GUILayout.Space(10f);

        GUILayout.Label("Server Address");
        serverAddress = GUILayout.TextField(serverAddress);

        GUILayout.Label("Port");
        portText = GUILayout.TextField(portText);

        GUILayout.Space(10f);

        GUI.enabled = CanStartNetwork();

        if (GUILayout.Button("Start Host", GUILayout.Height(35f)))
        {
            StartHost();
        }

        if (GUILayout.Button("Start Server", GUILayout.Height(35f)))
        {
            StartServer();
        }

        if (GUILayout.Button("Connect To Server", GUILayout.Height(35f)))
        {
            StartClient();
        }

        GUI.enabled = CanShutdownNetwork();

        if (GUILayout.Button("Shutdown", GUILayout.Height(35f)))
        {
            Shutdown();
        }

        GUI.enabled = true;

        GUILayout.Space(10f);
        GUILayout.Label($"Status: {GetCurrentStatusMessage()}");

        DrawNetworkDiagnostics();

        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    private void DrawNetworkDiagnostics()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        GUILayout.Space(12f);
        GUILayout.Label("Diagnostics");
        GUILayout.Label($"Tick Rate: {networkManager.NetworkConfig.TickRate} Hz");

        UnityTransport transport =
            networkManager.GetComponent<UnityTransport>();

        if (transport != null && networkManager.IsClient)
        {
            ulong rtt = transport.GetCurrentRtt(
                NetworkManager.ServerClientId);

            GUILayout.Label($"RTT: {rtt} ms");
        }

        RefreshSnapshotSynchronizers();

        int remoteVehicleCount = 0;

        for (int i = 0; i < snapshotSynchronizers.Length; i++)
        {
            VehicleSnapshotSynchronizer synchronizer =
                snapshotSynchronizers[i];

            if (synchronizer == null ||
                !synchronizer.IsInterpolatingRemoteClient)
            {
                continue;
            }

            remoteVehicleCount++;
            GUILayout.Space(8f);
            GUILayout.Label($"Remote Vehicle: {synchronizer.name}");
            GUILayout.Label(
                $"Buffer: {synchronizer.BufferedSnapshotCount}/" +
                $"{synchronizer.SnapshotBufferCapacity} | " +
                $"Span: " +
                $"{synchronizer.BufferedSnapshotTimeSpan * 1000f:F0} ms");

            string playbackState = synchronizer.IsBufferOverrun
                ? "Overrun"
                : synchronizer.IsBufferUnderrun
                    ? "Underrun"
                    : "Normal";

            GUILayout.Label(
                $"Playback: {playbackState} | " +
                $"Delay: {synchronizer.InterpolationDelay * 1000f:F0} ms | " +
                $"Error: {synchronizer.PlaybackTimeError * 1000f:+0;-0;0} ms");

            float sinceLast = synchronizer.TimeSinceLastSnapshot;
            string sinceLastText = sinceLast < 0f
                ? "waiting"
                : $"{sinceLast * 1000f:F0} ms";

            GUILayout.Label(
                $"Receive Interval: " +
                $"{synchronizer.SmoothedSnapshotInterval * 1000f:F1} ms | " +
                $"Last: {sinceLastText}");

            GUILayout.Label(
                $"Snapshots: {synchronizer.AcceptedSnapshotCount}/" +
                $"{synchronizer.ReceivedSnapshotCount} accepted | " +
                $"Rejected: {synchronizer.RejectedSnapshotCount}");

            GUILayout.Label(
                $"Latest Tick: {synchronizer.LatestReceivedTick} | " +
                $"Tick Gaps: {synchronizer.MissingTickCount}");

            GUILayout.Label(
                $"Buffer Events: U {synchronizer.BufferUnderrunCount} | " +
                $"O {synchronizer.BufferOverrunCount} | " +
                $"Resync {synchronizer.PlaybackResyncCount}");
        }

        if (networkManager.IsClient && !networkManager.IsServer &&
            remoteVehicleCount == 0)
        {
            GUILayout.Label("Remote Vehicle: waiting");
        }
    }

    private void RefreshSnapshotSynchronizers()
    {
        if (Time.unscaledTime < nextDiagnosticsRefreshTime)
        {
            return;
        }

        snapshotSynchronizers =
            FindObjectsOfType<VehicleSnapshotSynchronizer>();

        nextDiagnosticsRefreshTime = Time.unscaledTime + 0.5f;
    }

    private void DrawCurrentStatus()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            GUILayout.Label("NetworkManager: Missing");
            return;
        }

        if (!networkManager.IsListening)
        {
            GUILayout.Label("Mode: Offline");
            return;
        }

        if (networkManager.IsHost)
        {
            GUILayout.Label("Mode: Host");
        }
        else if (networkManager.IsServer)
        {
            GUILayout.Label("Mode: Server");
        }
        else if (networkManager.IsClient)
        {
            GUILayout.Label("Mode: Client");
        }

        GUILayout.Label(
            $"Local Client ID: {networkManager.LocalClientId}"
        );

        if (networkManager.IsServer)
        {
            GUILayout.Label(
                $"Connected Clients: {networkManager.ConnectedClientsIds.Count}"
            );
        }
    }

    private string GetCurrentStatusMessage()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            return "NetworkManager not found";
        }

        if (networkManager.IsHost && networkManager.IsListening)
        {
            return $"Host started on port {serverPort}";
        }

        if (networkManager.IsServer && networkManager.IsListening)
        {
            return $"Server started on port {serverPort}";
        }

        if (networkManager.IsConnectedClient)
        {
            return $"Connected to {serverAddress.Trim()}:{serverPort}";
        }

        if (networkManager.IsClient && networkManager.IsListening)
        {
            return $"Connecting to {serverAddress.Trim()}:{serverPort}";
        }

        return statusMessage;
    }

    private bool CanStartNetwork()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        return networkManager != null &&
               !networkManager.IsListening;
    }

    private bool CanShutdownNetwork()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        return networkManager != null &&
               networkManager.IsListening;
    }

    private bool TryApplyConnectionSettings()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            statusMessage = "NetworkManager not found";
            Debug.LogError("[NetworkDebugUI] NetworkManager.Singleton is null.");
            return false;
        }

        UnityTransport transport =
            networkManager.GetComponent<UnityTransport>();

        if (transport == null)
        {
            statusMessage = "UnityTransport not found";
            Debug.LogError(
                "[NetworkDebugUI] UnityTransport is missing on NetworkManager."
            );
            return false;
        }

        if (!ushort.TryParse(portText, out ushort parsedPort))
        {
            statusMessage = "Invalid port";
            Debug.LogWarning(
                $"[NetworkDebugUI] Invalid port: {portText}"
            );
            return false;
        }

        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            statusMessage = "Invalid server address";
            Debug.LogWarning(
                "[NetworkDebugUI] Server address is empty."
            );
            return false;
        }

        serverPort = parsedPort;

        transport.SetConnectionData(
            serverAddress.Trim(),
            serverPort
        );

        return true;
    }

    private void StartHost()
    {
        if (!TryApplyConnectionSettings())
        {
            return;
        }

        bool started = NetworkManager.Singleton.StartHost();

        statusMessage = started
            ? $"Host started on port {serverPort}"
            : "Failed to start host";

        Debug.Log(
            $"[NetworkDebugUI] StartHost result={started}, " +
            $"port={serverPort}"
        );
    }

    private void StartServer()
    {
        if (!TryApplyConnectionSettings())
        {
            return;
        }

        bool started = NetworkManager.Singleton.StartServer();

        statusMessage = started
            ? $"Server started on port {serverPort}"
            : "Failed to start server";

        Debug.Log(
            $"[NetworkDebugUI] StartServer result={started}, " +
            $"port={serverPort}"
        );
    }

    private void StartClient()
    {
        if (!TryApplyConnectionSettings())
        {
            return;
        }

        bool started = NetworkManager.Singleton.StartClient();

        statusMessage = started
            ? $"Connecting to {serverAddress}:{serverPort}"
            : "Failed to start client";

        Debug.Log(
            $"[NetworkDebugUI] StartClient result={started}, " +
            $"address={serverAddress}, port={serverPort}"
        );
    }

    private void Shutdown()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            return;
        }

        networkManager.Shutdown();

        statusMessage = "Network shutdown";

        Debug.Log("[NetworkDebugUI] Network shutdown.");
    }
}
