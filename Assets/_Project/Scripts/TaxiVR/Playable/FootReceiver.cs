using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TaxiVR.Playable
{
    public sealed class FootReceiver : MonoBehaviour
    {
        public int Port = FootProtocol.Port;

        /// <summary>El receptor vive siempre encendido: si no llega nada no hace nada y cuando llega, manda
        /// el. Editable en caliente, tambien durante Play; el cambio se aplica solo.</summary>
        [SerializeField] bool enabled = true;
        public bool Enabled { get => enabled; set => enabled = value; }

        public FootStateMachine Machine { get; } = new();
        public bool SocketBound { get; private set; }
        public string Failure { get; private set; }
        public string Warning => Machine.Warning ? "Coloque los pies frente a la camara" : null;

        readonly ConcurrentQueue<FootPacket> queue = new();
        UdpClient client;
        Thread worker;
        volatile bool running;
        int lastSequence = -1;
        float lastPacketTime = -100;
        bool hasPacket;
        bool applied;
        FootState red, green;
        bool redValid, greenValid;

        public bool TrackingReceived => hasPacket && Time.unscaledTime - lastPacketTime < FootProtocol.StaleSeconds;

        void OnEnable() { applied = !Enabled; Apply(); }
        void OnDisable() => Close();
        void OnDestroy() => Close();

        // Aplica en caliente el cambio de Enabled, venga del Inspector o del codigo.
        void Apply()
        {
            if (applied == Enabled) return;
            applied = Enabled;
            if (Enabled) Open(); else Close();
        }

        public void Open()
        {
            if (running || !Enabled) return;
            try
            {
                // 0.0.0.0 y no loopback: en el Quest independiente el tracker corre en un PC de la LAN.
                // El puerto no esta autenticado, asi que cualquier equipo de la red puede inyectar pedales.
                client = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
                running = true;
                SocketBound = true; Failure = null;
                worker = new Thread(Receive) { IsBackground = true, Name = "FootTracker" };
                worker.Start();
            }
            catch (SocketException error)
            {
                SocketBound = false;
                Failure = "No se pudo abrir el puerto UDP " + Port + ": " + error.SocketErrorCode;
                Debug.LogWarning(Failure + ". Se usan los controles de teclado.");
            }
        }

        public void Close()
        {
            if (!running && worker == null) return;
            running = false;
            try { client?.Close(); } catch (SocketException) { } catch (ObjectDisposedException) { }
            if (worker != null && !worker.Join(500)) Debug.LogWarning("El receptor de pies no terminó a tiempo.");
            worker = null; client = null; SocketBound = false;
        }

        void Receive()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var datagram = client.Receive(ref endpoint);
                    if (FootProtocol.TryParse(Encoding.UTF8.GetString(datagram), out var packet))
                    {
                        // Se descartan los mas viejos para dejar sitio al que llega, en vez de vaciar la cola
                        // entera (que tiraria tambien el paquete recien recibido).
                        while (queue.Count >= 128 && queue.TryDequeue(out _)) { }
                        queue.Enqueue(packet);
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { break; }
                catch (Exception error) { Debug.LogWarning("Paquete de pies descartado: " + error.Message); }
            }
        }

        void Update()
        {
            Apply();
            TakeLatest();
            if (TrackingReceived) Machine.Tick(red, green, redValid, greenValid, Time.deltaTime);
            else Machine.Tick(FootState.Unknown, FootState.Unknown, false, false, Time.deltaTime);
        }

        void TakeLatest()
        {
            FootPacket newest = default;
            bool any = false;
            while (queue.TryDequeue(out var packet))
            {
                if (any && !FootProtocol.IsNewer(newest.Sequence, packet.Sequence)) continue;
                newest = packet; any = true;
            }
            if (!any || hasPacket && !FootProtocol.IsNewer(lastSequence, newest.Sequence)) return;
            lastSequence = newest.Sequence;
            lastPacketTime = Time.unscaledTime;
            hasPacket = true;
            red = newest.Red; green = newest.Green;
            redValid = newest.RedValid; greenValid = newest.GreenValid;
        }
    }
}
