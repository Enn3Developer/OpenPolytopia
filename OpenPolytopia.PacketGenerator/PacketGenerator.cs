namespace OpenPolytopia.PacketGenerator;

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public sealed class PacketGenerator : IIncrementalGenerator {
  private const string AttributeName = "OpenPolytopia.Common.Network.Packets.PacketFieldAttribute";
  private const string GeneratedAttributeName = "OpenPolytopia.Common.Network.Packets.GeneratedPacketAttribute";
  private const string PacketInterfaceName = "OpenPolytopia.Common.Network.Packets.IPacket";
  private const string SerializableInterfaceName = "OpenPolytopia.Common.Network.INetworkSerializable";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var generatedPackets = context.SyntaxProvider.ForAttributeWithMetadataName(
      GeneratedAttributeName,
      static (node, _) => node is ClassDeclarationSyntax,
      static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);

    var members = context.SyntaxProvider.ForAttributeWithMetadataName(
      AttributeName,
      static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
      static (attributeContext, _) => (ISymbol)attributeContext.TargetSymbol);

    context.RegisterSourceOutput(
      context.CompilationProvider.Combine(generatedPackets.Collect()).Combine(members.Collect()),
      static (productionContext, input) =>
        Execute(input.Left.Left, input.Left.Right, input.Right, productionContext));
  }

  private static void Execute(
    Compilation compilation,
    ImmutableArray<INamedTypeSymbol> generatedPackets,
    ImmutableArray<ISymbol> attributedMembers,
    SourceProductionContext context) {
    if (generatedPackets.IsDefaultOrEmpty && attributedMembers.IsDefaultOrEmpty) return;

    var packetInterface = compilation.GetTypeByMetadataName(PacketInterfaceName);
    var serializableInterface = compilation.GetTypeByMetadataName(SerializableInterfaceName);
    if (packetInterface is null || serializableInterface is null) return;

    var generatedPacketSet = new HashSet<INamedTypeSymbol>(generatedPackets, SymbolEqualityComparer.Default);
    foreach (var member in attributedMembers) {
      if (member.ContainingType is not null && !generatedPacketSet.Contains(member.ContainingType)) {
        context.ReportDiagnostic(Diagnostic.Create(
          Diagnostics.GeneratedPacketRequired,
          MemberLocation(member),
          member.Name,
          member.ContainingType.ToDisplayString()));
      }
    }

    foreach (var packet in generatedPackets
               .OrderBy(static packet => packet.ToDisplayString(), StringComparer.Ordinal)) {
      var members = attributedMembers
        .Where(member => SymbolEqualityComparer.Default.Equals(member.ContainingType, packet))
        .Distinct(SymbolEqualityComparer.Default)
        .OrderBy(static member => member.Locations.FirstOrDefault()?.SourceTree?.FilePath, StringComparer.Ordinal)
        .ThenBy(static member => member.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
        .ToArray();

      if (!Implements(packet, packetInterface)) {
        context.ReportDiagnostic(Diagnostic.Create(
          Diagnostics.PacketOnly,
          TypeLocation(packet),
          packet.ToDisplayString()));
        continue;
      }

      var manualSerialize = FindManualMethod(packet, "Serialize", isDeserialize: false);
      var manualDeserialize = FindManualMethod(packet, "Deserialize", isDeserialize: true);
      if (members.Length > 0 && manualSerialize is not null) {
        context.ReportDiagnostic(Diagnostic.Create(
          Diagnostics.ManualImplementation,
          MethodLocation(manualSerialize),
          packet.Name,
          "Serialize"));
      }
      if (members.Length > 0 && manualDeserialize is not null) {
        context.ReportDiagnostic(Diagnostic.Create(
          Diagnostics.ManualImplementation,
          MethodLocation(manualDeserialize),
          packet.Name,
          "Deserialize"));
      }

      if (!IsPartial(packet)) {
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.PartialRequired, TypeLocation(packet), packet.Name));
        continue;
      }

      if (packet.TypeKind != TypeKind.Class || packet.ContainingType is not null || packet.TypeParameters.Length != 0) {
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SimpleClassRequired, TypeLocation(packet), packet.Name));
        continue;
      }

      var memberPlans = new List<MemberPlan>();
      var hasErrors = false;
      foreach (var member in members) {
        if (!IsWritable(member)) {
          context.ReportDiagnostic(Diagnostic.Create(Diagnostics.WritableRequired, MemberLocation(member), member.Name));
          hasErrors = true;
          continue;
        }

        var type = GetMemberType(member);
        var strategy = GetStrategy(type, serializableInterface);
        if (strategy is null) {
          context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.UnsupportedType,
            MemberLocation(member),
            member.Name,
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
          hasErrors = true;
          continue;
        }

        memberPlans.Add(new MemberPlan(Escape(member.Name), strategy));
      }

      if (hasErrors || manualSerialize is not null && manualDeserialize is not null) continue;

      var source = Render(packet, memberPlans, manualSerialize is null, manualDeserialize is null);
      context.AddSource(GetHintName(packet), SourceText.From(source, Encoding.UTF8));
    }
  }

  private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol interfaceType) =>
    type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, interfaceType));

  private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences
    .Select(reference => reference.GetSyntax())
    .OfType<ClassDeclarationSyntax>()
    .All(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

  private static bool IsWritable(ISymbol member) => member switch {
    IFieldSymbol field => !field.IsStatic && !field.IsConst && !field.IsReadOnly,
    IPropertySymbol property => !property.IsStatic &&
                                !property.IsIndexer &&
                                property.GetMethod is not null &&
                                property.SetMethod is { IsInitOnly: false },
    _ => false,
  };

  private static ITypeSymbol GetMemberType(ISymbol member) => member switch {
    IFieldSymbol field => field.Type,
    IPropertySymbol property => property.Type,
    _ => throw new InvalidOperationException("PacketField can only target a field or property"),
  };

  private static SerializationStrategy? GetStrategy(
    ITypeSymbol type,
    INamedTypeSymbol serializableInterface) {
    if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } enumType) {
      return GetEnumStrategy(enumType, underlyingType);
    }

    var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return fullName switch {
      "bool" => SerializationStrategy.Read("BoolSerialization"),
      "byte" => SerializationStrategy.Read("ByteSerialization"),
      "sbyte" => SerializationStrategy.Read("SByteSerialization"),
      "short" => SerializationStrategy.Read("ShortSerialization"),
      "ushort" => SerializationStrategy.Read("UShortSerialization"),
      "uint" => SerializationStrategy.Read("UIntSerialization"),
      "ulong" => SerializationStrategy.Read("ULongSerialization"),
      "int" => SerializationStrategy.Read("IntSerialization"),
      "long" => SerializationStrategy.Read("LongSerialization"),
      "string" => SerializationStrategy.Read("StringSerialization"),
      "global::Godot.Vector2I" => SerializationStrategy.Read("Vector2ISerialization"),
      "uint[]" => SerializationStrategy.Read("UIntArraySerialization"),
      "ulong[]" => SerializationStrategy.Read("ULongArraySerialization"),
      "global::System.Collections.Generic.List<string>" => SerializationStrategy.Mutate("StringListSerialization"),
      _ => GetCompositeStrategy(type, serializableInterface),
    };
  }

  private static SerializationStrategy? GetEnumStrategy(INamedTypeSymbol enumType, INamedTypeSymbol underlyingType) {
    var helper = underlyingType.SpecialType switch {
      SpecialType.System_Byte => "ByteSerialization",
      SpecialType.System_SByte => "SByteSerialization",
      SpecialType.System_Int16 => "ShortSerialization",
      SpecialType.System_UInt16 => "UShortSerialization",
      SpecialType.System_Int32 => "IntSerialization",
      SpecialType.System_UInt32 => "UIntSerialization",
      SpecialType.System_Int64 => "LongSerialization",
      SpecialType.System_UInt64 => "ULongSerialization",
      _ => null,
    };

    return helper is null
      ? null
      : SerializationStrategy.Enum(
        helper,
        underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
  }

  private static SerializationStrategy? GetCompositeStrategy(
    ITypeSymbol type,
    INamedTypeSymbol serializableInterface) {
    if (type is INamedTypeSymbol { IsGenericType: true } named &&
        named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
        "global::System.Collections.Generic.List<T>" &&
        ImplementsSerializable(named.TypeArguments[0], serializableInterface) &&
        HasUsableParameterlessConstructor(named.TypeArguments[0])) {
      return SerializationStrategy.Mutate("ListSerialization");
    }

    return ImplementsSerializable(type, serializableInterface)
      ? SerializationStrategy.Serializable
      : null;
  }

  private static bool ImplementsSerializable(ITypeSymbol type, INamedTypeSymbol serializableInterface) =>
    SymbolEqualityComparer.Default.Equals(type, serializableInterface) ||
    type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, serializableInterface));

  private static bool HasUsableParameterlessConstructor(ITypeSymbol type) {
    if (type.IsValueType) return true;
    return type is INamedTypeSymbol named &&
           !named.IsAbstract &&
           named.InstanceConstructors.Any(constructor =>
             constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);
  }

  private static IMethodSymbol? FindManualMethod(
    INamedTypeSymbol packet,
    string name,
    bool isDeserialize) => packet.GetMembers()
    .OfType<IMethodSymbol>()
    .FirstOrDefault(method =>
      !method.IsStatic &&
      (method.Name == name || method.ExplicitInterfaceImplementations.Any(implementation => implementation.Name == name)) &&
      method.ReturnsVoid &&
      (!isDeserialize
        ? IsSerializeSignature(method)
        : IsDeserializeSignature(method)));

  private static bool IsSerializeSignature(IMethodSymbol method) {
    if (method.Parameters.Length != 1 || method.Parameters[0].RefKind != RefKind.None) return false;
    return method.Parameters[0].Type is INamedTypeSymbol { Name: "List", Arity: 1 } list &&
           list.TypeArguments[0].SpecialType == SpecialType.System_Byte;
  }

  private static bool IsDeserializeSignature(IMethodSymbol method) {
    if (method.Parameters.Length != 2) return false;
    return method.Parameters[0].RefKind == RefKind.None &&
           method.Parameters[0].Type is IArrayTypeSymbol {
             ElementType.SpecialType: SpecialType.System_Byte
           } &&
           method.Parameters[1].RefKind == RefKind.Ref &&
           method.Parameters[1].Type.SpecialType == SpecialType.System_UInt32;
  }

  private static string Render(
    INamedTypeSymbol packet,
    IReadOnlyList<MemberPlan> members,
    bool generateSerialize,
    bool generateDeserialize) {
    var generatedMembers = new List<MemberDeclarationSyntax>();
    if (generateSerialize) {
      generatedMembers.Add(CreateSerializeMethod(members));
    }
    if (generateDeserialize) {
      var statements = new List<StatementSyntax>();
      for (var i = 0; i < members.Count; i++) {
        statements.AddRange(members[i].Strategy.Deserialize(members[i].Name, i));
      }
      generatedMembers.Add(CreateDeserializeMethod(statements));
    }

    if (members.Any(static member => member.Strategy.IsSerializable)) {
      if (generateSerialize) generatedMembers.Add(CreateSerializableSerializeHelper());
      if (generateDeserialize) generatedMembers.Add(CreateSerializableDeserializeHelper());
    }

    var declaration = SyntaxFactory.ClassDeclaration(Escape(packet.Name))
      .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword))
      .AddMembers(generatedMembers.ToArray());

    MemberDeclarationSyntax rootMember = declaration;
    if (!packet.ContainingNamespace.IsGlobalNamespace) {
      rootMember = SyntaxFactory.FileScopedNamespaceDeclaration(
          SyntaxFactory.ParseName(packet.ContainingNamespace.ToDisplayString()))
        .AddMembers(declaration);
    }

    return SyntaxFactory.CompilationUnit()
      .AddMembers(rootMember)
      .WithLeadingTrivia(
        SyntaxFactory.Comment("// <auto-generated />"),
        SyntaxFactory.EndOfLine("\n"),
        SyntaxFactory.Trivia(SyntaxFactory.NullableDirectiveTrivia(
          SyntaxFactory.Token(SyntaxKind.EnableKeyword),
          true)),
        SyntaxFactory.EndOfLine("\n"))
      .NormalizeWhitespace(eol: "\n")
      .ToFullString();
  }

  private static MethodDeclarationSyntax CreateSerializeMethod(IReadOnlyList<MemberPlan> members) =>
    SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "Serialize")
      .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
      .AddParameterListParameters(Parameter("bytes", "global::System.Collections.Generic.List<byte>"))
      .WithBody(SyntaxFactory.Block(members.Select(member => member.Strategy.Serialize(member.Name))));

  private static MethodDeclarationSyntax CreateDeserializeMethod(IEnumerable<StatementSyntax> statements) =>
    SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "Deserialize")
      .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
      .AddParameterListParameters(
        Parameter("bytes", "byte[]"),
        Parameter("index", "uint").AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword)))
      .WithBody(SyntaxFactory.Block(statements));

  private static MethodDeclarationSyntax CreateSerializableSerializeHelper() =>
    CreateSerializableHelper("__SerializePacketField")
      .AddParameterListParameters(
        Parameter("value", "T"),
        Parameter("bytes", "global::System.Collections.Generic.List<byte>"))
      .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
        Invoke(Member("value", "Serialize"), SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")))))
      .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

  private static MethodDeclarationSyntax CreateSerializableDeserializeHelper() =>
    CreateSerializableHelper("__DeserializePacketField")
      .AddParameterListParameters(
        Parameter("value", "T").AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword)),
        Parameter("bytes", "byte[]"),
        Parameter("index", "uint").AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword)))
      .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
        Invoke(
          Member("value", "Deserialize"),
          SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")),
          RefArgument("index"))))
      .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

  private static MethodDeclarationSyntax CreateSerializableHelper(string name) =>
    SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), name)
      .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
      .AddTypeParameterListParameters(SyntaxFactory.TypeParameter("T"))
      .AddConstraintClauses(SyntaxFactory.TypeParameterConstraintClause("T")
        .AddConstraints(SyntaxFactory.TypeConstraint(
          SyntaxFactory.ParseTypeName("global::OpenPolytopia.Common.Network.INetworkSerializable"))));

  private static ParameterSyntax Parameter(string name, string type) =>
    SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)).WithType(SyntaxFactory.ParseTypeName(type));

  private static MemberAccessExpressionSyntax Member(string instance, string member) =>
    SyntaxFactory.MemberAccessExpression(
      SyntaxKind.SimpleMemberAccessExpression,
      SyntaxFactory.IdentifierName(instance),
      SyntaxFactory.IdentifierName(member));

  private static InvocationExpressionSyntax Invoke(ExpressionSyntax expression, params ArgumentSyntax[] arguments) =>
    SyntaxFactory.InvocationExpression(expression)
      .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

  private static ArgumentSyntax RefArgument(string name) => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name))
    .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));

  private static string GetHintName(INamedTypeSymbol packet) =>
    packet.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
      .Replace("global::", "")
      .Replace('<', '_')
      .Replace('>', '_') + ".Packet.g.cs";

  private static string Escape(string identifier) =>
    SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;

  private static Location? MemberLocation(ISymbol member) =>
    member.Locations.FirstOrDefault(static location => location.IsInSource);

  private static Location? TypeLocation(INamedTypeSymbol type) => type.DeclaringSyntaxReferences
    .FirstOrDefault()?.GetSyntax().GetLocation();

  private static Location? MethodLocation(IMethodSymbol method) => method.DeclaringSyntaxReferences
    .FirstOrDefault()?.GetSyntax().GetLocation() ?? MemberLocation(method);

  private sealed class MemberPlan {
    public MemberPlan(string name, SerializationStrategy strategy) {
      Name = name;
      Strategy = strategy;
    }

    public string Name { get; }
    public SerializationStrategy Strategy { get; }
  }

  private sealed class SerializationStrategy {
    private const string SerializationNamespace = "global::OpenPolytopia.Common.Network.";

    private SerializationStrategy(
      string? helper,
      StrategyKind kind,
      string? serializedType = null,
      string? declaredType = null) {
      Helper = helper;
      Kind = kind;
      SerializedType = serializedType;
      DeclaredType = declaredType;
    }

    public static SerializationStrategy Serializable { get; } = new(null, StrategyKind.Serializable);

    private string? Helper { get; }
    private StrategyKind Kind { get; }
    private string? SerializedType { get; }
    private string? DeclaredType { get; }
    public bool IsSerializable => Kind == StrategyKind.Serializable;

    public static SerializationStrategy Read(string helper) => new(helper, StrategyKind.Read);
    public static SerializationStrategy Mutate(string helper) => new(helper, StrategyKind.Mutate);
    public static SerializationStrategy Enum(string helper, string serializedType, string declaredType) =>
      new(helper, StrategyKind.Enum, serializedType, declaredType);

    public StatementSyntax Serialize(string member) {
      if (Kind == StrategyKind.Serializable) {
        return SyntaxFactory.ExpressionStatement(Invoke(
          SyntaxFactory.IdentifierName("__SerializePacketField"),
          SyntaxFactory.Argument(Member(member)),
          SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes"))));
      }

      ExpressionSyntax value = Member(member);
      if (Kind == StrategyKind.Enum) {
        value = SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(SerializedType!), value);
      }

      return SyntaxFactory.ExpressionStatement(Invoke(
        HelperMember("Serialize"),
        SyntaxFactory.Argument(value),
        SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes"))));
    }

    public IEnumerable<StatementSyntax> Deserialize(string member, int index) {
      switch (Kind) {
        case StrategyKind.Read:
          yield return Assignment(member, Invoke(
            HelperMember("Read"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")),
            RefArgument("index")));
          break;
        case StrategyKind.Enum:
          yield return Assignment(member, SyntaxFactory.CastExpression(
            SyntaxFactory.ParseTypeName(DeclaredType!),
            Invoke(
              HelperMember("Read"),
              SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")),
              RefArgument("index"))));
          break;
        case StrategyKind.Mutate:
          yield return SyntaxFactory.ExpressionStatement(Invoke(
            HelperMember("Deserialize"),
            SyntaxFactory.Argument(Member(member)),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")),
            RefArgument("index")));
          break;
        case StrategyKind.Serializable:
          var temporary = $"__packetField{index}";
          yield return SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
              .AddVariables(SyntaxFactory.VariableDeclarator(temporary)
                .WithInitializer(SyntaxFactory.EqualsValueClause(Member(member)))));
          yield return SyntaxFactory.ExpressionStatement(Invoke(
            SyntaxFactory.IdentifierName("__DeserializePacketField"),
            RefArgument(temporary),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("bytes")),
            RefArgument("index")));
          yield return Assignment(member, SyntaxFactory.IdentifierName(temporary));
          break;
      }
    }

    private MemberAccessExpressionSyntax HelperMember(string method) =>
      SyntaxFactory.MemberAccessExpression(
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxFactory.ParseName($"{SerializationNamespace}{Helper}"),
        SyntaxFactory.IdentifierName(method));

    private static MemberAccessExpressionSyntax Member(string member) =>
      SyntaxFactory.MemberAccessExpression(
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxFactory.ThisExpression(),
        SyntaxFactory.IdentifierName(member));

    private static ExpressionStatementSyntax Assignment(string member, ExpressionSyntax value) =>
      SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
        SyntaxKind.SimpleAssignmentExpression,
        Member(member),
        value));

    private enum StrategyKind {
      Read,
      Mutate,
      Serializable,
      Enum,
    }
  }
}
