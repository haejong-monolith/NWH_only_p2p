using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkDebugUI : MonoBehaviour
{
    [Header("Default Connection")]
    [SerializeField] private string serverAddress = "127.0.0.1";
    [SerializeField] private ushort serverPort = 7777;

    private string portText = "7777";
    private string statusMessage = "Not started";

    private Rect windowRect = new Rect(20f, 20f, 340f, 330f);

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
        GUILayout.Label($"Status: {statusMessage}");

        GUI.DragWindow();
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