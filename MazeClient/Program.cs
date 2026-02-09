using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Common;

namespace MazeClient;

internal static class Program
{
    private const int UdpPort = 50032;
    private const int TcpPort = 50005;

    private static void Main(string[] args)
    {
        int id = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 1;

        string udpHost = args.Length > 1 ? args[1] : "127.0.0.1";
        string tcpHost = args.Length > 2 ? args[2] : "127.0.0.1";

        int x = 0, y = 0;

        var reportedGoals = new HashSet<long>();
        List<Point>? knownGoals = null;
        bool sentGoalsToServer2 = false;

        var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false
        };
        var udpEp = new IPEndPoint(IPAddress.Parse(udpHost), UdpPort);

        var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            Blocking = false
        };
        var tcpEp = new IPEndPoint(IPAddress.Parse(tcpHost), TcpPort);

        Console.WriteLine($"[Client#{id}] UDP -> {udpEp}, TCP -> {tcpHost}:{TcpPort}");

        while (true)
        {
            try
            {
                tcp.Connect(tcpEp);
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
            {
                var ws = new List<Socket> { tcp };
                Socket.Select(null, ws, null, 200_000);
                if (ws.Count > 0 && tcp.Connected) break;
            }
        }

        Console.WriteLine($"[Client#{id}] TCP connected. Sending HELLO...");
        Send(tcp, new TcpEnvelope
        {
            Type = TcpMsgType.Hello,
            Payload = new HelloPayload { JedinkaId = id, StartX = x, StartY = y }
        });

        var ack = ReceiveOneCommand(tcp);
        if (ack != null)
            Console.WriteLine($"[Client#{id}] Server ACK: cmd {ack.Move} note={ack.Note}");

        var rx = new List<byte>(8192);
        var tcpBuf = new byte[4096];

        int steps = 0;
        while (true)
        {
            steps++;

            var req = new UdpRequest { JedinkaId = id, X = x, Y = y };
            var reqBytes = BinarySer.Serialize(req);

            try { udp.SendTo(reqBytes, udpEp); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }

            UdpResponse? env = ReceiveUdpResponse(udp, out _);
            if (env == null)
            {
                Socket.Select(new List<Socket> { udp }, null, null, 200_000);
                env = ReceiveUdpResponse(udp, out _);
            }

            if (env == null)
            {
                Console.WriteLine($"[Client#{id}] No UDP response, retrying...");
                continue;
            }

            // Ciljevi: Server1 salje listu ciljeva, a klijent prijavljuje Server2 kada stane na cilj.
            knownGoals ??= env.Goals;

            bool goalFound = false;
            int goalX = 0, goalY = 0;

            if (env.IsGoalHere)
            {
                long k = ((long)x << 32) ^ (uint)y;
                if (!reportedGoals.Contains(k))
                {
                    reportedGoals.Add(k);
                    goalFound = true;
                    goalX = x;
                    goalY = y;
                    Console.WriteLine($"[Client#{id}] GOAL REACHED at ({x},{y}) in {steps} steps (reported to Server2).");
                }
            }

            var moves = ComputeMoves(env.Okolina);

            var stPayload = new StatePayload
            {
                JedinkaId = id,
                X = x,
                Y = y,
                MoguciPravci = moves,
                Okolina = env.Okolina,
                GoalFound = goalFound,
                GoalX = goalX,
                GoalY = goalY
            };

            if (!sentGoalsToServer2 && knownGoals != null && knownGoals.Count > 0)
            {
                stPayload.KnownGoals = new List<Point>(knownGoals);
                sentGoalsToServer2 = true;
            }

            Send(tcp, new TcpEnvelope { Type = TcpMsgType.State, Payload = stPayload });

            var rs = new List<Socket> { tcp };
            Socket.Select(rs, null, null, 500_000);
            if (rs.Count == 0)
            {
                Console.WriteLine($"[Client#{id}] No TCP command (timeout).");
                continue;
            }

            int n;
            try { n = tcp.Receive(tcpBuf); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { continue; }

            if (n == 0)
            {
                Console.WriteLine($"[Client#{id}] TCP disconnected by server.");
                break;
            }

            for (int i = 0; i < n; i++) rx.Add(tcpBuf[i]);

            var cmd = TryParseFirstCommand(rx);
            if (cmd == null)
                continue;

            if (cmd.Finished)
            {
                Console.WriteLine($"[Client#{id}] FINISH received from Server2. note={cmd.Note}");
                break;
            }

            x += cmd.Move.X;
            y += cmd.Move.Y;

            Console.WriteLine($"[Client#{id}] Step {steps}: move {cmd.Move} -> ({x},{y})");

            Thread.Sleep(200);
        }

        try { tcp.Shutdown(SocketShutdown.Both); } catch { }
        try { tcp.Close(); } catch { }
        try { udp.Close(); } catch { }
    }

    private static UdpResponse? ReceiveUdpResponse(Socket udp, out EndPoint? remote)
    {
        remote = new IPEndPoint(IPAddress.Any, 0);
        var buf = new byte[4096];

        try
        {
            if (!udp.Poll(50_000, SelectMode.SelectRead))
                return null;

            int n = udp.ReceiveFrom(buf, ref remote);
            return BinarySer.Deserialize<UdpResponse>(buf, n);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<Komanda> ComputeMoves(Okolina ok)
    {
        var list = new List<Komanda>();

        var candidates = new[]
        {
            new Komanda(1, 0),
            new Komanda(-1, 0),
            new Komanda(0, 1),
            new Komanda(0, -1),
        };

        foreach (var m in candidates)
        {
            int ny = 1 + m.Y;
            int nx = 1 + m.X;
            if (nx < 0 || nx > 2 || ny < 0 || ny > 2) continue;

            var p = ok.Mapa[ny, nx];
            if (p.Tip != FieldType.Wall && !p.Zauzeto)
                list.Add(m);
        }

        return list;
    }

    private static void Send(Socket tcp, TcpEnvelope env)
    {
        var packet = TcpFraming.BuildPacket(env);
        try { tcp.Send(packet); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }
    }

    private static CommandPayload? ReceiveOneCommand(Socket tcp)
    {
        var rx = new List<byte>(8192);
        var buf = new byte[4096];

        for (int tries = 0; tries < 10; tries++)
        {
            var rs = new List<Socket> { tcp };
            Socket.Select(rs, null, null, 200_000);
            if (rs.Count == 0) continue;

            int n;
            try { n = tcp.Receive(buf); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { continue; }

            if (n <= 0) return null;
            for (int i = 0; i < n; i++) rx.Add(buf[i]);

            var cmd = TryParseFirstCommand(rx);
            if (cmd != null) return cmd;
        }

        return null;
    }

    private static CommandPayload? TryParseFirstCommand(List<byte> rx)
    {
        byte[] payload;
        try
        {
            if (!TcpFraming.TryExtractFrame(rx, out payload))
                return null;
        }
        catch
        {
            return null;
        }

        TcpEnvelope env;
        try { env = BinarySer.Deserialize<TcpEnvelope>(payload, payload.Length); }
        catch { return null; }

        if (env.Type != TcpMsgType.Command && env.Type != TcpMsgType.Finished) return null;
        return (CommandPayload)env.Payload;
    }
}