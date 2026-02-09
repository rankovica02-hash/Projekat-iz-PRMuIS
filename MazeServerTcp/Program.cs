using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Common;

namespace MazeServerTcp;

internal sealed class ClientConn
{
    public Socket Sock { get; }
    public List<byte> Rx { get; } = new List<byte>(8192);

    public int? JedinkaId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public Stack<(int x, int y)> DfsStack { get; } = new Stack<(int x, int y)>();
    public HashSet<long> Visited { get; } = new HashSet<long>();

    public Komanda LastMove { get; set; } = new Komanda(0, 0);

    public long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

    public ClientConn(Socket s) => Sock = s;
}

internal static class Program
{
    private const int Port = 50005;
    private static int lastLines = 0;

    // Zadatak 3: Drugi server drzi svoj "prazan" lavirint i popunjava ga onim sto sazna od klijenata.
    // Inicijalno je sve Path (nepoznato nam je gde su zidovi), a kasnije update-ujemo po 3x3 okolini.


    private static readonly Dictionary<Socket, ClientConn> Clients = new();

    // Zadatak 6: ciljevi (globalno) i evidencija ko ih je pronasao.
    private static readonly List<Point> ExpectedGoals = new();
    private static bool GoalsInitialized = false;
    private static readonly Dictionary<long, int> FoundByJedinka = new();
    private static bool SearchFinished = false;

    private static void Main()
    {
        var listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listen.Bind(new IPEndPoint(IPAddress.Any, Port));
        listen.Listen(15);
        listen.Blocking = false;

        Console.WriteLine($"[TCP Server2] Listening on 0.0.0.0:{Port} (max 15)");

        while (true)
        {
            var readSet = new List<Socket> { listen };
            foreach (var s in Clients.Keys) readSet.Add(s);

            Socket.Select(readSet, null, null, 200_000);

            foreach (var s in readSet)
            {
                if (s == listen) AcceptAll(listen);
                else HandleClientReadable(s);
            }

            RenderStatus();
        }
    }

    private static void AcceptAll(Socket listen)
    {
        while (true)
        {
            Socket? client;
            try
            {
                client = listen.Accept();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break;
            }

            client.Blocking = false;
            Clients[client] = new ClientConn(client);
        }
    }

    private static void HandleClientReadable(Socket s)
    {
        if (!Clients.TryGetValue(s, out var c)) return;

        var buf = new byte[4096];
        int n;
        try
        {
            n = s.Receive(buf);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
        {
            return;
        }

        if (n == 0)
        {
            CloseClient(s);
            return;
        }

        for (int i = 0; i < n; i++) c.Rx.Add(buf[i]);

        while (true)
        {
            byte[] payload;
            try
            {
                if (!TcpFraming.TryExtractFrame(c.Rx, out payload)) return;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[TCP Server2] Bad frame from {s.RemoteEndPoint}: {e.Message} -> closing");
                CloseClient(s);
                return;
            }

            TcpEnvelope env;
            try
            {
                env = BinarySer.Deserialize<TcpEnvelope>(payload, payload.Length);
            }
            catch
            {
                Console.WriteLine($"[TCP Server2] Deserialize failed from {s.RemoteEndPoint} -> closing");
                CloseClient(s);
                return;
            }

            ProcessEnvelope(c, env);
        }
    }

    private static void ProcessEnvelope(ClientConn c, TcpEnvelope env)
    {
        if (SearchFinished)
        {
            return;
        }

        switch (env.Type)
        {
            case TcpMsgType.Hello:
                {
                    var hello = (HelloPayload)env.Payload;
                    c.JedinkaId = hello.JedinkaId;
                    c.X = hello.StartX;
                    c.Y = hello.StartY;
                    c.Visited.Clear();
                    c.DfsStack.Clear();
                    c.DfsStack.Push((c.X, c.Y));
                    c.Visited.Add(c.Key(c.X, c.Y));

                    SendCommand(c, new Komanda(0, 0), finished: false, note: "ACK");
                    break;
                }
            case TcpMsgType.State:
                {
                    var st = (StatePayload)env.Payload;
                    c.JedinkaId ??= st.JedinkaId;
                    c.X = st.X;
                    c.Y = st.Y;
                    DfsSyncToCurrent(c);



                    // Prvi put kada dobijemo listu ciljeva, inicijalizuj ocekivane ciljeve.
                    if (!GoalsInitialized && st.KnownGoals != null && st.KnownGoals.Count > 0)
                    {
                        ExpectedGoals.Clear();
                        ExpectedGoals.AddRange(st.KnownGoals);
                        GoalsInitialized = true;
                    }

                    // Detekcija ciljeva (Zadatak 6) – klijent prijavljuje kada je stao na cilj.
                    if (st.GoalFound)
                    {
                        long gk = ((long)st.GoalX << 32) ^ (uint)st.GoalY;
                        if (!FoundByJedinka.ContainsKey(gk))
                        {
                            FoundByJedinka[gk] = st.JedinkaId;
                        }
                    }


                    var allowed = st.MoguciPravci ?? new List<Komanda>();
                    Komanda move = ChooseMoveDfs(c, allowed);

                    SendCommand(c, move, finished: false, note: "dfs");

                    // Ako su svi ciljevi pronadjeni – zavrsi pretragu i obavesti klijente.
                    if (!SearchFinished && GoalsInitialized && ExpectedGoals.Count > 0 && FoundByJedinka.Count >= ExpectedGoals.Count)
                    {
                        SearchFinished = true;
                        BroadcastFinish();
                    }

                    break;
                }
            default:
                break;
        }
    }

    private static void DfsSyncToCurrent(ClientConn c)
    {
        if (c.DfsStack.Count == 0)
        {
            c.DfsStack.Push((c.X, c.Y));
            c.Visited.Add(c.Key(c.X, c.Y));
            return;
        }

        while (c.DfsStack.Count > 0)
        {
            var top = c.DfsStack.Peek();
            if (top.x == c.X && top.y == c.Y) return;
            c.DfsStack.Pop();
        }

        c.DfsStack.Push((c.X, c.Y));
        c.Visited.Add(c.Key(c.X, c.Y));
    }

    private static Komanda ChooseMoveDfs(ClientConn c, List<Komanda> allowedMoves)
    {
        if (allowedMoves.Count == 0) return new Komanda(0, 0);

        var order = new List<Komanda>
        {
            new Komanda(1, 0),
            new Komanda(0, 1),
            new Komanda(-1, 0),
            new Komanda(0, -1)
        };

        bool IsAllowed(Komanda m)
        {
            foreach (var a in allowedMoves)
                if (a.X == m.X && a.Y == m.Y) return true;
            return false;
        }

        foreach (var dir in order)
        {
            if (!IsAllowed(dir)) continue;
            int nx = c.X + dir.X;
            int ny = c.Y + dir.Y;
            var k = c.Key(nx, ny);
            if (c.Visited.Contains(k)) continue;

            c.Visited.Add(k);
            c.DfsStack.Push((nx, ny));
            return dir;
        }

        if (c.DfsStack.Count > 1)
        {
            var cur = c.DfsStack.Pop();
            var prev = c.DfsStack.Peek();

            var back = new Komanda(prev.x - cur.x, prev.y - cur.y);
            if (IsAllowed(back))
                return back;

            c.DfsStack.Push(cur);
        }

        return new Komanda(0, 0);
    }

    private static void BroadcastFinish()
    {
        Console.WriteLine("\n[TCP Server2] ALL GOALS FOUND -> FINISH");
        Console.WriteLine("[TCP Server2] Found goals:");
        foreach (var g in ExpectedGoals)
        {
            long k = ((long)g.X << 32) ^ (uint)g.Y;
            if (FoundByJedinka.TryGetValue(k, out var who))
                Console.WriteLine($"  Goal {g} -> Jedinka#{who}");
            else
                Console.WriteLine($"  Goal {g} -> (not reported)");
        }
        Console.WriteLine();

        foreach (var c in Clients.Values)
        {
            SendCommand(c, new Komanda(0, 0), finished: true, note: "FINISH");
        }
    }

    // Zadatak 3: Server2 update-uje svoju "praznu" mapu na osnovu 3x3 okoline koju klijent dobije od Server1.

    // Zadatak 7: prikaz trenutnih pozicija i narednih poteza u realnom vremenu.
    private static void RenderStatus()
    {
        Console.SetCursorPosition(0, 0);

        int linesNow = 0;

        ClearLine();
        Console.WriteLine("[TCP Server2] Status");
        linesNow++;

        ClearLine();
        Console.WriteLine($"Connected: {Clients.Count} | GoalsKnown: {(GoalsInitialized ? ExpectedGoals.Count : 0)} | Found: {FoundByJedinka.Count} | Finished: {SearchFinished}");
        linesNow++;

        ClearLine();
        Console.WriteLine();
        linesNow++;

        foreach (var c in Clients.Values)
        {
            var id = c.JedinkaId ?? -1;

            ClearLine();
            Console.WriteLine($"Jedinka#{id} pos=({c.X},{c.Y}) cmd={c.LastMove}");
            linesNow++;
        }

        ClearLine();
        Console.WriteLine();
        linesNow++;

        if (GoalsInitialized)
        {
            ClearLine();
            Console.WriteLine("Found goals:");
            linesNow++;

            foreach (var g in ExpectedGoals)
            {
                long k = ((long)g.X << 32) ^ (uint)g.Y;
                string who = FoundByJedinka.TryGetValue(k, out var w) ? $"Jedinka#{w}" : "-";

                ClearLine();
                Console.WriteLine($"  {g} -> {who}");
                linesNow++;
            }
        }

        ClearLine();
        Console.WriteLine();
        linesNow++;

        while (linesNow < lastLines)
        {
            ClearLine();
            Console.WriteLine();
            linesNow++;
        }

        lastLines = linesNow;
    }

    private static void SendCommand(ClientConn c, Komanda move, bool finished, string note)
    {
        c.LastMove = move;
        var env = new TcpEnvelope
        {
            Type = TcpMsgType.Command,
            Payload = new CommandPayload
            {
                JedinkaId = c.JedinkaId ?? -1,
                Move = move,
                Finished = finished,
                Note = note
            }
        };

        Send(c.Sock, env);
    }

    private static void Send(Socket s, TcpEnvelope env)
    {
        var packet = TcpFraming.BuildPacket(env);

        try
        {
            s.Send(packet);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
        {

        }
    }
    private static void CloseClient(Socket s)
    {
        try { s.Shutdown(SocketShutdown.Both); } catch { }
        try { s.Close(); } catch { }
        Clients.Remove(s);
    }
    static void ClearLine()
    {
        int w = Console.WindowWidth;
        Console.Write(new string(' ', w));
        Console.Write('\r');
    }
}
