using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Common;

public static class BinarySer
{
    public static byte[] Serialize(object obj)
    {
#pragma warning disable SYSLIB0011
        using var ms = new MemoryStream();
        var bf = new BinaryFormatter();
        bf.Serialize(ms, obj);
        return ms.ToArray();
#pragma warning restore SYSLIB0011
    }

    public static T Deserialize<T>(byte[] data, int count)
    {
#pragma warning disable SYSLIB0011
        using var ms = new MemoryStream(data, 0, count);
        var bf = new BinaryFormatter();
        object o = bf.Deserialize(ms);
        return (T)o;
#pragma warning restore SYSLIB0011
    }
}
