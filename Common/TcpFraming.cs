using System;
using System.Collections.Generic;

namespace Common;

public static class TcpFraming
{
    public const int MaxFrameLen = 10_000_000;

    public static byte[] BuildPacket(TcpEnvelope env)
    {
        byte[] payload = BinarySer.Serialize(env);
        byte[] packet = new byte[4 + payload.Length];
        byte[] lenBytes = BitConverter.GetBytes(payload.Length);
        Buffer.BlockCopy(lenBytes, 0, packet, 0, 4);
        Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);
        return packet;
    }
    public static bool TryExtractFrame(List<byte> rx, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (rx.Count < 4) return false;

        int len = ByteUtil.ReadInt32LE(rx, 0);
        if (len <= 0 || len > MaxFrameLen)
            throw new InvalidOperationException($"Bad frame length {len}");

        if (rx.Count < 4 + len) return false;

        payload = rx.GetRange(4, len).ToArray();
        rx.RemoveRange(0, 4 + len);
        return true;
    }
}
