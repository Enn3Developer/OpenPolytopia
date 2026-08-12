namespace OpenPolytopia.Common.Network;

using System.Text;

// Primitive (de)serialization extensions.
// Everything is written in network byte order (big-endian).
// Every Deserialize increments the index by the number of bytes it consumed.

public static class BoolSerialization {
  public static void Serialize(this bool value, List<byte> bytes) => bytes.Add((byte)value.ToUInt());

  public static void Deserialize(this ref bool value, byte[] bytes, ref uint index) => value = bytes[index++] == 1;
}

public static class ByteSerialization {
  public static void Serialize(this byte value, List<byte> bytes) => bytes.Add(value);

  public static void Deserialize(this ref byte value, byte[] bytes, ref uint index) => value = bytes[index++];
}

public static class UIntSerialization {
  public static void Serialize(this uint value, List<byte> bytes) {
    bytes.Add((byte)(value >> 24));
    bytes.Add((byte)(value >> 16));
    bytes.Add((byte)(value >> 8));
    bytes.Add((byte)value);
  }

  public static byte[] Serialize(this uint value) => [
    (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
  ];

  public static void Deserialize(this ref uint value, byte[] bytes, ref uint index) {
    value = ((uint)bytes[index] << 24) | ((uint)bytes[index + 1] << 16) | ((uint)bytes[index + 2] << 8) |
            bytes[index + 3];
    index += 4;
  }

  public static uint Read(byte[] bytes, ref uint index) {
    var value = 0u;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class IntSerialization {
  public static void Serialize(this int value, List<byte> bytes) => ((uint)value).Serialize(bytes);

  public static void Deserialize(this ref int value, byte[] bytes, ref uint index) {
    var unsigned = 0u;
    unsigned.Deserialize(bytes, ref index);
    value = (int)unsigned;
  }
}

public static class ULongSerialization {
  public static void Serialize(this ulong value, List<byte> bytes) {
    bytes.Add((byte)(value >> 56));
    bytes.Add((byte)(value >> 48));
    bytes.Add((byte)(value >> 40));
    bytes.Add((byte)(value >> 32));
    bytes.Add((byte)(value >> 24));
    bytes.Add((byte)(value >> 16));
    bytes.Add((byte)(value >> 8));
    bytes.Add((byte)value);
  }

  public static void Deserialize(this ref ulong value, byte[] bytes, ref uint index) {
    value = 0;
    for (var i = 0; i < 8; i++) {
      value = (value << 8) | bytes[index++];
    }
  }

  public static ulong Read(byte[] bytes, ref uint index) {
    var value = 0ul;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class StringSerialization {
  public static void Serialize(this string value, List<byte> bytes) {
    var encoded = Encoding.UTF8.GetBytes(value);
    ((uint)encoded.Length).Serialize(bytes);
    bytes.AddRange(encoded);
  }

  public static string Read(byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);
    var value = Encoding.UTF8.GetString(bytes, (int)index, (int)length);
    index += length;
    return value;
  }
}

public static class ListSerialization {
  public static void Serialize<T>(this List<T> list, List<byte> bytes) where T : INetworkSerializable {
    ((uint)list.Count).Serialize(bytes);
    foreach (var element in list) {
      element.Serialize(bytes);
    }
  }

  public static void Deserialize<T>(this List<T> list, byte[] bytes, ref uint index)
    where T : INetworkSerializable, new() {
    var length = UIntSerialization.Read(bytes, ref index);

    for (var i = 0; i < length; i++) {
      var value = new T();
      value.Deserialize(bytes, ref index);
      list.Add(value);
    }
  }

  public static void Serialize(this List<uint> list, List<byte> bytes) {
    ((uint)list.Count).Serialize(bytes);
    foreach (var element in list) {
      element.Serialize(bytes);
    }
  }

  public static void Deserialize(this List<uint> list, byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);

    for (var i = 0; i < length; i++) {
      list.Add(UIntSerialization.Read(bytes, ref index));
    }
  }

  public static void Serialize(this List<string> list, List<byte> bytes) {
    ((uint)list.Count).Serialize(bytes);
    foreach (var element in list) {
      element.Serialize(bytes);
    }
  }

  public static void Deserialize(this List<string> list, byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);

    for (var i = 0; i < length; i++) {
      list.Add(StringSerialization.Read(bytes, ref index));
    }
  }
}
