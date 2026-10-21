using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace UA
{
    public enum Msg : byte
    {
        Join = 1,
        Welcome = 2,
        Lobby = 3,
        Snapshot = 4,
        Input = 5,
        Leave = 6,
        Kicked = 7,
        Query = 20,
        Announce = 21,
    }

    /// <summary>Non-blocking UDP socket polled from the main thread.</summary>
    public sealed class NetSocket : IDisposable
    {
        private Socket socket;
        private readonly byte[] receiveBuffer = new byte[16384];

        public NetSocket(int port, bool reuseAddress)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            if (reuseAddress)
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Blocking = false;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public int Port => ((IPEndPoint)socket.LocalEndPoint).Port;

        public void Send(byte[] data, int length, IPEndPoint to)
        {
            try { socket?.SendTo(data, 0, length, SocketFlags.None, to); }
            catch (Exception) { /* unreachable network, broadcast not allowed, ... */ }
        }

        public bool Receive(out BinaryReader reader, out IPEndPoint from)
        {
            reader = null;
            from = null;
            try
            {
                if (socket == null || socket.Available <= 0) return false;
                EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                int length = socket.ReceiveFrom(receiveBuffer, ref endPoint);
                from = (IPEndPoint)endPoint;
                reader = new BinaryReader(new MemoryStream(receiveBuffer, 0, length, false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            try { socket?.Close(); } catch (Exception) { }
            socket = null;
        }
    }

    public sealed class PacketWriter
    {
        private readonly MemoryStream stream = new MemoryStream(8192);
        public readonly BinaryWriter W;

        public PacketWriter() { W = new BinaryWriter(stream); }

        public BinaryWriter Begin(Msg type)
        {
            stream.Position = 0;
            stream.SetLength(0);
            W.Write((byte)type);
            return W;
        }

        public byte[] Buffer => stream.GetBuffer();
        public int Length => (int)stream.Length;
    }

    public static class NetIO
    {
        public static void Write(this BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 ReadVector3(this BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    /// <summary>A group found on the local network.</summary>
    public class GroupInfo
    {
        public string Name = "";
        public string HostName = "";
        public int Port;
        public Mode Mode;
        public byte Map;
        public byte Players;
        public byte MaxPlayers;
        public Phase Phase;
        public IPEndPoint Address;
        public float LastSeen;

        public void Write(BinaryWriter w)
        {
            w.Write(Name); w.Write(HostName); w.Write(Port); w.Write((byte)Mode); w.Write(Map);
            w.Write(Players); w.Write(MaxPlayers); w.Write((byte)Phase);
        }

        public static GroupInfo Read(BinaryReader r) => new GroupInfo
        {
            Name = r.ReadString(), HostName = r.ReadString(), Port = r.ReadInt32(), Mode = (Mode)r.ReadByte(), Map = r.ReadByte(),
            Players = r.ReadByte(), MaxPlayers = r.ReadByte(), Phase = (Phase)r.ReadByte(),
        };
    }

    public static class LanUtil
    {
        /// <summary>
        /// Where to send discovery packets: the global broadcast, the directed broadcast of every local
        /// interface (Wi-Fi client or the phone's own hotspot), the classic Android hotspot subnet and loopback.
        /// </summary>
        public static List<IPEndPoint> BroadcastTargets(int port)
        {
            var list = new List<IPEndPoint>
            {
                new IPEndPoint(IPAddress.Broadcast, port),
                new IPEndPoint(IPAddress.Loopback, port),
                new IPEndPoint(IPAddress.Parse("192.168.43.255"), port),
            };
            foreach (var (address, mask) in Interfaces())
            {
                var a = address.GetAddressBytes();
                var m = mask.GetAddressBytes();
                var b = new byte[4];
                for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                var endPoint = new IPEndPoint(new IPAddress(b), port);
                if (!list.Exists(e => e.Equals(endPoint))) list.Add(endPoint);
            }
            return list;
        }

        public static List<string> LocalAddresses()
        {
            var result = new List<string>();
            foreach (var (address, _) in Interfaces())
                if (!result.Contains(address.ToString())) result.Add(address.ToString());
            if (result.Count == 0)
            {
                try
                {
                    foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                            result.Add(address.ToString());
                }
                catch (Exception) { }
            }
            return result;
        }

        private static List<(IPAddress, IPAddress)> Interfaces()
        {
            var list = new List<(IPAddress, IPAddress)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        if (ni.OperationalStatus == OperationalStatus.Down || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                            IPAddress mask = null;
                            try { mask = ua.IPv4Mask; } catch (Exception) { }
                            if (mask == null || mask.Equals(IPAddress.Any)) mask = IPAddress.Parse("255.255.255.0");
                            list.Add((ua.Address, mask));
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
