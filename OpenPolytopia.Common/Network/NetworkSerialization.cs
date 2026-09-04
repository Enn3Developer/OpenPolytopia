namespace OpenPolytopia.Common.Network;

using System.Text;
using Godot;

// Everything is written in network byte order (big-endian)
// Every Deserialize should increment the index

public static class BoolSerialization {
  public static void Serialize(this bool value, List<byte> bytes) => bytes.Add((byte)value.ToUInt());

  public static void Deserialize(this ref bool value, byte[] bytes, ref uint index) => value = bytes[index++] == 1;

  public static bool Read(byte[] bytes, ref uint index) {
    var value = false;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class ByteSerialization {
  public static void Serialize(this byte value, List<byte> bytes) => bytes.Add(value);

  public static void Deserialize(this ref byte value, byte[] bytes, ref uint index) => value = bytes[index++];

  public static byte Read(byte[] bytes, ref uint index) {
    var value = (byte)0;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class SByteSerialization {
  public static void Serialize(this sbyte value, List<byte> bytes) => bytes.Add(unchecked((byte)value));

  public static void Deserialize(this ref sbyte value, byte[] bytes, ref uint index) =>
    value = unchecked((sbyte)bytes[index++]);

  public static sbyte Read(byte[] bytes, ref uint index) {
    var value = (sbyte)0;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class ShortSerialization {
  public static void Serialize(this short value, List<byte> bytes) => unchecked((ushort)value).Serialize(bytes);

  public static void Deserialize(this ref short value, byte[] bytes, ref uint index) =>
    value = unchecked((short)UShortSerialization.Read(bytes, ref index));

  public static short Read(byte[] bytes, ref uint index) {
    var value = (short)0;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

public static class UShortSerialization {
  public static void Serialize(this ushort value, List<byte> bytes) {
    bytes.Add((byte)(value >> 8));
    bytes.Add((byte)value);
  }

  public static void Deserialize(this ref ushort value, byte[] bytes, ref uint index) {
    value = (ushort)((bytes[index] << 8) | bytes[index + 1]);
    index += 2;
  }

  public static ushort Read(byte[] bytes, ref uint index) {
    var value = (ushort)0;
    value.Deserialize(bytes, ref index);
    return value;
  }
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

public static class LongSerialization {
  public static void Serialize(this long value, List<byte> bytes) => unchecked((ulong)value).Serialize(bytes);

  public static void Deserialize(this ref long value, byte[] bytes, ref uint index) =>
    value = unchecked((long)ULongSerialization.Read(bytes, ref index));

  public static long Read(byte[] bytes, ref uint index) {
    var value = 0L;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

/// <summary>
/// Serialization helpers for <see langword="int"/>, big-endian, two's complement
/// </summary>
public static class IntSerialization {
  /// <summary>
  /// Serializes an <see langword="int"/> as 4 big-endian bytes
  /// </summary>
  /// <param name="value">the value to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this int value, List<byte> bytes) => ((uint)value).Serialize(bytes);

  /// <summary>
  /// Deserializes an <see langword="int"/> from 4 big-endian bytes
  /// </summary>
  /// <param name="value">the value to write the result into</param>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  public static void Deserialize(this ref int value, byte[] bytes, ref uint index) {
    var raw = 0u;
    raw.Deserialize(bytes, ref index);
    value = (int)raw;
  }

  /// <summary>
  /// Reads an <see langword="int"/> from the buffer
  /// </summary>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  /// <returns>the deserialized value</returns>
  public static int Read(byte[] bytes, ref uint index) {
    var value = 0;
    value.Deserialize(bytes, ref index);
    return value;
  }
}

/// <summary>
/// Serialization helpers for <see cref="Vector2I"/>, written as X then Y
/// </summary>
public static class Vector2ISerialization {
  /// <summary>
  /// Serializes a <see cref="Vector2I"/> as two big-endian ints, X then Y
  /// </summary>
  /// <param name="value">the value to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this Vector2I value, List<byte> bytes) {
    value.X.Serialize(bytes);
    value.Y.Serialize(bytes);
  }

  /// <summary>
  /// Deserializes a <see cref="Vector2I"/> from two big-endian ints, X then Y
  /// </summary>
  /// <param name="value">the value to write the result into</param>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  public static void Deserialize(this ref Vector2I value, byte[] bytes, ref uint index) {
    value.X = IntSerialization.Read(bytes, ref index);
    value.Y = IntSerialization.Read(bytes, ref index);
  }

  /// <summary>
  /// Reads a <see cref="Vector2I"/> from the buffer
  /// </summary>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  /// <returns>the deserialized value</returns>
  public static Vector2I Read(byte[] bytes, ref uint index) {
    var value = new Vector2I();
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

}

/// <summary>
/// Serialization helpers for <see langword="uint"/>[], as a <see langword="uint"/> length prefix followed by
/// the elements
/// </summary>
public static class UIntArraySerialization {
  /// <summary>
  /// Serializes a <see langword="uint"/> array
  /// </summary>
  /// <param name="values">the array to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this uint[] values, List<byte> bytes) {
    ((uint)values.Length).Serialize(bytes);
    foreach (var value in values) {
      value.Serialize(bytes);
    }
  }

  /// <summary>
  /// Reads a <see langword="uint"/> array from the buffer
  /// </summary>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  /// <returns>a freshly allocated array with the deserialized values</returns>
  public static uint[] Read(byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);
    // the length comes off the wire, so it must fit the buffer before anything gets allocated for it
    if (length > (bytes.Length - index) / sizeof(uint)) {
      throw new ArgumentOutOfRangeException(nameof(bytes), length, "the array length exceeds the remaining bytes");
    }

    var values = new uint[length];
    for (var i = 0; i < length; i++) {
      values[i] = UIntSerialization.Read(bytes, ref index);
    }

    return values;
  }
}

/// <summary>
/// Serialization helpers for <see langword="ulong"/>[], as a <see langword="uint"/> length prefix followed by
/// the elements
/// </summary>
public static class ULongArraySerialization {
  /// <summary>
  /// Serializes a <see langword="ulong"/> array
  /// </summary>
  /// <param name="values">the array to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this ulong[] values, List<byte> bytes) {
    ((uint)values.Length).Serialize(bytes);
    foreach (var value in values) {
      value.Serialize(bytes);
    }
  }

  /// <summary>
  /// Reads a <see langword="ulong"/> array from the buffer
  /// </summary>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  /// <returns>a freshly allocated array with the deserialized values</returns>
  public static ulong[] Read(byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);
    // the length comes off the wire, so it must fit the buffer before anything gets allocated for it
    if (length > (bytes.Length - index) / sizeof(ulong)) {
      throw new ArgumentOutOfRangeException(nameof(bytes), length, "the array length exceeds the remaining bytes");
    }

    var values = new ulong[length];
    for (var i = 0; i < length; i++) {
      values[i] = ULongSerialization.Read(bytes, ref index);
    }

    return values;
  }
}

/// <summary>
/// Serialization helpers for <see cref="List{T}"/> of <see langword="string"/>, since <see langword="string"/>
/// isn't <see cref="INetworkSerializable"/> and so can't use <see cref="ListSerialization"/>
/// </summary>
public static class StringListSerialization {
  /// <summary>
  /// Serializes a list of strings
  /// </summary>
  /// <param name="list">the list to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this List<string> list, List<byte> bytes) {
    ((uint)list.Count).Serialize(bytes);
    foreach (var value in list) {
      value.Serialize(bytes);
    }
  }

  /// <summary>
  /// Deserializes a list of strings, appending to any element already in the list
  /// </summary>
  /// <param name="list">the list to append the deserialized values into</param>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read bytes</param>
  public static void Deserialize(this List<string> list, byte[] bytes, ref uint index) {
    var length = UIntSerialization.Read(bytes, ref index);
    // every string costs at least its own length prefix, so a count that can't fit the buffer is bogus
    if (length > (bytes.Length - index) / sizeof(uint)) {
      throw new ArgumentOutOfRangeException(nameof(bytes), length, "the list length exceeds the remaining bytes");
    }

    for (var i = 0; i < length; i++) {
      list.Add(StringSerialization.Read(bytes, ref index));
    }
  }
}
