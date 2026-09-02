namespace OpenPolytopia.Common;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads and writes an enum as its camelCase name, the convention of the json resources
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumConverter{TEnum}"/> ignores <see cref="System.Runtime.Serialization.EnumMemberAttribute"/>,
/// so without a naming policy the members would be written with their C# names (<c>MindBender</c> instead of
/// <c>mindBender</c>); reading stays case-insensitive
/// </remarks>
/// <typeparam name="TEnum">the enum to convert</typeparam>
public class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
  where TEnum : struct, Enum;
