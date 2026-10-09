using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Estructura del JSON que manda servidor.py. Los nombres de campo deben
/// coincidir exactamente (JsonUtility los mapea por nombre).
/// </summary>
[Serializable]
public class ComandoCamilla
{
    public string tipo;
    public float valor;
    public bool relativo;
    public bool esNatural;
}

/// <summary>
/// Se conecta al puente WebSocket del backend de Python (servidor.py) y
/// reenvía cada comando recibido al CamillaController.
/// </summary>
public class UnityWebSocketClient : MonoBehaviour
{
    [Header("Conexión")]
    [Tooltip("IP de la máquina donde corre el backend de Python. 'localhost' si es la misma máquina que corre Unity.")]
    public string host = "localhost";
    public int puerto = 8000;

    [Header("Referencia")]
    public CamillaController camilla;

    ClientWebSocket socket;
    CancellationTokenSource cts;
    readonly ConcurrentQueue<string> mensajesRecibidos = new ConcurrentQueue<string>();

    void Start()
    {
        cts = new CancellationTokenSource();
        _ = ConectarConReintentos();
    }

    async Task ConectarConReintentos()
    {
        string url = $"ws://{host}:{puerto}/ws";

        while (!cts.IsCancellationRequested)
        {
            socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(url), cts.Token);
                Debug.Log($"Conectado al backend de voz en {url}");
                await EscucharMensajes();
                Debug.LogWarning("Se perdió la conexión con el backend de voz.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"No se pudo conectar a {url} ({e.Message}). Reintentando en 2s...");
            }

            if (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(2000, cts.Token); }
                catch (TaskCanceledException) { }
            }
        }
    }

    async Task EscucharMensajes()
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            try
            {
                var resultado = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (resultado.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                    break;
                }
                string mensaje = Encoding.UTF8.GetString(buffer, 0, resultado.Count);
                mensajesRecibidos.Enqueue(mensaje);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Conexión con el backend interrumpida: {e.Message}");
                break;
            }
        }
    }

    void Update()
    {
        // ClientWebSocket recibe en un hilo aparte; mover transforms de Unity
        // solo es válido desde el hilo principal, por eso los mensajes pasan
        // por esta cola y se procesan acá, en Update().
        while (mensajesRecibidos.TryDequeue(out string mensaje))
        {
            ProcesarMensaje(mensaje);
        }
    }

    void ProcesarMensaje(string json)
    {
        ComandoCamilla comando;
        try
        {
            comando = JsonUtility.FromJson<ComandoCamilla>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"JSON inválido recibido: {json} ({e.Message})");
            return;
        }

        if (camilla == null)
        {
            Debug.LogWarning("No hay CamillaController asignado en UnityWebSocketClient");
            return;
        }

        camilla.EjecutarComando(comando.tipo, comando.valor, comando.relativo, comando.esNatural);
    }

    async void OnDestroy()
    {
        cts?.Cancel();
        if (socket != null && socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "cerrando", CancellationToken.None);
        }
    }
}
