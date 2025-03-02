namespace OpenPolytopia;

using System.Runtime.CompilerServices;

/// <summary>
/// Delegate function that takes a ref parameter
/// </summary>
/// <typeparam name="T">type of the parameter</typeparam>
public delegate void ActionRef<T>(ref T item);

public static class BooleanExtensions {
  /// <summary>
  /// Converts the bool to a <see langword="ulong"/>
  /// </summary>
  /// <param name="value">the bool to convert</param>
  /// <returns>1 if value else 0</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static ulong ToULong(this bool value) => value ? 1ul : 0;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static uint ToUInt(this bool value) => value ? 1u : 0;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int ToInt(this bool value) => value ? 1 : 0;
}

public static class ULongExtensions {
  /// <summary>
  /// Clear the bits in the specified position by the specified length.
  /// Ex. value == 3 (...011); value.ClearBits(1, 1); value == 1 (...001)
  /// </summary>
  /// <param name="value">the number to clear bits</param>
  /// <param name="bits">number of bits to clear; must be all ones</param>
  /// <param name="position">the position where to start clearing bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static ulong ClearBits(this ulong value, ulong bits, int position) => value & ~(bits << position);

  /// <summary>
  /// Clear bits and set them
  /// </summary>
  /// <param name="value">the number where to set bits</param>
  /// <param name="data">the bits to be set</param>
  /// <param name="bits">number of bits to set; must be all ones</param>
  /// <param name="position">the position where to set bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void SetBits(this ref ulong value, ulong data, ulong bits, int position) =>
    value = value.ClearBits(bits, position) | (data << position);

  /// <summary>
  /// Get the bits
  /// </summary>
  /// <param name="value">the number where to get bits</param>
  /// <param name="bits">number of bits to get; must be all ones</param>
  /// <param name="position">the position where to get bits starting from the right</param>
  /// <returns></returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static ulong GetBits(this ulong value, ulong bits, int position) => (value >> position) & bits;
}

public static class UIntExtensions {
  /// <summary>
  /// Clear the bits in the specified position by the specified length.
  /// Ex. value == 3 (...011); value.ClearBits(1, 1); value == 1 (...001)
  /// </summary>
  /// <param name="value">the number to clear bits</param>
  /// <param name="bits">number of bits to clear; must be all ones</param>
  /// <param name="position">the position where to start clearing bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static uint ClearBits(this uint value, uint bits, int position) => value & ~(bits << position);

  /// <summary>
  /// Clear bits and set them
  /// </summary>
  /// <param name="value">the number where to set bits</param>
  /// <param name="data">the bits to be set</param>
  /// <param name="bits">number of bits to set; must be all ones</param>
  /// <param name="position">the position where to set bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void SetBits(this ref uint value, uint data, uint bits, int position) =>
    value = value.ClearBits(bits, position) | (data << position);

  /// <summary>
  /// Get the bits
  /// </summary>
  /// <param name="value">the number where to get bits</param>
  /// <param name="bits">number of bits to get; must be all ones</param>
  /// <param name="position">the position where to get bits starting from the right</param>
  /// <returns></returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static uint GetBits(this uint value, uint bits, int position) => (value >> position) & bits;
}

public static class IntExtensions {
  /// <summary>
  /// Clear the bits in the specified position by the specified length.
  /// Ex. value == 3 (...011); value.ClearBits(1, 1); value == 1 (...001)
  /// </summary>
  /// <param name="value">the number to clear bits</param>
  /// <param name="bits">number of bits to clear; must be all ones</param>
  /// <param name="position">the position where to start clearing bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int ClearBits(this int value, int bits, int position) => value & ~(bits << position);

  /// <summary>
  /// Clear bits and set them
  /// </summary>
  /// <param name="value">the number where to set bits</param>
  /// <param name="data">the bits to be set</param>
  /// <param name="bits">number of bits to set; must be all ones</param>
  /// <param name="position">the position where to set bits starting from the right</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void SetBits(this ref int value, int data, int bits, int position) =>
    value = value.ClearBits(bits, position) | (data << position);

  /// <summary>
  /// Get the bits
  /// </summary>
  /// <param name="value">the number where to get bits</param>
  /// <param name="bits">number of bits to get; must be all ones</param>
  /// <param name="position">the position where to get bits starting from the right</param>
  /// <returns></returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int GetBits(this int value, int bits, int position) => (value >> position) & bits;
}
