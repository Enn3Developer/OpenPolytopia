namespace OpenPolytopia.Common.Network.Packets;

using System.Net.Sockets;

/// <summary>
/// Interface to declare a packet
/// </summary>
public interface IPacket {
  /// <summary>
  /// Serializes a packet to binary data
  /// </summary>
  /// <param name="bytes">the bytes to write to</param>
  public void Serialize(List<byte> bytes);

  /// <summary>
  /// Deserializes binary data to a packet
  /// </summary>
  /// <param name="bytes">the binary data to deserialize</param>
  public void Deserialize(byte[] bytes);
}

#region Extensions

// Every Deserialize should increment the index

public static class UIntExtension {
  private const int FIRST_BYTE = 0;
  private const int SECOND_BYTE = 8;
  private const int THIRD_BYTE = 16;
  private const int FOURTH_BYTE = 24;

  private const int EIGHT_BITS = 255;

  public static void Serialize(this uint value, List<byte> bytes) {
    bytes.Add((byte)value.GetBits(EIGHT_BITS, FOURTH_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, THIRD_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, SECOND_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, FIRST_BYTE));
  }

  public static byte[] Serialize(this uint value) {
    var bytes = new byte[4];
    bytes[0] = (byte)value.GetBits(EIGHT_BITS, FOURTH_BYTE);
    bytes[1] = (byte)value.GetBits(EIGHT_BITS, THIRD_BYTE);
    bytes[2] = (byte)value.GetBits(EIGHT_BITS, SECOND_BYTE);
    bytes[3] = (byte)value.GetBits(EIGHT_BITS, FIRST_BYTE);
    return bytes;
  }

  public static void Deserialize(this ref uint value, byte[] bytes, ref uint index) {
    value.SetBits(bytes[index++], EIGHT_BITS, FOURTH_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, THIRD_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, SECOND_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, FIRST_BYTE);
  }

  public static uint Deserialize(uint value, byte[] bytes, ref uint index) {
    value.SetBits(bytes[index++], EIGHT_BITS, FOURTH_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, THIRD_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, SECOND_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, FIRST_BYTE);
    return value;
  }
}

public static class IntExtension {
  private const int FIRST_BYTE = 0;
  private const int SECOND_BYTE = 8;
  private const int THIRD_BYTE = 16;
  private const int FOURTH_BYTE = 24;

  private const int EIGHT_BITS = 255;

  public static void Serialize(this int value, List<byte> bytes) {
    bytes.Add((byte)value.GetBits(EIGHT_BITS, FOURTH_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, THIRD_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, SECOND_BYTE));
    bytes.Add((byte)value.GetBits(EIGHT_BITS, FIRST_BYTE));
  }

  public static void Deserialize(this ref int value, byte[] bytes, ref uint index) {
    value.SetBits(bytes[index++], EIGHT_BITS, FOURTH_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, THIRD_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, SECOND_BYTE);
    value.SetBits(bytes[index++], EIGHT_BITS, FIRST_BYTE);
  }
}

public static class BoolExtension {
  public static void Serialize(this bool value, List<byte> bytes) => bytes.Add((byte)value.ToUInt());

  public static void Deserialize(this ref bool value, byte[] bytes, ref uint index) => value = bytes[index++] == 1;
}

public static class StringExtension {
  public static void Serialize(this string str, List<byte> bytes) {
    str.Length.Serialize(bytes);
    bytes.AddRange(str.Select(c => (byte)c));
  }

  public static string Deserialize(this string str, byte[] bytes, ref uint index) {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      str = str.Insert(i, ((char)bytes[index++]).ToString());
    }

    return str;
  }
}

public static class ListExtension {
  public static void Serialize<T>(this List<T> list, List<byte> bytes) {
    list.Count.Serialize(bytes);
    foreach (var element in list) {
      switch (element) {
        case uint uintValue:
          uintValue.Serialize(bytes);
          break;
        case int intValue:
          intValue.Serialize(bytes);
          break;
        case bool boolValue:
          boolValue.Serialize(bytes);
          break;
        case string str:
          str.Serialize(bytes);
          break;
        case INetworkSerializable networkSerializable:
          networkSerializable.Serialize(bytes);
          break;
      }
    }
  }

  public static void Deserialize(this List<uint> list, byte[] bytes, ref uint index) {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      var value = 0u;
      value.Deserialize(bytes, ref index);
      list.Add(value);
    }
  }

  public static void Deserialize(this List<int> list, byte[] bytes, ref uint index) {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      var value = 0;
      value.Deserialize(bytes, ref index);
      list.Add(value);
    }
  }

  public static void Deserialize(this List<bool> list, byte[] bytes, ref uint index) {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      var value = false;
      value.Deserialize(bytes, ref index);
      list.Add(value);
    }
  }

  public static void Deserialize(this List<string> list, byte[] bytes, ref uint index) {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      var str = "";
      str = str.Deserialize(bytes, ref index);
      list.Add(str);
    }
  }

  public static void Deserialize<T>(this List<T> list, byte[] bytes, ref uint index)
    where T : INetworkSerializable, new() {
    var length = UIntExtension.Deserialize(0u, bytes, ref index);

    for (var i = 0; i < length; i++) {
      var value = new T();
      value.Deserialize(bytes, ref index);
      list.Add(value);
    }
  }
}

public static class NetworkStreamExtension {
  public static void PreparePacket(this NetworkStream stream, IPacket packet, List<byte> bytes) {
    // get the packet id
    var id = PacketRegistrar.GetPacketId(packet);
    // compute the start index of the packet
    var startIndex = bytes.Count;
    // serialize the id
    id.Serialize(bytes);
    // serialize the packet
    packet.Serialize(bytes);
    // get the bytes count and serialize it to a temp byte array
    var contentLength = ((uint)bytes.Count).Serialize();
    // insert the temp byte array at the start of the bytes to send
    bytes.InsertRange(startIndex, contentLength);
  }

  public static async Task SendPacketsAsync(this NetworkStream stream, List<byte> bytes) {
    // send the bytes
    await stream.WriteAsync(bytes.ToArray());
    // clear the list
    bytes.Clear();
  }

  public static async Task WritePacketAsync(this NetworkStream stream, IPacket packet, List<byte> bytes) {
    PreparePacket(stream, packet, bytes);
    await SendPacketsAsync(stream, bytes);
  }
}

#endregion
