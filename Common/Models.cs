using System;
using System.Collections.Generic;

namespace Common;

[Serializable]
public sealed class Point
{
    public int X { get; set; }
    public int Y { get; set; }

    public Point() { }
    public Point(int x, int y) { X = x; Y = y; }

    public override string ToString() => $"({X},{Y})";
}

[Serializable]
public enum FieldType
{
    Start = 0,
    Wall = 1,
    Goal = 2,
    Path = 3
}

[Serializable]
public sealed class Polje
{
    public FieldType Tip { get; set; }
    public bool Zauzeto { get; set; }

    public Polje() { }

    public Polje(FieldType tip, bool zauzeto = false)
    {
        Tip = tip;
        Zauzeto = zauzeto;
    }
}

[Serializable]
public sealed class Komanda
{
    // Pomak po X osi: -1, 0, 1
    public int X { get; set; }
    // Pomak po Y osi: -1, 0, 1
    public int Y { get; set; }

    public Komanda() { }

    public Komanda(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X},{Y})";
}

[Serializable]
public sealed class Okolina
{
    public Polje[,] Mapa { get; set; } = new Polje[3, 3];

    public Okolina()
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Mapa[i, j] = new Polje(FieldType.Path, false);
    }
}

[Serializable]
public sealed class UdpRequest
{
    public int JedinkaId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

[Serializable]
public sealed class UdpResponse
{
    public Okolina Okolina { get; set; } = new Okolina();
    public bool IsGoalHere { get; set; }
    // Lista svih ciljeva u lavirintu (globalno)
    public List<Point> Goals { get; set; } = new();
}

public enum TcpMsgType : int
{
    Hello = 1,
    State = 2,
    Command = 3,
    Finished = 4
}

[Serializable]
public sealed class TcpEnvelope
{
    public TcpMsgType Type { get; set; }
    public object Payload { get; set; } = new object();
}

[Serializable]
public sealed class HelloPayload
{
    public int JedinkaId { get; set; }
    public int StartX { get; set; }
    public int StartY { get; set; }
}

[Serializable]
public sealed class StatePayload
{
    public int JedinkaId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public List<Komanda> MoguciPravci { get; set; } = new();

    // Poslednja okolina koju je klijent dobio od UDP servera (Server1).
    // Server2 moze da koristi ovo da gradi svoj "prazan" lavirint (known map).
    public Okolina? Okolina { get; set; }

    // Klijent detektuje cilj preko UDP odgovora i prijavljuje Server2.
    public bool GoalFound { get; set; }
    public int GoalX { get; set; }
    public int GoalY { get; set; }

    public List<Point>? KnownGoals { get; set; }
}

[Serializable]
public sealed class CommandPayload
{
    public int JedinkaId { get; set; }
    public Komanda Move { get; set; } = new Komanda(0, 0);
    public bool Finished { get; set; }
    public string? Note { get; set; }
}
