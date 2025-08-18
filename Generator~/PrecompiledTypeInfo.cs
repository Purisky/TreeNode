using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using TreeNodeSourceGenerator;

namespace TreeNodeSourceGenerator
{
    /// <summary>
    /// 预编译 TypeReflectionInfo 生成器
    /// 专门负责生成编译期的类型反射信息，避免运行时反射和循环依赖
    /// </summary>
    public partial class NodeAccessorSourceGenerator
    {
        /// <summary>
        /// 生成预编译的 TypeReflectionInfo 初始化代码
        /// </summary>
        private void GeneratePrecompiledTypeReflectionInfo(StringBuilder sb, INamedTypeSymbol nodeType, string fullTypeName)
        {
            sb.AppendLine($"        private static readonly TypeReflectionInfo _typeInfo = InitializeTypeInfo();");
            sb.AppendLine("        private static TypeReflectionInfo InitializeTypeInfo()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var currentType = typeof({fullTypeName});");
            sb.AppendLine();
            sb.AppendLine("            return new TypeReflectionInfo");
            sb.AppendLine("            {");
            
            // 生成基础类型信息
            GenerateTypeBasicInfo(sb, nodeType, fullTypeName);
            
            // 生成成员信息
            GenerateTypeMembers(sb, nodeType);
            
            sb.AppendLine("            };");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// 生成类型基础信息
        /// </summary>
        private void GenerateTypeBasicInfo(StringBuilder sb, INamedTypeSymbol nodeType, string fullTypeName)
        {
            sb.AppendLine("                Type = currentType,");
            
            // 分析类型特性
            var isUserDefinedType = AnalyzeIsUserDefinedType(nodeType);
            var containsJsonNode = AnalyzeContainsJsonNode(nodeType);
            var mayContainNestedJsonNode = AnalyzeMayContainNestedJsonNode(nodeType);
            var hasParameterlessConstructor = AnalyzeHasParameterlessConstructor(nodeType);
            
            sb.AppendLine($"                IsUserDefinedType = {isUserDefinedType.ToString().ToLower()},");
            sb.AppendLine($"                ContainsJsonNode = {containsJsonNode.ToString().ToLower()},");
            sb.AppendLine($"                MayContainNestedJsonNode = {mayContainNestedJsonNode.ToString().ToLower()},");
            sb.AppendLine($"                HasParameterlessConstructor = {hasParameterlessConstructor.ToString().ToLower()},");
            
            // 生成构造函数委托
            if (hasParameterlessConstructor)
            {
                sb.AppendLine($"                Constructor = () => new {fullTypeName}(),");
            }
            else
            {
                sb.AppendLine("                Constructor = null,");
            }
            
            // 生成 Attribute 信息
            GenerateAttributeInfo(sb, nodeType);
        }

        /// <summary>
        /// 生成类型 Attribute 信息
        /// </summary>
        private void GenerateAttributeInfo(StringBuilder sb, INamedTypeSymbol nodeType)
        {
            // 生成 NodeInfo Attribute
            var nodeInfoAttr = GetAttribute(nodeType, "NodeInfoAttribute");
            if (nodeInfoAttr != null)
            {
                sb.AppendLine($"                NodeInfo = {GenerateAttributeInitializer(nodeInfoAttr)},");
            }

            // 生成 AssetFilter Attribute
            var assetFilterAttr = GetAttribute(nodeType, "AssetFilterAttribute");
            if (assetFilterAttr != null)
            {
                sb.AppendLine($"                AssetFilter = {GenerateAttributeInitializer(assetFilterAttr)},");
            }

            // 生成 PortColor Attribute
            var portColorAttr = GetAttribute(nodeType, "PortColorAttribute");
            if (portColorAttr != null)
            {
                sb.AppendLine($"                PortColor = {GenerateAttributeInitializer(portColorAttr)},");
            }
        }

        /// <summary>
        /// 获取指定名称的特性
        /// </summary>
        private AttributeData GetAttribute(INamedTypeSymbol type, string attributeName)
        {
            return type.GetAttributes().FirstOrDefault(attr => 
                attr.AttributeClass?.Name == attributeName ||
                attr.AttributeClass?.Name == attributeName.Replace("Attribute", ""));
        }

        /// <summary>
        /// 生成 Attribute 的初始化代码
        /// </summary>
        private string GenerateAttributeInitializer(AttributeData attributeData)
        {
            if (attributeData?.AttributeClass == null)
                return "null";

            var attributeTypeName = attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var sb = new StringBuilder();
            
            sb.Append($"new {attributeTypeName}()");
            
            // 检查是否有构造函数参数
            if (attributeData.ConstructorArguments.Length > 0)
            {
                sb.Clear();
                sb.Append($"new {attributeTypeName}(");
                
                for (int i = 0; i < attributeData.ConstructorArguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatAttributeValue(attributeData.ConstructorArguments[i]));
                }
                
                sb.Append(")");
            }
            
            // 检查是否有命名参数
            if (attributeData.NamedArguments.Length > 0)
            {
                sb.Append(" { ");
                
                for (int i = 0; i < attributeData.NamedArguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var namedArg = attributeData.NamedArguments[i];
                    sb.Append($"{namedArg.Key} = {FormatAttributeValue(namedArg.Value)}");
                }
                
                sb.Append(" }");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 格式化 Attribute 参数值
        /// </summary>
        private string FormatAttributeValue(TypedConstant value)
        {
            if (value.IsNull)
                return "null";
                
            switch (value.Kind)
            {
                case TypedConstantKind.Primitive:
                    if (value.Type?.SpecialType == SpecialType.System_String)
                        return $"\"{value.Value?.ToString().Replace("\"", "\\\"")}\"";
                    if (value.Type?.SpecialType == SpecialType.System_Char)
                        return $"'{value.Value}'";
                    if (value.Type?.SpecialType == SpecialType.System_Boolean)
                        return value.Value?.ToString().ToLower();
                    return value.Value?.ToString();
                    
                case TypedConstantKind.Enum:
                    var enumType = value.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return $"{enumType}.{value.Value}";
                    
                case TypedConstantKind.Type:
                    var typeValue = value.Value as ITypeSymbol;
                    return $"typeof({typeValue?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
                    
                case TypedConstantKind.Array:
                    var arrayValues = value.Values;
                    if (arrayValues.Length == 0)
                        return "new object[0]";
                    
                    var elementType = ((IArrayTypeSymbol)value.Type).ElementType;
                    var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    
                    var arrayContent = string.Join(", ", arrayValues.Select(FormatAttributeValue));
                    return $"new {elementTypeName}[] {{ {arrayContent} }}";
                    
                default:
                    return value.Value?.ToString() ?? "null";
            }
        }

        /// <summary>
        /// 生成成员 Attribute 信息
        /// </summary>
        private void GenerateMemberAttributeInfo(StringBuilder sb, AccessibleMemberInfo member, ISymbol memberSymbol)
        {
            // ShowInNodeAttribute
            var showInNodeAttr = GetMemberAttribute(memberSymbol, "ShowInNodeAttribute");
            if (showInNodeAttr != null)
            {
                sb.AppendLine($"                        ShowInNodeAttribute = {GenerateAttributeInitializer(showInNodeAttr)},");
            }

            // LabelInfoAttribute
            var labelInfoAttr = GetMemberAttribute(memberSymbol, "LabelInfoAttribute");
            if (labelInfoAttr != null)
            {
                sb.AppendLine($"                        LabelInfoAttribute = {GenerateAttributeInitializer(labelInfoAttr)},");
            }

            // StyleAttribute
            var styleAttr = GetMemberAttribute(memberSymbol, "StyleAttribute");
            if (styleAttr != null)
            {
                sb.AppendLine($"                        StyleAttribute = {GenerateAttributeInitializer(styleAttr)},");
            }

            // GroupAttribute
            var groupAttr = GetMemberAttribute(memberSymbol, "GroupAttribute");
            if (groupAttr != null)
            {
                sb.AppendLine($"                        GroupAttribute = {GenerateAttributeInitializer(groupAttr)},");
            }

            // OnChangeAttribute
            var onChangeAttr = GetMemberAttribute(memberSymbol, "OnChangeAttribute");
            if (onChangeAttr != null)
            {
                sb.AppendLine($"                        OnChangeAttribute = {GenerateAttributeInitializer(onChangeAttr)},");
            }

            // DropdownAttribute
            var dropdownAttr = GetMemberAttribute(memberSymbol, "DropdownAttribute");
            if (dropdownAttr != null)
            {
                sb.AppendLine($"                        DropdownAttribute = {GenerateAttributeInitializer(dropdownAttr)},");
            }

            // TitlePortAttribute
            var titlePortAttr = GetMemberAttribute(memberSymbol, "TitlePortAttribute");
            if (titlePortAttr != null)
            {
                sb.AppendLine($"                        TitlePortAttribute = {GenerateAttributeInitializer(titlePortAttr)},");
            }
        }

        /// <summary>
        /// 获取成员的指定特性
        /// </summary>
        private AttributeData GetMemberAttribute(ISymbol memberSymbol, string attributeName)
        {
            return memberSymbol?.GetAttributes().FirstOrDefault(attr => 
                attr.AttributeClass?.Name == attributeName ||
                attr.AttributeClass?.Name == attributeName.Replace("Attribute", ""));
        }

        /// <summary>
        /// 生成类型成员信息
        /// </summary>
        private void GenerateTypeMembers(StringBuilder sb, INamedTypeSymbol nodeType)
        {
            var members = AnalyzeAccessibleMembers(nodeType, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            var sortedMembers = members.OrderBy(m => CalculateRenderOrder(m)).ToList();
            
            // 生成 AllMembers 列表
            sb.AppendLine("                AllMembers = new List<UnifiedMemberInfo>");
            sb.AppendLine("                {");
            
            for (int i = 0; i < sortedMembers.Count; i++)
            {
                var member = sortedMembers[i];
                GenerateUnifiedMemberInfoInitializer(sb, member, nodeType);
                if (i < sortedMembers.Count - 1)
                {
                    sb.AppendLine(",");
                }
            }
            
            sb.AppendLine("                },");
            
            // 生成 MemberLookup 字典
            sb.AppendLine("                MemberLookup = new Dictionary<string, UnifiedMemberInfo>()");
        }

        /// <summary>
        /// 生成单个 UnifiedMemberInfo 的初始化代码
        /// </summary>
        private void GenerateUnifiedMemberInfoInitializer(StringBuilder sb, AccessibleMemberInfo member, INamedTypeSymbol ownerType)
        {
            var memberType = member.IsProperty ? "MemberType.Property" : "MemberType.Field";
            var category = DetermineMemberCategory(member);
            var renderOrder = CalculateRenderOrder(member);
            var groupName = GetGroupName(member);
            var memberValueTypeName = member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
            // 查找成员符号
            var memberSymbol = ownerType.GetMembers(member.Name).FirstOrDefault();
            
            sb.AppendLine("                    new UnifiedMemberInfo");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        Member = currentType.GetMember(\"{member.Name}\")[0],");
            sb.AppendLine($"                        MemberType = {memberType},");
            sb.AppendLine($"                        ValueType = typeof({memberValueTypeName}),");
            sb.AppendLine($"                        Category = MemberCategory.{category},");
            sb.AppendLine($"                        IsChild = {member.IsChild.ToString().ToLower()},");
            sb.AppendLine($"                        IsTitlePort = {member.IsTitlePort.ToString().ToLower()},");
            sb.AppendLine($"                        ShowInNode = {member.ShowInNode.ToString().ToLower()},");
            sb.AppendLine($"                        RenderOrder = {renderOrder},");
            sb.AppendLine($"                        GroupName = \"{groupName}\",");
            sb.AppendLine($"                        IsMultiValue = {member.IsCollection.ToString().ToLower()},");
            sb.AppendLine($"                        MayContainNestedStructure = {AnalyzeMayContainNestedStructure(member.Type).ToString().ToLower()},");
            sb.AppendLine($"                        MayContainNestedJsonNode = {AnalyzeMayContainNestedJsonNode(member.Type).ToString().ToLower()},");
            
            // 生成成员 Attribute 信息
            GenerateMemberAttributeInfo(sb, member, memberSymbol);
            
            // 生成Getter委托
            GenerateGetterInitializer(sb, member, ownerType);
            sb.AppendLine(",");
            
            // 生成Setter委托
            GenerateSetterInitializer(sb, member, ownerType);
            
            sb.Append("                    }");
        }

        /// <summary>
        /// 生成Getter委托的初始化代码
        /// </summary>
        private void GenerateGetterInitializer(StringBuilder sb, AccessibleMemberInfo member, INamedTypeSymbol ownerType)
        {
            var ownerTypeName = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
            sb.AppendLine("                        Getter = (obj) =>");
            sb.AppendLine("                        {");
            sb.AppendLine($"                            if (obj is {ownerTypeName} typedObj)");
            sb.AppendLine("                            {");
            sb.AppendLine($"                                return typedObj.{member.Name};");
            sb.AppendLine("                            }");
            sb.AppendLine($"                            throw new ArgumentException($\"Expected {ownerTypeName}, got {{obj?.GetType().Name ?? \"null\"}}\");");
            sb.AppendLine("                        }");
        }

        /// <summary>
        /// 生成Setter委托的初始化代码
        /// </summary>
        private void GenerateSetterInitializer(StringBuilder sb, AccessibleMemberInfo member, INamedTypeSymbol ownerType)
        {
            var ownerTypeName = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var memberValueTypeName = member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
            if (member.CanWrite && !member.IsReadOnly)
            {
                // 检查是否为结构体类型的属性，如果是则需要特殊处理
                bool isStructProperty = ownerType.IsValueType && member.IsProperty;
                
                if (isStructProperty)
                {
                    // 对于结构体的属性，我们需要使用反射来设置值，因为无法通过委托直接修改结构体的属性
                    sb.AppendLine("                        Setter = (obj, value) =>");
                    sb.AppendLine("                        {");
                    sb.AppendLine($"                            var property = typeof({ownerTypeName}).GetProperty(\"{member.Name}\");");
                    sb.AppendLine("                            if (property != null)");
                    sb.AppendLine("                            {");
                    sb.AppendLine("                                try");
                    sb.AppendLine("                                {");
                    sb.AppendLine($"                                    var convertedValue = value == null ? default({memberValueTypeName}) : ({memberValueTypeName})value;");
                    sb.AppendLine("                                    property.SetValue(obj, convertedValue);");
                    sb.AppendLine("                                }");
                    sb.AppendLine("                                catch (InvalidCastException)");
                    sb.AppendLine("                                {");
                    sb.AppendLine($"                                    throw new ArgumentException($\"Cannot convert {{value?.GetType().Name ?? \"null\"}} to {memberValueTypeName}\");");
                    sb.AppendLine("                                }");
                    sb.AppendLine("                            }");
                    sb.AppendLine("                            else");
                    sb.AppendLine("                            {");
                    sb.AppendLine($"                                throw new InvalidOperationException(\"Property {member.Name} not found\");");
                    sb.AppendLine("                            }");
                    sb.AppendLine("                        }");
                }
                else
                {
                    sb.AppendLine("                        Setter = (obj, value) =>");
                    sb.AppendLine("                        {");
                    sb.AppendLine($"                            if (obj is {ownerTypeName} typedObj)");
                    sb.AppendLine("                            {");
                    
                    // 处理值类型和引用类型的转换
                    if (member.Type.IsValueType)
                    {
                        // 检查是否为可空值类型
                        var underlyingType = member.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T 
                            ? ((INamedTypeSymbol)member.Type).TypeArguments[0] 
                            : member.Type;
                        var underlyingTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        
                        if (member.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T)
                        {
                            // 可空值类型处理
                            sb.AppendLine($"                                if (value is {underlyingTypeName} typedValue)");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    typedObj.{member.Name} = typedValue;");
                            sb.AppendLine("                                }");
                            sb.AppendLine("                                else if (value == null)");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    typedObj.{member.Name} = null;");
                            sb.AppendLine("                                }");
                            sb.AppendLine($"                                else if (value is {memberValueTypeName} nullableValue)");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    typedObj.{member.Name} = nullableValue;");
                            sb.AppendLine("                                }");
                            sb.AppendLine("                                else");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    throw new ArgumentException($\"Cannot convert {{value?.GetType().Name ?? \"null\"}} to {memberValueTypeName}\");");
                            sb.AppendLine("                                }");
                        }
                        else
                        {
                            // 普通值类型处理
                            sb.AppendLine($"                                if (value is {memberValueTypeName} typedValue)");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    typedObj.{member.Name} = typedValue;");
                            sb.AppendLine("                                }");
                            sb.AppendLine("                                else if (value == null)");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    typedObj.{member.Name} = default({memberValueTypeName});");
                            sb.AppendLine("                                }");
                            sb.AppendLine("                                else");
                            sb.AppendLine("                                {");
                            sb.AppendLine($"                                    throw new ArgumentException($\"Cannot convert {{value?.GetType().Name ?? \"null\"}} to {memberValueTypeName}\");");
                            sb.AppendLine("                                }");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"                                if (value == null || value is {memberValueTypeName})");
                        sb.AppendLine("                                {");
                        sb.AppendLine($"                                    typedObj.{member.Name} = ({memberValueTypeName})value;");
                        sb.AppendLine("                                }");
                        sb.AppendLine("                                else");
                        sb.AppendLine("                                {");
                        sb.AppendLine($"                                    throw new ArgumentException($\"Cannot convert {{value.GetType().Name}} to {memberValueTypeName}\");");
                        sb.AppendLine("                                }");
                    }
                    
                    sb.AppendLine("                                return;");
                    sb.AppendLine("                            }");
                    sb.AppendLine($"                            throw new ArgumentException($\"Expected {ownerTypeName}, got {{obj?.GetType().Name ?? \"null\"}}\");");
                    sb.AppendLine("                        }");
                }
            }
            else
            {
                // 只读成员的Setter为null
                sb.AppendLine("                        Setter = null");
            }
        }

        #region 类型分析方法

        /// <summary>
        /// 计算成员渲染顺序（编译时版本）
        /// </summary>
        private static int CalculateRenderOrder(AccessibleMemberInfo member)
        {
            int order = 1000; // 默认顺序

            // TitlePort具有最高优先级
            if (member.IsTitlePort)
            {
                order = 0;
            }
            // Child属性次之
            else if (member.IsChild)
            {
                // 假设所有Child都是top=false，因为编译时无法确定动态值
                order = 200;
            }
            // ShowInNode属性再次之
            else if (member.ShowInNode)
            {
                order = 300;
            }

            // Group属性影响顺序
            if (!string.IsNullOrEmpty(member.GroupName))
            {
                order += 50;
            }

            // 根据成员名称的字母顺序作为次要排序
            order += Math.Abs(member.Name.GetHashCode()) % 100;

            return order;
        }

        /// <summary>
        /// 确定成员分类（编译时版本）
        /// </summary>
        private static string DetermineMemberCategory(AccessibleMemberInfo member)
        {
            var memberType = member.Type;

            // 检查是否为JsonNode类型
            if (IsJsonNodeDerived(memberType))
            {
                return "JsonNode";
            }

            // 检查是否为集合类型
            if (IsCollectionType(memberType))
            {
                return "Collection";
            }

            return "Normal";
        }

        /// <summary>
        /// 获取分组名称（编译时版本）
        /// </summary>
        private static string GetGroupName(AccessibleMemberInfo member)
        {
            return member.GroupName ?? string.Empty;
        }

        /// <summary>
        /// 检查类型是否派生自JsonNode（编译时版本）
        /// </summary>
        private static bool IsJsonNodeDerived(ITypeSymbol typeSymbol)
        {
            var current = typeSymbol;
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

        /// <summary>
        /// 检查类型是否为集合类型（编译时版本）
        /// </summary>
        private static bool IsCollectionType(ITypeSymbol type)
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

        /// <summary>
        /// 分析是否为用户定义类型
        /// </summary>
        private bool AnalyzeIsUserDefinedType(INamedTypeSymbol type)
        {
            if (type == null) return false;
            
            // 基本类型和系统类型
            if (type.SpecialType != SpecialType.None) return false;
            
            // Unity基本类型
            if (type.ContainingNamespace?.ToDisplayString().StartsWith("UnityEngine") == true)
                return false;
            
            // 系统命名空间
            if (type.ContainingNamespace?.ToDisplayString().StartsWith("System") == true)
                return false;
            
            return true;
        }

        /// <summary>
        /// 分析是否包含JsonNode
        /// </summary>
        private bool AnalyzeContainsJsonNode(INamedTypeSymbol type)
        {
            if (type == null) return false;
            
            // 直接是JsonNode类型
            if (IsJsonNodeType(type)) return true;
            
            // 检查成员中是否包含JsonNode类型
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    if (IsJsonNodeType(property.Type)) return true;
                    if (IsCollectionOfJsonNode(property.Type)) return true;
                }
                else if (member is IFieldSymbol field)
                {
                    if (IsJsonNodeType(field.Type)) return true;
                    if (IsCollectionOfJsonNode(field.Type)) return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 分析是否可能包含嵌套JsonNode
        /// </summary>
        private bool AnalyzeMayContainNestedJsonNode(ITypeSymbol type)
        {
            if (type == null) return false;
            
            // 如果类型被标记为NoJsonNodeContainer，则不可能包含JsonNode
            if (HasNoJsonNodeContainerAttribute(type)) return false;
            
            // 基本类型不包含JsonNode
            if (type.SpecialType != SpecialType.None) return false;
            
            // Unity特定的基本值类型不包含JsonNode
            if (type.ContainingNamespace?.ToDisplayString().StartsWith("UnityEngine") == true)
                return false;
            
            // 集合类型快速检查
            if (IsCollection(type))
            {
                var elementType = GetCollectionElementType(type);
                if (elementType != null && IsJsonNodeType(elementType)) return true;
                if (elementType != null && AnalyzeIsUserDefinedType(elementType as INamedTypeSymbol)) return true;
            }
            
            // 对于用户定义的类型，保守返回true
            return AnalyzeIsUserDefinedType(type as INamedTypeSymbol);
        }

        /// <summary>
        /// 分析是否可能包含嵌套结构
        /// </summary>
        private bool AnalyzeMayContainNestedStructure(ITypeSymbol type)
        {
            if (type == null) return false;
            
            // 基本类型不包含嵌套结构
            if (type.SpecialType != SpecialType.None) return false;
            
            // Unity特定的基本值类型不包含嵌套结构
            if (type.ContainingNamespace?.ToDisplayString().StartsWith("UnityEngine") == true)
                return false;
            
            // JsonNode类型本身可能包含嵌套结构
            if (IsJsonNodeType(type)) return true;
            
            // 集合类型可能包含嵌套结构
            if (IsCollection(type)) return true;
            
            // 用户定义的类型可能包含嵌套结构
            return AnalyzeIsUserDefinedType(type as INamedTypeSymbol);
        }

        /// <summary>
        /// 分析是否有无参构造函数
        /// </summary>
        private bool AnalyzeHasParameterlessConstructor(INamedTypeSymbol type)
        {
            if (type == null || type.IsAbstract || type.TypeKind == TypeKind.Interface) return false;
            
            // 值类型总是有默认构造函数
            if (type.IsValueType) return true;
            
            // 检查是否有公共无参构造函数
            return type.Constructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
        }

        /// <summary>
        /// 检查是否为JsonNode的集合类型
        /// </summary>
        private bool IsCollectionOfJsonNode(ITypeSymbol type)
        {
            var elementType = GetCollectionElementType(type);
            return elementType != null && IsJsonNodeType(elementType);
        }

        #endregion
    }
}
