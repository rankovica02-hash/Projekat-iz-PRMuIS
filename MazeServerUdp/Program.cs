using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Common;

namespace MazeServerUdp;

internal static class Program
{
    private const int W = 10;
    private const int H = 10;
    private const int Port = 50032;

    private static readonly Polje[,] Maze = new Polje[H, W];

    // Lista ciljeva (globalno) – koristi se i za slanje klijentima i za vizuelizaciju.
    private static readonly List<Point> Goals = new();

    // Prati poslednju poznatu poziciju svake jedinke 
    private static readonly Dictionary<int, (int x, int y)> Jedinke = new();

    private static void Main()
    {
        InitMaze();

        var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Any, Port));
        server.Blocking = false;

        Console.WriteLine($"[UDP Server1] Listening on 0.0.0.0:{Port}");
        Console.WriteLine("[UDP Server1] Request: UdpRequest{JedinkaId,X,Y} (BinaryFormatter)\n");

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        var buffer = new byte[4096];

        int logCounter = 0;

        while (true)
        {
            if (!server.Poll(200 * 1000, SelectMode.SelectRead))
                continue;

            int bytes;
            try
            {
                bytes = server.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                continue;
            }

            UdpRequest req;
            try
            {
                req = BinarySer.Deserialize<UdpRequest>(buffer, bytes);
            }
            catch
            {
                Console.WriteLine($"[UDP Server1] Bad packet from {remote} (bytes={bytes})");
                continue;
            }

            UpdateOccupancy(req.JedinkaId, req.X, req.Y);

            var resp = new UdpResponse
            {
                Okolina = GetOkolina(req.X, req.Y),
                IsGoalHere = InBounds(req.X, req.Y) && Maze[req.Y, req.X].Tip == FieldType.Goal,
                Goals = new List<Point>(Goals)
            };

            var outBytes = BinarySer.Serialize(resp);
            server.SendTo(outBytes, remote);

            // Vizuelizacija lavirinta se osvezava u svakom ciklusu.
            RenderMaze();

            logCounter++;
            if (logCounter % 5 == 0)
                Console.WriteLine($"[UDP Server1] Jedinka#{req.JedinkaId} at ({req.X},{req.Y}) -> okolina sent to {remote}");

            RenderMaze();
        }
    }

    private static void UpdateOccupancy(int id, int x, int y)
    {
        if (Jedinke.TryGetValue(id, out var old))
        {
            if (InBounds(old.x, old.y) && Maze[old.y, old.x].Tip != FieldType.Wall)
                Maze[old.y, old.x].Zauzeto = false;
        }

        if (InBounds(x, y) && Maze[y, x].Tip != FieldType.Wall)
            Maze[y, x].Zauzeto = true;

        Jedinke[id] = (x, y);
    }

    private static void InitMaze()
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                Maze[y, x] = new Polje(FieldType.Path, false);

        Maze[0, 0].Tip = FieldType.Start;

        // Vise ciljeva (Zadatak 6)
        Goals.Clear();
        Goals.Add(new Point(7, 2));
        Goals.Add(new Point(0, 9));
        Goals.Add(new Point(4, 5));
        Goals.Add(new Point(8, 5));
        foreach (var g in Goals)
            Maze[g.Y, g.X].Tip = FieldType.Goal;

        for (int x = 2; x <= 7; x++)
            Maze[3, x].Tip = FieldType.Wall;
        for (int y = 1; y <= 6; y++)
            Maze[y, 6].Tip = FieldType.Wall;

        Console.WriteLine($"[UDP Server1] Maze generated (10x10). Start=(0,0), Goals={string.Join(",", Goals)}");
        RenderMaze();
    }

    private static Okolina GetOkolina(int x, int y)
    {
        var ok = new Okolina();

        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = x + dx;
                int gy = y + dy;

                Polje p;
                if (!InBounds(gx, gy))
                {
                    p = new Polje(FieldType.Wall, false);
                }
                else
                {
                    var src = Maze[gy, gx];
                    p = new Polje(src.Tip, src.Zauzeto);
                }

                ok.Mapa[dy + 1, dx + 1] = p;
            }

        return ok;
    }

    private static bool InBounds(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;

    private static void RenderMaze()
    {
        // Grid sa zidovima (#), prolazima (.), ciljevima (X) i jedinkama (A,B,C...)
        var grid = new char[H, W];

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var f = Maze[y, x];
                grid[y, x] = f.Tip switch
                {
                    FieldType.Wall => '#',
                    FieldType.Goal => 'X',
                    _ => '.'
                };
            }

        foreach (var kv in Jedinke)
        {
            int id = kv.Key;
            int x = kv.Value.x;
            int y = kv.Value.y;
            if (!InBounds(x, y)) continue;
            if (Maze[y, x].Tip == FieldType.Wall) continue;

            char letter = (char)('A' + (id - 1) % 26);
            grid[y, x] = letter;
        }

        Console.SetCursorPosition(0, 0);
        Console.WriteLine("[UDP Server1] Lavirint (# zid, . prolaz, X cilj, A/B/... jedinke)");
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
                Console.Write(grid[y, x]);
            Console.WriteLine();
        }
        Console.WriteLine();
    }
}
