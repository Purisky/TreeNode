using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TreeNodeSourceGenerator
{
    [Generator]
    public partial class NodeAccessorSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new JsonNodeSyntaxReceiver());


        }
        static Dictionary<INamedTypeSymbol, List<AccessibleMemberInfo>> TypeDict;
        public void Execute(GeneratorExecutionContext context)
        {
            string assemblyName = context.Compilation.Assembly.Name;
            if (assemblyName.StartsWith("Unity")) { return; }
            Debug.debugLogs.Clear();
            Debug.Log("NodeAccessorSourceGenerator.Execute called");
            if (context.SyntaxReceiver is not JsonNodeSyntaxReceiver receiver)
                return;
            var compilation = context.Compilation;
            var jsonNodeTypes = new List<INamedTypeSymbol>();
            var propertyAccessorTypes = new List<INamedTypeSymbol>();
            foreach (var candidateClass in receiver.CandidateClasses)
            {
                var model = compilation.GetSemanticModel(candidateClass.SyntaxTree);
                if (model.GetDeclaredSymbol(candidateClass) is INamedTypeSymbol typeSymbol)
                {
                    bool add = false;
                    if (IsJsonNodeDerived(typeSymbol))
                    {
                        jsonNodeTypes.Add(typeSymbol);
                        if (IsEligibleForPropertyAccessor(typeSymbol))
                        {
                            propertyAccessorTypes.Add(typeSymbol);
                            add = true;
                        }
                    }
                    if (!add && typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "GenIPropertyAccessorAttribute"))
                    {
                        propertyAccessorTypes.Add(typeSymbol);
                    }
                }
            }
            foreach (var candidateClass in receiver.CandidateStructs)
            {
                var model = compilation.GetSemanticModel(candidateClass.SyntaxTree);
                if (model.GetDeclaredSymbol(candidateClass) is INamedTypeSymbol typeSymbol)
                {
                    bool add = false;
                    if (IsJsonNodeDerived(typeSymbol))
                    {
                        jsonNodeTypes.Add(typeSymbol);
                        if (IsEligibleForPropertyAccessor(typeSymbol))
                        {
                            propertyAccessorTypes.Add(typeSymbol);
                            add = true;
                        }
                    }
                    if (!add && typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "GenIPropertyAccessorAttribute"))
                    {
                        propertyAccessorTypes.Add(typeSymbol);
                    }
                }
            }
            // 过滤类型，只处理本程序集的类型
            var currentAssembly = context.Compilation.Assembly;
            // 生成访问器类
            foreach (var nodeType in jsonNodeTypes)
            {
                var accessorSource = GenerateAccessorClass(nodeType);
                context.AddSource($"{nodeType.Name}Accessor.g.cs", SourceText.From(accessorSource, Encoding.UTF8));
            }

            TypeDict = new();

            List<INamedTypeSymbol> list = new(propertyAccessorTypes);
            HashSet<INamedTypeSymbol> newTypes = new();
            while (list.Count > 0)
            {
                var current = list[0];
                list.RemoveAt(0);
                if (TypeDict.ContainsKey(current)) continue;
                newTypes.Clear();
                List<AccessibleMemberInfo> accessibleMembers = AnalyzeAccessibleMembers(current, newTypes);
                TypeDict[current] = accessibleMembers;
                foreach (var type in newTypes)
                {
                    // 确保新发现的类型也是本程序集的
                    if (!TypeDict.ContainsKey(type) && !list.Contains(type))
                    {
                        Debug.Log(type.ToDisplayString());
                        list.Add(type);
                    }
                }
            }
            foreach (var item in TypeDict)
            {
                if (SymbolEqualityComparer.Default.Equals(item.Key.ContainingAssembly, currentAssembly))
                {
                    var propertyAccessorSource = GeneratePropertyAccessorPartialClass(item.Key, item.Value);
                    context.AddSource($"{item.Key.Name}.PropertyAccessor.g.cs", SourceText.From(propertyAccessorSource, Encoding.UTF8));
                }
            }
            // 生成注册器
            if (jsonNodeTypes.Count > 0)
            {
                var registrarSource = GenerateRegistrarClass(jsonNodeTypes);
                context.AddSource("GeneratedAccessorRegistrar.g.cs", SourceText.From(registrarSource, Encoding.UTF8));
            }
           Debug.GenerateDebugFile(context);
        }

        private bool IsEligibleForPropertyAccessor(INamedTypeSymbol typeSymbol)
        {
            return !typeSymbol.IsAbstract && IsPartial(typeSymbol);
        }

        private bool IsPartial(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .Where(syntax => syntax is ClassDeclarationSyntax || syntax is StructDeclarationSyntax)
                .Any(syntax => syntax.ChildTokens().Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
        }




        // 辅助方法
        private bool IsBuiltinValueType(ITypeSymbol type)
        {
            if (type == null) return false;

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_Char:
                case SpecialType.System_String:
                case SpecialType.System_DateTime:
                    return true;
                default:
                    break;
            }

            if (type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T)
            {
                if (type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
                {
                    return IsBuiltinValueType(namedType.TypeArguments[0]);
                }
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            return false;
        }

        private bool IsJsonNodeDerived(INamedTypeSymbol typeSymbol)
        {
            var current = typeSymbol.BaseType;
            while (current != null)
            {
                if (current.Name == "JsonNode" && current.ContainingNamespace.ToDisplayString() == "TreeNode.Runtime")
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        private bool IsJsonNodeType(ITypeSymbol type)
        {
            if (type == null) return false;

            var current = type;
            while (current != null)
            {
                if (current.Name == "JsonNode" && current.ContainingNamespace.ToDisplayString() == "TreeNode.Runtime")
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        private bool IsCollection(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Array)
                return true;

            if (type is INamedTypeSymbol namedType)
            {
                if (type.SpecialType == SpecialType.System_String)
                    return false;

                var interfaces = type.AllInterfaces;
                foreach (var interfaceType in interfaces)
                {
                    if (interfaceType.Name == "IEnumerable" && interfaceType.ContainingNamespace.ToDisplayString() == "System.Collections")
                        return true;
                }
            }

            return false;
        }

        private ITypeSymbol GetCollectionElementType(ITypeSymbol collectionType)
        {
            if (collectionType.TypeKind == TypeKind.Array && collectionType is IArrayTypeSymbol arrayType)
                return arrayType.ElementType;

            if (collectionType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var typeArgs = namedType.TypeArguments;
                if (typeArgs.Length > 0)
                    return typeArgs[0];
            }

            return null;
        }

        private bool IsUserDefinedType(ITypeSymbol type)
        {
            if (type == null || type.TypeKind == TypeKind.Enum || type.SpecialType != SpecialType.None)
                return false;

            var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? "";

            if (namespaceName.StartsWith("System") ||
                namespaceName.StartsWith("Unity") ||
                namespaceName.StartsWith("Microsoft") ||
                namespaceName.StartsWith("Newtonsoft"))
            {
                return false;
            }

            return true;
        }

        private bool IsStructCollection(ITypeSymbol type)
        {
            if (!IsCollection(type)) return false;

            var elementType = GetCollectionElementType(type);
            return elementType != null && elementType.IsValueType && !IsBuiltinValueType(elementType);
        }

        public static bool ImplementsIPropertyAccessor(ITypeSymbol type)
        {
            if (type == null) return false;
            var interfaces = type.AllInterfaces;
            return interfaces.Any(i => i.Name == "IPropertyAccessor" &&i.ContainingNamespace.ToDisplayString() == "TreeNode.Runtime");
        }

        private bool HasNestedAccessCapability(ITypeSymbol type)
        {
            return IsJsonNodeType(type) ||
                   ImplementsIPropertyAccessor(type) ||
                   IsUserDefinedType(type);
        }

    }

    internal class JsonNodeSyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> CandidateClasses { get; } = new List<ClassDeclarationSyntax>();
        public List<StructDeclarationSyntax> CandidateStructs { get; } = new List<StructDeclarationSyntax>();
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax classDeclaration)
            {
                CandidateClasses.Add(classDeclaration);
            }
            else if (syntaxNode is StructDeclarationSyntax structDeclaration)
            {
                CandidateStructs.Add(structDeclaration);
            }
        }
    }

    internal class AccessibleMemberInfo
    {
        public string Name { get; set; }
        public bool IsProperty { get; set; }
        public ITypeSymbol Type { get; set; }
        public bool IsValueType { get; set; }
        public bool IsJsonNodeType { get; set; }
        public bool ImplementsIPropertyAccessor => NodeAccessorSourceGenerator.ImplementsIPropertyAccessor(Type);
        public bool HasNestedAccess { get; set; }
        public bool IsCollection { get; set; }
        public ITypeSymbol ElementType { get; set; }
        public bool CanWrite { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsStructCollection { get; set; }
    }
}
