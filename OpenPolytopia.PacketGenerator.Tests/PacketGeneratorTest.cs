namespace OpenPolytopia.PacketGenerator.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public class PacketGeneratorTest {
  private const string Prelude = """
    using System;
    using System.Collections.Generic;

    namespace OpenPolytopia.Common.Network {
      public interface INetworkSerializable {
        void Serialize(List<byte> bytes);
        void Deserialize(byte[] bytes, ref uint index);
      }

      public static class StringSerialization {
        public static void Serialize(string value, List<byte> bytes) { }
        public static string Read(byte[] bytes, ref uint index) => "";
      }

      public static class UIntSerialization {
        public static void Serialize(uint value, List<byte> bytes) { }
        public static uint Read(byte[] bytes, ref uint index) => 0;
      }

      public static class ByteSerialization {
        public static void Serialize(byte value, List<byte> bytes) { }
        public static byte Read(byte[] bytes, ref uint index) => 0;
      }

      public static class SByteSerialization {
        public static void Serialize(sbyte value, List<byte> bytes) { }
        public static sbyte Read(byte[] bytes, ref uint index) => 0;
      }

      public static class ShortSerialization {
        public static void Serialize(short value, List<byte> bytes) { }
        public static short Read(byte[] bytes, ref uint index) => 0;
      }

      public static class UShortSerialization {
        public static void Serialize(ushort value, List<byte> bytes) { }
        public static ushort Read(byte[] bytes, ref uint index) => 0;
      }

      public static class IntSerialization {
        public static void Serialize(int value, List<byte> bytes) { }
        public static int Read(byte[] bytes, ref uint index) => 0;
      }

      public static class LongSerialization {
        public static void Serialize(long value, List<byte> bytes) { }
        public static long Read(byte[] bytes, ref uint index) => 0;
      }

      public static class ULongSerialization {
        public static void Serialize(ulong value, List<byte> bytes) { }
        public static ulong Read(byte[] bytes, ref uint index) => 0;
      }
    }

    namespace OpenPolytopia.Common.Network.Packets {
      [AttributeUsage(AttributeTargets.Class)]
      public sealed class GeneratedPacketAttribute : Attribute { }

      [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
      public sealed class PacketFieldAttribute : Attribute { }

      public interface IPacket : OpenPolytopia.Common.Network.INetworkSerializable { }
    }
    """;

  [Fact]
  public void GeneratesFieldsPropertiesAndSerializableMembersInDeclarationOrder() {
    var source = Prelude + """

      namespace Test {
        using OpenPolytopia.Common.Network;
        using OpenPolytopia.Common.Network.Packets;

        public sealed class Child : INetworkSerializable {
          public void Serialize(List<byte> bytes) { }
          public void Deserialize(byte[] bytes, ref uint index) { }
        }

        [GeneratedPacket]
        public partial class ExamplePacket : IPacket {
          [PacketField]
          public string Name = "";

          [PacketField]
          public uint Count { get; set; }

          [PacketField]
          public Child Child { get; set; } = new();
        }
      }
      """;

    var result = Run(source);
    var generated = Assert.Single(result.RunResult.Results).GeneratedSources.Single().SourceText.ToString();

    Assert.Contains("StringSerialization.Serialize(this.Name, bytes);", generated);
    Assert.Contains("UIntSerialization.Serialize(this.Count, bytes);", generated);
    Assert.Contains("__SerializePacketField(this.Child, bytes);", generated);
    Assert.True(generated.IndexOf("this.Name", StringComparison.Ordinal) <
                generated.IndexOf("this.Count", StringComparison.Ordinal));
    Assert.True(generated.IndexOf("this.Count", StringComparison.Ordinal) <
                generated.IndexOf("this.Child", StringComparison.Ordinal));
    Assert.DoesNotContain(result.OutputCompilation.GetDiagnostics(), diagnostic =>
      diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratesEveryEnumUnderlyingType() {
    var source = Prelude + """

      namespace Test {
        using OpenPolytopia.Common.Network.Packets;

        public enum ByteEnum : byte { Value }
        public enum SByteEnum : sbyte { Value }
        public enum ShortEnum : short { Value }
        public enum UShortEnum : ushort { Value }
        public enum IntEnum : int { Value }
        public enum UIntEnum : uint { Value }
        public enum LongEnum : long { Value }
        public enum ULongEnum : ulong { Value }

        [GeneratedPacket]
        public partial class EnumPacket : IPacket {
          [PacketField] public ByteEnum Byte;
          [PacketField] public SByteEnum SByte;
          [PacketField] public ShortEnum Short;
          [PacketField] public UShortEnum UShort;
          [PacketField] public IntEnum Int;
          [PacketField] public UIntEnum UInt;
          [PacketField] public LongEnum Long;
          [PacketField] public ULongEnum ULong { get; set; }
        }
      }
      """;

    var result = Run(source);
    var generated = Assert.Single(result.RunResult.Results).GeneratedSources.Single().SourceText.ToString();

    Assert.Empty(result.RunResult.Results.Single().Diagnostics);
    Assert.DoesNotContain(result.OutputCompilation.GetDiagnostics(), diagnostic =>
      diagnostic.Severity == DiagnosticSeverity.Error);
    Assert.Contains("ByteSerialization.Serialize((byte)this.Byte, bytes);", generated);
    Assert.Contains("SByteSerialization.Serialize((sbyte)this.SByte, bytes);", generated);
    Assert.Contains("ShortSerialization.Serialize((short)this.Short, bytes);", generated);
    Assert.Contains("UShortSerialization.Serialize((ushort)this.UShort, bytes);", generated);
    Assert.Contains("IntSerialization.Serialize((int)this.Int, bytes);", generated);
    Assert.Contains("UIntSerialization.Serialize((uint)this.UInt, bytes);", generated);
    Assert.Contains("LongSerialization.Serialize((long)this.Long, bytes);", generated);
    Assert.Contains("ULongSerialization.Serialize((ulong)this.ULong, bytes);", generated);
  }

  [Fact]
  public void GeneratesAnEmptyPacket() {
    var source = Prelude + """

      namespace Test {
        using OpenPolytopia.Common.Network.Packets;

        [GeneratedPacket]
        public partial class EmptyPacket : IPacket { }
      }
      """;

    var result = Run(source);
    var generated = Assert.Single(result.RunResult.Results).GeneratedSources.Single().SourceText.ToString();

    Assert.Contains("public void Serialize", generated);
    Assert.Contains("public void Deserialize", generated);
    Assert.DoesNotContain(result.OutputCompilation.GetDiagnostics(), diagnostic =>
      diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void ReportsInvalidPacketFieldsAndManualMethods() {
    var source = Prelude + """

      namespace Test {
        using OpenPolytopia.Common.Network.Packets;

        public class NotAPacket {
          [PacketField]
          public uint Value;
        }

        [GeneratedPacket]
        public partial class GeneratedNotPacket { }

        [GeneratedPacket]
        public class NotPartialPacket : IPacket {
          [PacketField]
          public uint Value;
        }

        [GeneratedPacket]
        public partial class UnsupportedPacket : IPacket {
          [PacketField]
          public object Value = new();
        }

        [GeneratedPacket]
        public partial class ReadonlyPacket : IPacket {
          [PacketField]
          public readonly uint Value;
        }

        [GeneratedPacket]
        public partial class ManualPacket : IPacket {
          [PacketField]
          public uint Value;

          public void Serialize(List<byte> bytes) { }
          public void Deserialize(byte[] bytes, ref uint index) { }
        }
      }
      """;

    var result = Run(source);
    var diagnostics = result.RunResult.Results.Single().Diagnostics;

    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "OPG001");
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "OPG002");
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "OPG003");
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "OPG004");
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "OPG007");
    Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "OPG005"));
    Assert.All(diagnostics.Where(diagnostic => diagnostic.Id == "OPG005"), diagnostic =>
      Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
  }

  private static GeneratorResult Run(string source) {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
      .Split(Path.PathSeparator)
      .Select(path => MetadataReference.CreateFromFile(path));
    var compilation = CSharpCompilation.Create(
      "GeneratorTests",
      [syntaxTree],
      references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    GeneratorDriver driver = CSharpGeneratorDriver.Create(new PacketGenerator());
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
    return new GeneratorResult(driver.GetRunResult(), outputCompilation);
  }

  private sealed record GeneratorResult(GeneratorDriverRunResult RunResult, Compilation OutputCompilation);
}
