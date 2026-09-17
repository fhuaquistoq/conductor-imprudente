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
        public bool Enabled = true;
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
        FootState red, green;
        bool redValid, greenValid;

        public bool TrackingReceived => hasPacket && Time.unscaledTime - lastPacketTime < FootProtocol.StaleSeconds;

        void OnEnable() => Open();
        void OnDisable() => Close();
        void OnDestroy() => Close();

        public void Open()
        {
            if (running || !Enabled) return;
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
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
                    if (FootProtocol.TryParse(Encoding.UTF8.GetString(datagram), out var packet)) queue.Enqueue(packet);
                    if (queue.Count > 128) while (queue.TryDequeue(out _)) { }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { break; }
                catch (Exception error) { Debug.LogWarning("Paquete de pies descartado: " + error.Message); }
            }
        }

        void Update()
        {
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
