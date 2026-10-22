using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Reachy.ControlApp;
using Reachy.Sdk.Joint;

internal static class EthernetDiscoveryTests
{
    private static int _checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        Console.WriteLine("PASS: " + description);
        _checks++;
    }

    private sealed class RobotService : JointService.JointServiceBase
    {
        internal string[] Names = { "neck_yaw", "r_shoulder_pitch" };
        internal int ReadCalls;
        internal int MotionCalls;

        public override Task<JointsId> GetAllJointsId(Empty request, ServerCallContext context)
        {
            ReadCalls++;
            var result = new JointsId();
            result.Names.Add(Names);
            return Task.FromResult(result);
        }

        public override Task<JointsCommandAck> SendJointsCommands(JointsCommand request, ServerCallContext context)
        {
            MotionCalls++;
            throw new Exception("Discovery must never send motion commands.");
        }
    }

    private static async Task RunAsync(bool live)
    {
        Check(ReachyEthernetDiscovery.IsWired(NetworkInterfaceType.Ethernet) &&
            ReachyEthernetDiscovery.IsWired(NetworkInterfaceType.GigabitEthernet), "wired adapter types are accepted");
        Check(!ReachyEthernetDiscovery.IsWired(NetworkInterfaceType.Wireless80211) &&
            !ReachyEthernetDiscovery.IsWired(NetworkInterfaceType.Tunnel), "Wi-Fi and VPN tunnels are excluded from Ethernet discovery");
        var local = new ReachyEthernetDiscovery.WiredAddress
        {
            Name = "Test Ethernet", Address = IPAddress.Parse("192.168.1.10"), Mask = IPAddress.Parse("255.255.255.0")
        };
        Check(ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse("192.168.1.109")), "same-subnet robot is eligible");
        Check(!ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse("192.168.2.109")), "different subnet is excluded");
        Check(!ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse("192.168.1.10")), "own PC / local simulator is excluded");
        Check(!ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse("192.168.1.0")) &&
            !ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse("192.168.1.255")), "network and broadcast addresses are excluded");
        Check(!ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.IPv6Loopback), "IPv6 address is not mistaken for IPv4 SDK endpoint");
        IPAddress[] subnet = ReachyEthernetDiscovery.EnumerateSmallSubnet(local).ToArray();
        Check(subnet.Length == 253 && subnet.Contains(IPAddress.Parse("192.168.1.109")), "/24 discovery covers peer hosts only");
        local.Mask = IPAddress.Parse("255.255.255.252");
        Check(ReachyEthernetDiscovery.EnumerateSmallSubnet(local).Single().Equals(IPAddress.Parse("192.168.1.9")), "/30 discovery respects the actual mask");
        local.Address = IPAddress.Parse("169.254.124.122");
        local.Mask = IPAddress.Parse("255.255.0.0");
        Check(ReachyEthernetDiscovery.IsLinkLocal(local.Address) && !ReachyEthernetDiscovery.EnumerateSmallSubnet(local).Any(),
            "automatic /16 address is identified without a 65k-host scan");
        Check(ReachyEthernetDiscovery.IsPeerInSubnet(local, IPAddress.Parse(ReachyEthernetDiscovery.DirectEthernetFallbackHost)),
            "robot's direct Ethernet fallback is reachable from the PC automatic subnet");

        var service = new RobotService();
        var port = new ServerPort("127.0.0.1", 0, ServerCredentials.Insecure);
        var server = new Server { Services = { JointService.BindService(service) }, Ports = { port } };
        server.Start();
        int boundPort = server.Ports.Single().BoundPort;
        try
        {
            var result = await ReachyEthernetDiscovery.VerifyServiceAsync("127.0.0.1", boundPort, "Fixture Ethernet", CancellationToken.None);
            Check(result != null && result.Found && result.Port == boundPort && result.InterfaceName == "Fixture Ethernet",
                "Reachy joint service is verified and discovered settings are returned");
            Check(service.ReadCalls == 1 && service.MotionCalls == 0, "discovery reads joint names without sending motion commands");
            service.Names = new string[0];
            Check(await ReachyEthernetDiscovery.VerifyServiceAsync("127.0.0.1", boundPort, "Fixture", CancellationToken.None) == null,
                "empty joint service is rejected");
            service.Names = new[] { "unrelated_motor" };
            Check(await ReachyEthernetDiscovery.VerifyServiceAsync("127.0.0.1", boundPort, "Fixture", CancellationToken.None) == null,
                "unrelated robot joint names are rejected");
        }
        finally { await server.ShutdownAsync(); }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int portNumber = ((IPEndPoint)listener.LocalEndpoint).Port;
            var stopwatch = Stopwatch.StartNew();
            Check(await ReachyEthernetDiscovery.VerifyServiceAsync("127.0.0.1", portNumber, "Fixture", CancellationToken.None) == null &&
                stopwatch.Elapsed < TimeSpan.FromSeconds(4), "an open TCP port without Reachy is rejected within the deadline");
            using (var cancellation = new CancellationTokenSource(100))
            {
                bool canceled = false;
                try { await ReachyEthernetDiscovery.VerifyServiceAsync("127.0.0.1", portNumber, "Fixture", cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "shutdown cancellation interrupts an outstanding probe");
            }
        }
        finally { listener.Stop(); }

        if (live)
        {
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var result = await ReachyEthernetDiscovery.DiscoverAsync(new[] { "192.168.1.109" }, new[] { 50055, 3972 }, cancellation.Token);
                Console.WriteLine("LIVE: " + result.Message);
            }
        }
        Console.WriteLine("All " + _checks + " checks passed.");
    }

    private static int Main(string[] args)
    {
        try { RunAsync(args.Contains("--live")).GetAwaiter().GetResult(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
