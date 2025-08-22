using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using TreeNodeSourceGenerator;

namespace TreeNodeSourceGenerator
{
    public partial class NodeAccessorSourceGenerator
    {

        private static bool HasNoJsonNodeContainerAttribute(ITypeSymbol type)
        {
            return type.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "NoJsonNodeContainerAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "TreeNode.Runtime.NoJsonNodeContainerAttribute");
        }

        private static bool HasJsonIgnoreAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "JsonIgnoreAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonIgnoreAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "Newtonsoft.Json.JsonIgnoreAttribute");
        }
        private AccessibleMemberInfo AnalyzeFieldMember(IFieldSymbol field)
        {
            var memberInfo = new AccessibleMemberInfo
            {
                Name = field.Name,
                Type = field.Type,
                IsProperty = false,
                IsValueType = field.Type.IsValueType,
                IsJsonNodeType = IsJsonNodeType(field.Type),
                HasNestedAccess = HasNestedAccessCapability(field.Type),
                IsCollection = IsCollection(field.Type),
                ElementType = GetCollectionElementType(field.Type),
                CanWrite = !field.IsReadOnly,
                IsReadOnly = field.IsReadOnly,
                IsStructCollection = IsStructCollection(field.Type),
                HasNoJsonNodeContainer = HasNoJsonNodeContainerAttribute(field.Type)
            };
            
            // 分析特性标记
            AnalyzeMemberAttributes(field, memberInfo);
            
            return memberInfo;
        }

        private AccessibleMemberInfo AnalyzePropertyMember(IPropertySymbol property)
        {
            var memberInfo = new AccessibleMemberInfo
            {
                Name = property.Name,
                Type = property.Type,
                IsProperty = true,
                IsValueType = property.Type.IsValueType,
                IsJsonNodeType = IsJsonNodeType(property.Type),
                HasNestedAccess = HasNestedAccessCapability(property.Type),
                IsCollection = IsCollection(property.Type),
                ElementType = GetCollectionElementType(property.Type),
                CanWrite = property.SetMethod != null,
                IsReadOnly = property.SetMethod == null,
                IsStructCollection = IsStructCollection(property.Type),
                HasNoJsonNodeContainer = HasNoJsonNodeContainerAttribute(property.Type)
            };
            
            // 分析特性标记
            AnalyzeMemberAttributes(property, memberInfo);
            
            return memberInfo;
        }

        /// <summary>
        /// 分析成员的特性标记
        /// </summary>
        private void AnalyzeMemberAttributes(ISymbol member, AccessibleMemberInfo memberInfo)
        {
            var attributes = member.GetAttributes();
            
            foreach (var attr in attributes)
            {
                var attrName = attr.AttributeClass?.Name;
                var attrFullName = attr.AttributeClass?.ToDisplayString();
                
                switch (attrName)
                {
                    case "ChildAttribute":
                        memberInfo.IsChild = true;
                        memberInfo.ShowInNode = true;
                        // 尝试获取 IsTop 参数
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            var firstArg = attr.ConstructorArguments[0];
                            if (firstArg.Type?.SpecialType == SpecialType.System_Boolean)
                            {
                                memberInfo.IsTopChild = (bool)firstArg.Value;
                            }
                        }
                        break;
                        
                    case "TitlePortAttribute":
                        memberInfo.IsTitlePort = true;
                        memberInfo.ShowInNode = true;
                        break;
                        
                    case "ShowInNodeAttribute":
                        memberInfo.ShowInNode = true;
                        // 获取 Order 属性
                        foreach (var namedArg in attr.NamedArguments)
                        {
                            if (namedArg.Key == "Order" && namedArg.Value.Type?.SpecialType == SpecialType.System_Int32)
                            {
                                memberInfo.Order = (int)(namedArg.Value.Value ?? 0);
                            }
                        }
                        break;
                        
                    case "GroupAttribute":
                        memberInfo.HasGroupAttribute = true;
                        // 尝试获取 Name 参数
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            var firstArg = attr.ConstructorArguments[0];
                            if (firstArg.Type?.SpecialType == SpecialType.System_String)
                            {
                                memberInfo.GroupName = firstArg.Value?.ToString() ?? string.Empty;
                            }
                        }
                        break;
                }
            }
        }
        private bool HasParameterlessConstructor(ITypeSymbol type)
        {
            if (type == null || type.IsAbstract || type.TypeKind != TypeKind.Class)
                return false;

            // 检查是否有公共无参构造函数
            var constructors = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Constructor &&
                           m.DeclaredAccessibility == Accessibility.Public);

            // 如果没有显式声明构造函数，则有默认的无参构造函数
            if (!constructors.Any())
                return true;

            // 检查是否有无参构造函数
            return constructors.Any(c => c.Parameters.Length == 0);
        }
        private bool NeedCreateInstance(AccessibleMemberInfo member)
        {
            return !member.IsValueType && !member.IsJsonNodeType && HasParameterlessConstructor(member.Type);
        }

        private List<AccessibleMemberInfo> AnalyzeAccessibleMembers(INamedTypeSymbol nodeType, HashSet<INamedTypeSymbol> propertyAccessorTypes)
        {
            Debug.Log($"-{nodeType.ToDisplayString()}");

            var currentType = nodeType;
            Dictionary<string, AccessibleMemberInfo> dic = new();
            int declarationIndex = 0;
            
            while (currentType != null)
            {
                //Debug.Log($"  {currentType.ToDisplayString()}");
                foreach (var member in currentType.GetMembers())
                {
                    bool added = false;
                    if (!IsMemberValid(member))
                    {
                        //Debug.Log($"     {member.ToDisplayString()} :false");
                        continue;
                    }
                    if (member is IFieldSymbol field && !field.IsReadOnly)
                    {
                        if (dic.ContainsKey(field.Name)) { continue; }
                        var memberInfo = AnalyzeFieldMember(field);
                        memberInfo.DeclarationIndex = declarationIndex++;
                        dic.Add(field.Name, memberInfo);

                        added = true;

                        if (memberInfo.HasNestedAccess &&
                            !memberInfo.IsJsonNodeType &&
                            !memberInfo.IsCollection &&
                            !memberInfo.Type.IsAbstract &&
                            memberInfo.Type is INamedTypeSymbol namedType &&
                            IsPartial(namedType))
                        {
                            propertyAccessorTypes.Add(namedType);
                        }
                    }
                    else if (member is IPropertySymbol property &&
                             property.GetMethod != null && !property.IsReadOnly)
                    {
                        if (dic.ContainsKey(property.Name)) { continue; }
                        var memberInfo = AnalyzePropertyMember(property);
                        memberInfo.DeclarationIndex = declarationIndex++;
                        dic.Add(property.Name, memberInfo);
                        added = true;

                        if (memberInfo.HasNestedAccess &&
                            !memberInfo.IsJsonNodeType &&
                            !memberInfo.IsCollection &&
                            !memberInfo.Type.IsAbstract &&
                            memberInfo.Type is INamedTypeSymbol namedType &&
                            IsPartial(namedType))
                        {
                            propertyAccessorTypes.Add(namedType);
                        }
                    }
                    //Debug.Log($"     {member.ToDisplayString()} :{added}");
                }
                // 移动到父类
                currentType = currentType.BaseType;

                // 避免处理 System.Object 等系统类型
                if (currentType?.ContainingNamespace?.ToDisplayString().StartsWith("System") == true)
                    break;
            }
            return dic.Values.ToList();
        }


        public static bool IsMemberValid(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Public &&
                 !symbol.IsStatic &&
                 !HasJsonIgnoreAttribute(symbol);
        }

    
    }
}