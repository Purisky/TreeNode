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
            
            // 查找JsonNode基类
            INamedTypeSymbol jsonNodeBaseType = null;
            
            foreach (var candidateClass in receiver.CandidateClasses)
            {
                var model = compilation.GetSemanticModel(candidateClass.SyntaxTree);
                if (model.GetDeclaredSymbol(candidateClass) is INamedTypeSymbol typeSymbol)
                {
                    // 检查是否是JsonNode基类
                    if (typeSymbol.Name == "JsonNode" && typeSymbol.ContainingNamespace.ToDisplayString() == "TreeNode.Runtime")
                    {
                        jsonNodeBaseType = typeSymbol;
                        Debug.Log($"----Found JsonNode base type: {jsonNodeBaseType.ToDisplayString()}");
                    }
                    
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
            
            // 生成JsonNode的IPropertyAccessor实现
            if (jsonNodeBaseType != null && SymbolEqualityComparer.Default.Equals(jsonNodeBaseType.ContainingAssembly, currentAssembly))
            {
                Debug.Log($"Generating JsonNode property accessor for {jsonNodeBaseType.ToDisplayString()}");
                var jsonNodePropertyAccessorSource = GenerateJsonNodePropertyAccessor(jsonNodeBaseType);
                context.AddSource("JsonNode.PropertyAccessor.g.cs", SourceText.From(jsonNodePropertyAccessorSource, Encoding.UTF8));
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

        private string GenerateJsonNodePropertyAccessor(INamedTypeSymbol jsonNodeType)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine($"namespace {jsonNodeType.ContainingNamespace.ToDisplayString()}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {jsonNodeType.Name} : IPropertyAccessor");
            sb.AppendLine("    {");
            
            // 查找IPropertyAccessor接口
            var compilation = jsonNodeType.ContainingAssembly.GetTypeByMetadataName("TreeNode.Runtime.IPropertyAccessor")
                ?? FindIPropertyAccessorInterface(jsonNodeType);
            
            if (compilation != null)
            {
                // 生成接口方法的virtual实现
                foreach (var member in compilation.GetMembers())
                {
                    if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
                    {
                        GenerateVirtualMethodImplementation(sb, method);
                    }
                }
            }
            
            sb.AppendLine("    }");
            sb.AppendLine("}");
            
            return sb.ToString();
        }

        private INamedTypeSymbol FindIPropertyAccessorInterface(INamedTypeSymbol contextType)
        {
            // 在编译上下文中查找IPropertyAccessor接口
            var allTypes = contextType.ContainingAssembly.GlobalNamespace.GetNamespaceMembers()
                .SelectMany(ns => GetAllTypes(ns))
                .OfType<INamedTypeSymbol>();
            
            return allTypes.FirstOrDefault(t => 
                t.TypeKind == TypeKind.Interface && 
                t.Name == "IPropertyAccessor" && 
                t.ContainingNamespace.ToDisplayString() == "TreeNode.Runtime");
        }

        private IEnumerable<INamespaceOrTypeSymbol> GetAllTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;
            
            foreach (var childNs in ns.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(childNs))
                    yield return type;
            }
        }

        private void GenerateVirtualMethodImplementation(StringBuilder sb, IMethodSymbol method)
        {
            var returnType = method.ReturnType.ToDisplayString();
            var methodName = method.Name;
            
            // 处理泛型方法
            var genericConstraints = "";
            if (method.IsGenericMethod)
            {
                var typeParameters = string.Join(", ", method.TypeParameters.Select(tp => tp.Name));
                methodName += $"<{typeParameters}>";
                
                // 生成泛型约束
                var constraints = new List<string>();
                foreach (var typeParam in method.TypeParameters)
                {
                    var constraintParts = new List<string>();
                    
                    if (typeParam.HasReferenceTypeConstraint)
                        constraintParts.Add("class");
                    if (typeParam.HasValueTypeConstraint)
                        constraintParts.Add("struct");
                    if (typeParam.HasUnmanagedTypeConstraint)
                        constraintParts.Add("unmanaged");
                    if (typeParam.HasNotNullConstraint)
                        constraintParts.Add("notnull");
                    
                    // 添加类型约束
                    foreach (var constraintType in typeParam.ConstraintTypes)
                    {
                        constraintParts.Add(constraintType.ToDisplayString());
                    }
                    
                    if (typeParam.HasConstructorConstraint)
                        constraintParts.Add("new()");
                    
                    if (constraintParts.Count > 0)
                    {
                        constraints.Add($"where {typeParam.Name} : {string.Join(", ", constraintParts)}");
                    }
                }
                
                if (constraints.Count > 0)
                {
                    genericConstraints = $"\n            {string.Join("\n            ", constraints)}";
                }
            }
            
            // 处理参数，包括ref、out、in等修饰符
            var parameters = string.Join(", ", method.Parameters.Select(p => 
            {
                var refKind = p.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => ""
                };
                
                var paramType = p.Type.ToDisplayString();
                var paramName = p.Name;
                
                // 处理params参数
                var paramsModifier = p.IsParams ? "params " : "";
                
                return $"{paramsModifier}{refKind}{paramType} {paramName}";
            }));
            
            sb.AppendLine($"        public virtual {returnType} {methodName}({parameters}){genericConstraints}=>throw new NotImplementedException($\"Method '{methodName}' is not implemented in {{GetType().Name}}\");");

        }

        private string GetDefaultValue(ITypeSymbol type)
        {
            if (type.IsReferenceType || type.CanBeReferencedByName && type.Name == "Nullable")
                return "null";
            
            return type.SpecialType switch
            {
                SpecialType.System_Boolean => "false",
                SpecialType.System_Byte or SpecialType.System_SByte or 
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 => "0",
                SpecialType.System_Single or SpecialType.System_Double or 
                SpecialType.System_Decimal => "0",
                SpecialType.System_Char => "'\\0'",
                _ => type.IsValueType ? $"default({type.ToDisplayString()})" : "null"
            };
        }

        // ...existing code...
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
