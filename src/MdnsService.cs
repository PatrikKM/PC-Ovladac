using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PCOvladac
{
    public class MdnsService : IDisposable
    {
        private const string MulticastAddress = "224.0.0.251";
        private const int MdnsPort = 5353;
        private const string InstanceName = "PC-Ovladac";
        private const string HostName = "PC-Ovladac.local";
        private const string HttpServiceType = "_http._tcp.local";
        private const string CustomServiceType = "_pcovladac._tcp.local";
        private const string DnsSdMetaType = "_services._dns-sd._udp.local";

        private readonly List<UdpClient> _listeners = new List<UdpClient>();
        private readonly List<Thread> _threads = new List<Thread>();
        private volatile bool _running;
        private int _port;

        public bool IsRunning { get { return _running; } }

        public void Start(int port)
        {
            Stop();
            _port = port;
            _running = true;

            IPAddress multicastIp = IPAddress.Parse(MulticastAddress);

            try
            {
                var allIfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in allIfaces)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPAddress ipv4 = null;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        {
                            ipv4 = ua.Address;
                            break;
                        }
                    }

                    if (ipv4 == null) continue;

                    try
                    {
                        var client = new UdpClient(AddressFamily.InterNetwork);
                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        client.ExclusiveAddressUse = false;
                        client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
                        client.JoinMulticastGroup(multicastIp, ipv4);
                        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);

                        _listeners.Add(client);

                        var localClient = client;
                        var localIp = ipv4;
                        var t = new Thread(() => ListenLoop(localClient, localIp))
                        {
                            IsBackground = true,
                            Name = "mDNS-" + ipv4
                        };
                        _threads.Add(t);
                        t.Start();
                    }
                    catch
                    {
                        // Rozhraní nemusí podporovat multicast
                    }
                }
            }
            catch
            {
                // Ignorujeme chyby při inicializaci síťových rozhraní
            }

            // Oznámení služby při startu
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Announce(120);
                Thread.Sleep(600);
                if (_running) Announce(120);
            });
        }

        public void Stop()
        {
            if (_running)
            {
                try { Announce(0); } catch { }
            }
            _running = false;

            foreach (var client in _listeners)
            {
                try { client.Close(); } catch { }
            }
            _listeners.Clear();
            _threads.Clear();
        }

        private void ListenLoop(UdpClient client, IPAddress interfaceIp)
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = client.Receive(ref remoteEp);
                    if (data == null || data.Length < 12) continue;
                    HandlePacket(data, client, interfaceIp);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandlePacket(byte[] data, UdpClient client, IPAddress interfaceIp)
        {
            ushort flags = (ushort)((data[2] << 8) | data[3]);
            bool isResponse = (flags & 0x8000) != 0;
            if (isResponse) return;

            ushort qdCount = (ushort)((data[4] << 8) | data[5]);
            if (qdCount == 0) return;

            int offset = 12;
            bool matchHttp = false;
            bool matchCustom = false;
            bool matchMeta = false;
            bool matchHost = false;

            for (int i = 0; i < qdCount && offset < data.Length; i++)
            {
                string qname = ReadDnsName(data, ref offset);
                if (offset + 4 > data.Length) break;
                offset += 4; // QTYPE + QCLASS

                string qLower = qname.TrimEnd('.').ToLowerInvariant();
                if (qLower == DnsSdMetaType) matchMeta = true;
                else if (qLower == HttpServiceType || qLower == (InstanceName + "." + HttpServiceType).ToLowerInvariant()) matchHttp = true;
                else if (qLower == CustomServiceType || qLower == (InstanceName + "." + CustomServiceType).ToLowerInvariant()) matchCustom = true;
                else if (qLower == HostName.ToLowerInvariant()) matchHost = true;
            }

            if (matchMeta || matchHttp || matchCustom || matchHost)
            {
                byte[] response = BuildAnnouncementPacket(interfaceIp, 120);
                var ep = new IPEndPoint(IPAddress.Parse(MulticastAddress), MdnsPort);
                try
                {
                    client.Send(response, response.Length, ep);
                }
                catch { }
            }
        }

        private void Announce(uint ttl)
        {
            var ep = new IPEndPoint(IPAddress.Parse(MulticastAddress), MdnsPort);
            IPAddress primaryIp;
            if (!IPAddress.TryParse(NetworkHelper.GetPrimaryLocalIp(), out primaryIp))
            {
                primaryIp = IPAddress.Loopback;
            }

            byte[] packet = BuildAnnouncementPacket(primaryIp, ttl);
            foreach (var client in _listeners)
            {
                try
                {
                    client.Send(packet, packet.Length, ep);
                }
                catch { }
            }
        }

        private byte[] BuildAnnouncementPacket(IPAddress ip, uint ttl)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // Header: ID=0, Flags=0x8400 (Response + Authoritative), QD=0, AN=8, NS=0, AR=0
                WriteUInt16(bw, 0);
                WriteUInt16(bw, 0x8400);
                WriteUInt16(bw, 0);
                WriteUInt16(bw, 8);
                WriteUInt16(bw, 0);
                WriteUInt16(bw, 0);

                string httpInstance = InstanceName + "." + HttpServiceType;
                string customInstance = InstanceName + "." + CustomServiceType;

                // 1. PTR _services._dns-sd._udp.local -> _http._tcp.local
                WritePtrRecord(bw, DnsSdMetaType, HttpServiceType, ttl);
                // 2. PTR _services._dns-sd._udp.local -> _pcovladac._tcp.local
                WritePtrRecord(bw, DnsSdMetaType, CustomServiceType, ttl);
                // 3. PTR _http._tcp.local -> PC-Ovladac._http._tcp.local
                WritePtrRecord(bw, HttpServiceType, httpInstance, ttl);
                // 4. PTR _pcovladac._tcp.local -> PC-Ovladac._pcovladac._tcp.local
                WritePtrRecord(bw, CustomServiceType, customInstance, ttl);
                // 5. SRV PC-Ovladac._http._tcp.local
                WriteSrvRecord(bw, httpInstance, HostName, (ushort)_port, ttl);
                // 6. SRV PC-Ovladac._pcovladac._tcp.local
                WriteSrvRecord(bw, customInstance, HostName, (ushort)_port, ttl);
                // 7. TXT PC-Ovladac._http._tcp.local
                WriteTxtRecord(bw, httpInstance, new[] { "app=PC-Ovladac", "ver=1" }, ttl);
                // 8. A PC-Ovladac.local
                WriteARecord(bw, HostName, ip, ttl);

                return ms.ToArray();
            }
        }

        private static void WritePtrRecord(BinaryWriter bw, string name, string target, uint ttl)
        {
            WriteDnsName(bw, name);
            WriteUInt16(bw, 12); // PTR
            WriteUInt16(bw, 1);  // IN
            WriteUInt32(bw, ttl);
            byte[] rdata = EncodeDnsName(target);
            WriteUInt16(bw, (ushort)rdata.Length);
            bw.Write(rdata);
        }

        private static void WriteSrvRecord(BinaryWriter bw, string name, string targetHost, ushort port, uint ttl)
        {
            WriteDnsName(bw, name);
            WriteUInt16(bw, 33);     // SRV
            WriteUInt16(bw, 0x8001); // IN + Cache Flush
            WriteUInt32(bw, ttl);
            byte[] targetBytes = EncodeDnsName(targetHost);
            WriteUInt16(bw, (ushort)(6 + targetBytes.Length));
            WriteUInt16(bw, 0); // Priority
            WriteUInt16(bw, 0); // Weight
            WriteUInt16(bw, port);
            bw.Write(targetBytes);
        }

        private static void WriteTxtRecord(BinaryWriter bw, string name, string[] entries, uint ttl)
        {
            WriteDnsName(bw, name);
            WriteUInt16(bw, 16);     // TXT
            WriteUInt16(bw, 0x8001); // IN + Cache Flush
            WriteUInt32(bw, ttl);
            using (var rdata = new MemoryStream())
            {
                foreach (string entry in entries)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(entry);
                    rdata.WriteByte((byte)bytes.Length);
                    rdata.Write(bytes, 0, bytes.Length);
                }
                byte[] buf = rdata.ToArray();
                WriteUInt16(bw, (ushort)buf.Length);
                bw.Write(buf);
            }
        }

        private static void WriteARecord(BinaryWriter bw, string name, IPAddress ip, uint ttl)
        {
            WriteDnsName(bw, name);
            WriteUInt16(bw, 1);      // A
            WriteUInt16(bw, 0x8001); // IN + Cache Flush
            WriteUInt32(bw, ttl);
            byte[] addr = ip.GetAddressBytes();
            WriteUInt16(bw, (ushort)addr.Length);
            bw.Write(addr);
        }

        private static byte[] EncodeDnsName(string name)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                WriteDnsName(bw, name);
                return ms.ToArray();
            }
        }

        private static void WriteDnsName(BinaryWriter bw, string name)
        {
            string[] labels = name.TrimEnd('.').Split('.');
            foreach (string label in labels)
            {
                if (string.IsNullOrEmpty(label)) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(label);
                bw.Write((byte)bytes.Length);
                bw.Write(bytes);
            }
            bw.Write((byte)0);
        }

        private static string ReadDnsName(byte[] data, ref int offset)
        {
            var sb = new StringBuilder();
            int current = offset;
            bool jumped = false;
            int safety = 0;

            while (current < data.Length && safety++ < 64)
            {
                byte len = data[current];
                if (len == 0)
                {
                    if (!jumped) offset = current + 1;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    if (current + 1 >= data.Length) break;
                    int ptr = ((len & 0x3F) << 8) | data[current + 1];
                    if (!jumped) offset = current + 2;
                    jumped = true;
                    current = ptr;
                    continue;
                }
                current++;
                if (current + len > data.Length) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.UTF8.GetString(data, current, len));
                current += len;
            }
            return sb.ToString();
        }

        private static void WriteUInt16(BinaryWriter bw, ushort val)
        {
            bw.Write((byte)(val >> 8));
            bw.Write((byte)(val & 0xFF));
        }

        private static void WriteUInt32(BinaryWriter bw, uint val)
        {
            bw.Write((byte)((val >> 24) & 0xFF));
            bw.Write((byte)((val >> 16) & 0xFF));
            bw.Write((byte)((val >> 8) & 0xFF));
            bw.Write((byte)(val & 0xFF));
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
