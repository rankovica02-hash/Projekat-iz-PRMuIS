using System;
using System.Collections.Generic;

namespace Common;

public static class ByteUtil
{
    public static int ReadInt32LE(List<byte> buf, int offset)
    {
        if (offset < 0 || buf.Count < offset + 4)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return buf[offset]
             | (buf[offset + 1] << 8)
             | (buf[offset + 2] << 16)
             | (buf[offset + 3] << 24);
    }
}
