using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Reachy.Sdk.Joint;

namespace Reachy.ControlApp
{
    // Read-only discovery. Changing either computer's IP configuration needs a separate setup step.
    internal static class ReachyEthernetDiscovery
    {
        // Optional secondary address configured on the robot; DHCP can remain enabled.
        internal const string DirectEthernetFallbackHost = "169.254.42.1";

        internal sealed class WiredAddress
        {
            internal string Name;
            internal int InterfaceIndex;
            internal IPAddress Address;
            internal IPAddress Mask;
        }

        internal sealed class Result
        {
            internal string Host;
            internal int Port;
            internal string InterfaceName;
            internal string Message;
            internal bool Ambiguous;
            internal bool Found => !string.IsNullOrEmpty(Host);
        }

        private sealed class Candidate
        {
            internal IPAddress Address;
            internal WiredAddress Local;
            internal int Port;
            internal bool TcpReachable;
        }

        internal static async Task<Result> DiscoverAsync(
            string[] hostHints, int[] ports, CancellationToken cancellation)
        {
            List<WiredAddress> wired = GetWiredAddresses();
            if (wired.Count == 0)
            {
                return new Result { Message = "No active Ethernet IPv4 connection. Connect and power on Reachy; discovery retries automatically." };
            }

            var addresses = new List<IPAddress>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<IPAddress> addAddress = address =>
            {
                if (address != null && address.AddressFamily == AddressFamily.InterNetwork &&
                    seen.Add(address.ToString())) addresses.Add(address);
            };
            var lookups = new List<Task<IPAddress[]>>();
            foreach (string hint in hostHints.Concat(new[] { DirectEthernetFallbackHost, "reachy.local", "reachy" }).Distinct())
            {
                if (IPAddress.TryParse(hint, out IPAddress literal)) addAddress(literal);
                else if (!string.IsNullOrWhiteSpace(hint)) lookups.Add(ResolveAsync(hint));
            }

            Task<IPAddress[][]> allLookups = Task.WhenAll(lookups);
            if (await Task.WhenAny(allLookups, Task.Delay(1200, cancellation)) == allLookups)
            {
                foreach (IPAddress[] resolved in await allLookups)
                    foreach (IPAddress address in resolved) addAddress(address);
            }
            cancellation.ThrowIfCancellationRequested();
            foreach (IPAddress address in ReadWindowsNeighbours(wired)) addAddress(address);
            foreach (WiredAddress local in wired)
                foreach (IPAddress address in EnumerateSmallSubnet(local)) addAddress(address);

            // Check the OS route as well as the subnet: gRPC must actually use Ethernet, not Wi-Fi.
            var candidates = new List<Candidate>();
            foreach (IPAddress address in addresses)
            {
                if (wired.Any(local => local.Address.Equals(address))) continue;
                WiredAddress source = wired.FirstOrDefault(local => IsPeerInSubnet(local, address) && UsesWiredRoute(local, address));
                if (source == null) continue;
                foreach (int port in ports.Where(port => port > 0 && port <= 65535).Distinct().Take(4))
                    candidates.Add(new Candidate { Address = address, Local = source, Port = port });
            }

            using (var slots = new SemaphoreSlim(32))
            {
                Task<Result>[] probes = candidates.Select(async candidate =>
                {
                    await slots.WaitAsync(cancellation);
                    try { return await ProbeAsync(candidate, cancellation); }
                    finally { slots.Release(); }
                }).ToArray();
                Result[] results = await Task.WhenAll(probes);
                List<Result> robots = results.Where(result => result != null)
                    .GroupBy(result => result.Host).Select(group => group.First()).ToList();
                if (robots.Count == 1) return robots[0];
                if (robots.Count > 1)
                {
                    return new Result { Ambiguous = true, Message = "Multiple Reachy endpoints found on Ethernet: " +
                        string.Join(", ", robots.Select(robot => robot.Host)) + ". Select the intended robot manually." };
                }
            }

            string interfaces = string.Join(", ", wired.Select(local => local.Name + ": " + local.Address));
            string[] respondingPeers = candidates.Where(candidate => candidate.TcpReachable)
                .Select(candidate => candidate.Address.ToString()).Distinct().ToArray();
            if (respondingPeers.Length > 0)
            {
                return new Result { Message = "Ethernet devices respond at " + string.Join(", ", respondingPeers) +
                    ", but no Reachy joint service was found. Check that the robot SDK service is running. Discovery retries automatically." };
            }
            bool onlyLinkLocal = wired.All(local => IsLinkLocal(local.Address));
            return new Result
            {
                Message = onlyLinkLocal
                    ? "Ethernet connected (" + interfaces + "), but no Reachy service found. IPv4 setup may be required: " +
                      "the PC only has an automatic 169.254 address. Set the PC and robot Ethernet IPv4 addresses in the same subnet once. " +
                      "IP addresses cannot be inferred from the cable alone."
                    : "Ethernet connected (" + interfaces + "), but no Reachy joint service found. " +
                      "Check robot power, Ethernet IPv4 subnet and SDK service. Discovery retries automatically."
            };
        }

        internal static IEnumerable<NetworkInterface> GetActiveWiredAdapters()
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || !IsWired(adapter.NetworkInterfaceType)) continue;
                string description = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
                if (new[] { "virtual", "vpn", "wintun", "hyper-v", "vmware", "loopback", "tap-windows" }
                    .Any(description.Contains)) continue;
                yield return adapter;
            }
        }

        internal static List<WiredAddress> GetWiredAddresses()
        {
            var result = new List<WiredAddress>();
            foreach (NetworkInterface adapter in GetActiveWiredAdapters())
            {
                try
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    IPv4InterfaceProperties ipv4 = properties.GetIPv4Properties();
                    if (ipv4 == null) continue;
                    foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address)) continue;
                        result.Add(new WiredAddress { Name = adapter.Name, InterfaceIndex = ipv4.Index,
                            Address = unicast.Address, Mask = unicast.IPv4Mask });
                    }
                }
                catch (NetworkInformationException) { /* Adapter changed while enumerating. */ }
            }
            return result;
        }

        internal static WiredAddress FindWiredSource(string host)
        {
            if (!IPAddress.TryParse(host, out IPAddress address)) return null;
            return GetWiredAddresses().FirstOrDefault(local => IsPeerInSubnet(local, address) && UsesWiredRoute(local, address));
        }

        [Serializable]
        internal sealed class SshRoute
        {
            public string host;
            public string source;
            public bool directFallback;
        }

        internal static async Task<SshRoute[]> FindManagementRoutesAsync(IEnumerable<string> hints,
            string cachedLinkLocal, CancellationToken cancellation)
        {
            var ipv4 = GetWiredAddresses();
            var linkLocal = new List<IPAddress>();
            foreach (NetworkInterface adapter in GetActiveWiredAdapters())
            {
                try
                {
                    var properties = adapter.GetIPProperties();
                    var index = properties.GetIPv6Properties()?.Index ?? 0;
                    if (index == 0) continue;
                    foreach (var address in properties.UnicastAddresses.Select(item => item.Address).Where(ip => ip.IsIPv6LinkLocal))
                        linkLocal.Add(new IPAddress(address.GetAddressBytes(), index));
                }
                catch (NetworkInformationException) { }
            }

            var routes = new List<SshRoute>();
            var seen = new HashSet<string>();
            void Add(IPAddress address)
            {
                if (address.IsIPv6LinkLocal)
                {
                    foreach (IPAddress source in linkLocal)
                    {
                        if (address.ScopeId != 0 && address.ScopeId != source.ScopeId ||
                            address.GetAddressBytes().SequenceEqual(source.GetAddressBytes())) continue;
                        string host = new IPAddress(address.GetAddressBytes(), source.ScopeId).ToString();
                        if (seen.Add(host)) routes.Add(new SshRoute { host = host, source = source.ToString(),
                            directFallback = ipv4.Any(local => local.InterfaceIndex == source.ScopeId && IsLinkLocal(local.Address) &&
                                IsPeerInSubnet(local, IPAddress.Parse(DirectEthernetFallbackHost))) });
                    }
                    return;
                }
                WiredAddress local = ipv4.FirstOrDefault(item => IsPeerInSubnet(item, address) && UsesWiredRoute(item, address));
                if (local != null && seen.Add(address.ToString()))
                    routes.Add(new SshRoute { host = address.ToString(), source = local.Address.ToString() });
            }

            if (IPAddress.TryParse(cachedLinkLocal, out IPAddress cached) && cached.IsIPv6LinkLocal)
                Add(new IPAddress(cached.GetAddressBytes())); // The Windows interface number can change between boots.
            var lookups = new List<Task<IPAddress[]>>();
            foreach (string hint in hints.Concat(new[] { DirectEthernetFallbackHost, "reachy.local" }).Distinct())
            {
                if (IPAddress.TryParse(hint, out var literal)) Add(literal);
                else if (!string.IsNullOrWhiteSpace(hint)) lookups.Add(ResolveAsync(hint));
            }
            var resolved = Task.WhenAll(lookups);
            if (await Task.WhenAny(resolved, Task.Delay(900, cancellation)) == resolved)
                foreach (var address in (await resolved).SelectMany(x => x)) Add(address);
            cancellation.ThrowIfCancellationRequested();
            foreach (var address in ReadWindowsLinkLocalNeighbours(linkLocal)) Add(address);
            foreach (var address in ReadWindowsNeighbours(ipv4)) Add(address);
            return routes.Take(12).ToArray();
        }

        // MIB_IPNET_ROW2 contains a 28-byte SOCKADDR_INET union and is aligned
        // by its 64-bit NET_LUID. Use the marshaller for the row/table stride.
        [StructLayout(LayoutKind.Sequential)]
        private struct NeighbourRow
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)] public byte[] Address;
            public uint InterfaceIndex;
            public ulong InterfaceLuid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
            public uint PhysicalAddressLength;
            public int State;
            public byte Flags;
            public uint ReachabilityTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NeighbourTableStart { public uint Count; public NeighbourRow First; }

        [DllImport("iphlpapi.dll")]
        private static extern uint GetIpNetTable2(ushort family, out IntPtr table);

        [DllImport("iphlpapi.dll")]
        private static extern void FreeMibTable(IntPtr table);

        private static List<IPAddress> ReadWindowsLinkLocalNeighbours(List<IPAddress> wired)
        {
            var addresses = new List<IPAddress>();
            if (wired.Count == 0 || Environment.OSVersion.Platform != PlatformID.Win32NT) return addresses;
            IntPtr table = IntPtr.Zero;
            try
            {
                const ushort Inet6 = 23;
                if (GetIpNetTable2(Inet6, out table) != 0 || table == IntPtr.Zero) return addresses;
                int size = Marshal.SizeOf(typeof(NeighbourRow));
                int start = Marshal.OffsetOf(typeof(NeighbourTableStart), nameof(NeighbourTableStart.First)).ToInt32();
                int count = Math.Min(Marshal.ReadInt32(table), 4096);
                for (int i = 0; i < count; i++)
                {
                    var row = (NeighbourRow)Marshal.PtrToStructure(IntPtr.Add(table, start + i * size), typeof(NeighbourRow));
                    if (row.PhysicalAddressLength != 6 || (row.PhysicalAddress[0] & 1) != 0 ||
                        !wired.Any(source => source.ScopeId == row.InterfaceIndex) || BitConverter.ToUInt16(row.Address, 0) != Inet6) continue;
                    var peer = new IPAddress(row.Address.Skip(8).Take(16).ToArray(), row.InterfaceIndex);
                    if (peer.IsIPv6LinkLocal) addresses.Add(peer);
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (table != IntPtr.Zero) FreeMibTable(table); }
            return addresses;
        }

        internal static bool IsWired(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.GigabitEthernet ||
                type == NetworkInterfaceType.FastEthernetFx || type == NetworkInterfaceType.FastEthernetT;
        }

        internal static bool IsLinkLocal(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }

        internal static bool IsPeerInSubnet(WiredAddress local, IPAddress remote)
        {
            if (remote.AddressFamily != AddressFamily.InterNetwork || local.Mask == null) return false;
            byte[] address = local.Address.GetAddressBytes(), peer = remote.GetAddressBytes(), mask = local.Mask.GetAddressBytes();
            uint hostMask = ~ToNumber(local.Mask);
            uint peerHost = ToNumber(remote) & hostMask;
            if (peer[0] == 0 || peer[0] >= 224 || IPAddress.IsLoopback(remote) || remote.Equals(local.Address) ||
                hostMask > 1 && (peerHost == 0 || peerHost == hostMask)) return false;
            for (int i = 0; i < 4; i++)
                if ((address[i] & mask[i]) != (peer[i] & mask[i])) return false;
            return true;
        }

        internal static IEnumerable<IPAddress> EnumerateSmallSubnet(WiredAddress local)
        {
            if (local.Mask == null) yield break;
            uint mask = ToNumber(local.Mask), hostMask = ~mask;
            // Never sweep a /16 link-local network or a large shared LAN.
            if (hostMask < 3 || hostMask > 255 || (hostMask & (hostMask + 1)) != 0) yield break;
            uint network = ToNumber(local.Address) & mask;
            for (uint host = 1; host < hostMask; host++)
            {
                IPAddress peer = FromNumber(network | host);
                if (!peer.Equals(local.Address)) yield return peer;
            }
        }

        private static uint ToNumber(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static IPAddress FromNumber(uint value)
        {
            return new IPAddress(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        }

        private static bool UsesWiredRoute(WiredAddress local, IPAddress remote)
        {
            try
            {
                using (var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    route.Connect(new IPEndPoint(remote, 50055)); // Selects a route without sending a packet.
                    return ((IPEndPoint)route.LocalEndPoint).Address.Equals(local.Address);
                }
            }
            catch (SocketException) { return false; }
        }

        private static async Task<IPAddress[]> ResolveAsync(string host)
        {
            try { return await Dns.GetHostAddressesAsync(host); }
            catch { return new IPAddress[0]; }
        }

        private static async Task<Result> ProbeAsync(Candidate candidate, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using (var tcp = new TcpClient(AddressFamily.InterNetwork))
                {
                    tcp.Client.Bind(new IPEndPoint(candidate.Local.Address, 0));
                    Task connect = tcp.ConnectAsync(candidate.Address, candidate.Port);
                    Task completed = await Task.WhenAny(connect, Task.Delay(250, cancellation));
                    if (completed != connect)
                    {
                        // Observe the pending connect's exception after the socket is disposed.
                        _ = connect.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        cancellation.ThrowIfCancellationRequested();
                        return null;
                    }
                    await connect;
                    candidate.TcpReachable = true;
                }
                return await VerifyServiceAsync(candidate.Address.ToString(), candidate.Port, candidate.Local.Name, cancellation);
            }
            catch (SocketException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }

        internal static async Task<Result> VerifyServiceAsync(string host, int port, string interfaceName, CancellationToken cancellation)
        {
            var channel = new Channel(host, port, ChannelCredentials.Insecure);
            try
            {
                var client = new JointService.JointServiceClient(channel);
                using (var names = client.GetAllJointsIdAsync(new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(1.2), cancellationToken: cancellation))
                {
                    JointsId joints = await names.ResponseAsync;
                    if (!HasReachyJoints(joints)) return null;
                    var request = new JointsStateRequest();
                    request.RequestedFields.Add(JointField.PresentPosition);
                    foreach (string name in joints.Names.Distinct()) request.Ids.Add(new JointId { Name = name });
                    using (var positions = client.GetJointsStateAsync(request,
                        deadline: DateTime.UtcNow.AddSeconds(1.2), cancellationToken: cancellation))
                    {
                        JointsState sample = await positions.ResponseAsync;
                        var observed = new HashSet<string>();
                        for (int i = 0; i < Math.Min(sample.Ids.Count, sample.States.Count); i++)
                        {
                            float? value = sample.States[i].PresentPosition;
                            if (value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
                                observed.Add(sample.Ids[i].Name);
                        }
                        if (request.Ids.Any(id => !observed.Contains(id.Name))) return null;
                        return new Result { Host = host, Port = port, InterfaceName = interfaceName,
                            Message = "Reachy detected via " + interfaceName + " at " + host + ":" + port +
                                " (" + request.Ids.Count + " joint positions verified)." };
                    }
                }
            }
            catch (RpcException) { cancellation.ThrowIfCancellationRequested(); return null; }
            finally { await channel.ShutdownAsync(); }
        }

        internal static bool HasReachyJoints(JointsId joints)
        {
            return joints != null && joints.Names.Any(name => name == "neck_yaw" || name == "head_yaw" ||
                name == "l_shoulder_pitch" || name == "r_shoulder_pitch");
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetIpNetTable(IntPtr table, ref int size, bool order);

        private static List<IPAddress> ReadWindowsNeighbours(List<WiredAddress> wired)
        {
            var addresses = new List<IPAddress>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return addresses;
            IntPtr table = IntPtr.Zero;
            try
            {
                int size = 0;
                GetIpNetTable(IntPtr.Zero, ref size, false);
                if (size < 4 || size > 1024 * 1024) return addresses;
                table = Marshal.AllocHGlobal(size);
                if (GetIpNetTable(table, ref size, false) != 0) return addresses;
                int count = Marshal.ReadInt32(table);
                for (int i = 0; i < count && 4 + (i + 1) * 24 <= size; i++)
                {
                    IntPtr row = IntPtr.Add(table, 4 + i * 24);
                    int index = Marshal.ReadInt32(row), type = Marshal.ReadInt32(row, 20);
                    if (type != 3 && type != 4 || !wired.Any(local => local.InterfaceIndex == index)) continue;
                    addresses.Add(new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 16))));
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (table != IntPtr.Zero) Marshal.FreeHGlobal(table); }
            return addresses;
        }
    }
}
