using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    /// <summary>
    /// JsonNode树状逻辑处理器 - 高性能版本，使用缓存和UI渲染顺序优化
    /// 提供高性能的节点查询、遍历和操作功能
    /// </summary>
    public class JsonNodeTree
    {
        #region 内部数据结构

        /// <summary>
        /// 节点元数据，包含节点的层次结构信息
        /// </summary>
        public class NodeMetadata
        {
            public JsonNode Node { get; set; }
            public string Path { get; set; }
            public int Depth { get; set; }
            public NodeMetadata Parent { get; set; }
            public List<NodeMetadata> Children { get; set; } = new();
            public int RootIndex { get; set; } = -1;
            public int ListIndex { get; set; } = 0;
            public string PortName { get; set; }
            public bool IsMultiPort { get; set; }
            public int RenderOrder { get; set; } = 0; // UI渲染顺序
            
            public bool IsRoot => Parent == null;
            public string DisplayName => Node?.GetInfo() ?? "Unknown";
        }

        /// <summary>
        /// 类型成员信息缓存
        /// </summary>
        public class TypeMemberInfo
        {
            public Type Type { get; set; }
            public List<MemberRenderInfo> RenderMembers { get; set; } = new();
            public List<MemberInfo> JsonNodeMembers { get; set; } = new();
            public List<MemberInfo> CollectionMembers { get; set; } = new();
            public List<NestedNodePath> NestedPaths { get; set; } = new();
        }

        /// <summary>
        /// 成员渲染信息，按照UI显示顺序排序
        /// </summary>
        public class MemberRenderInfo
        {
            public MemberInfo Member { get; set; }
            public int RenderOrder { get; set; }
            public bool IsChild { get; set; }
            public bool IsTitlePort { get; set; }
            public bool IsMultiValue { get; set; }
            public Type ValueType { get; set; }
            public string GroupName { get; set; }
            public bool IsNestedNodeContainer { get; set; } // 如FuncValue.Node, TimeValue.Value.Node等
        }

        /// <summary>
        /// 嵌套节点路径信息
        /// </summary>
        public class NestedNodePath
        {
            public string Path { get; set; }
            public Type ContainerType { get; set; }
            public string[] PathSegments { get; set; }
            public int Depth { get; set; }
        }

        #endregion

        #region 缓存字段

        // 类型成员信息缓存
        private static readonly ConcurrentDictionary<Type, TypeMemberInfo> _typeMemberCache = new();
        
        // 类型检测缓存 - 是否为用户定义类型
        private static readonly ConcurrentDictionary<Type, bool> _userDefinedTypeCache = new();
        
        // 类型检测缓存 - 是否包含JsonNode
        private static readonly ConcurrentDictionary<Type, bool> _containsJsonNodeCache = new();

        #endregion

        #region 字段

        private readonly TreeNodeAsset _asset;
        private readonly Dictionary<JsonNode, NodeMetadata> _nodeMetadataMap;
        private readonly List<NodeMetadata> _rootNodes;
        private bool _isDirty = true;

        #endregion

        #region 构造函数

        public JsonNodeTree(TreeNodeAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _nodeMetadataMap = new Dictionary<JsonNode, NodeMetadata>();
            _rootNodes = new List<NodeMetadata>();
            
            RebuildTree();
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取所有根节点的元数据
        /// </summary>
        public IReadOnlyList<NodeMetadata> RootNodes => _rootNodes.AsReadOnly();

        /// <summary>
        /// 获取所有节点的总数
        /// </summary>
        public int TotalNodeCount => _nodeMetadataMap.Count;

        /// <summary>
        /// 树是否需要重新构建
        /// </summary>
        public bool IsDirty => _isDirty;

        #endregion

        #region 核心构建方法

        /// <summary>
        /// 重新构建整个树结构
        /// </summary>
        public void RebuildTree()
        {
            _nodeMetadataMap.Clear();
            _rootNodes.Clear();

            // 首先收集所有节点
            var allNodes = CollectAllNodes();
            
            // 为每个节点创建元数据
            foreach (var node in allNodes)
            {
                if (!_nodeMetadataMap.ContainsKey(node))
                {
                    var metadata = new NodeMetadata { Node = node };
                    _nodeMetadataMap[node] = metadata;
                }
            }
            // 建立层次关系
            BuildHierarchy();
            // 计算路径和深度
            CalculatePathsAndDepths();
            _isDirty = false;
        }

        /// <summary>
        /// 递归收集所有JsonNode（包括嵌套的）- 高性能版本
        /// </summary>
        private HashSet<JsonNode> CollectAllNodes()
        {
            var allNodes = new HashSet<JsonNode>();
            
            // 从根节点开始
            foreach (var rootNode in _asset.Nodes)
            {
                CollectNodesRecursively(rootNode, allNodes);
            }
            
            return allNodes;
        }

        /// <summary>
        /// 递归收集节点 - 使用缓存的类型信息和通用嵌套检测
        /// </summary>
        private void CollectNodesRecursively(JsonNode node, HashSet<JsonNode> collected)
        {
            if (node == null || collected.Contains(node))
                return;
                
            collected.Add(node);
            
            var typeInfo = GetOrCreateTypeMemberInfo(node.GetType());
            
            // 处理直接的JsonNode成员
            foreach (var member in typeInfo.JsonNodeMembers)
            {
                try
                {
                    var value = member.GetValue(node);
                    if (value is JsonNode childNode)
                    {
                        CollectNodesRecursively(childNode, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
            
            // 处理集合成员 - 增强版本，支持嵌套节点集合
            foreach (var member in typeInfo.CollectionMembers)
            {
                try
                {
                    var value = member.GetValue(node);
                    if (value is System.Collections.IEnumerable enumerable)
                    {
                        CollectNodesFromCollection(enumerable, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
            
            // 处理嵌套的JsonNode - 使用通用递归方法
            CollectNestedNodesGeneric(node, collected);
        }

        /// <summary>
        /// 通用方法收集嵌套的JsonNode
        /// </summary>
        private void CollectNestedNodesGeneric(JsonNode node, HashSet<JsonNode> collected)
        {
            var typeInfo = GetOrCreateTypeMemberInfo(node.GetType());
            
            foreach (var nestedPath in typeInfo.NestedPaths)
            {
                try
                {
                    var nestedNode = PropertyAccessor.GetValue<JsonNode>(node, nestedPath.Path);
                    if (nestedNode != null)
                    {
                        CollectNodesRecursively(nestedNode, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的路径
                }
            }
        }

        /// <summary>
        /// 从集合中收集JsonNode - 支持嵌套结构
        /// </summary>
        private void CollectNodesFromCollection(System.Collections.IEnumerable collection, HashSet<JsonNode> collected)
        {
            foreach (var item in collection)
            {
                if (item == null) continue;

                // 直接是JsonNode的情况
                if (item is JsonNode childJsonNode)
                {
                    CollectNodesRecursively(childJsonNode, collected);
                }
                // 可能包含嵌套JsonNode的用户定义类型（如TimeValue）
                else if (IsUserDefinedType(item.GetType()) && ContainsJsonNodeRecursively(item.GetType()))
                {
                    CollectNestedNodesFromObject(item, collected);
                }
            }
        }

        /// <summary>
        /// 从对象中收集嵌套的JsonNode
        /// </summary>
        private void CollectNestedNodesFromObject(object obj, HashSet<JsonNode> collected)
        {
            if (obj == null) return;

            var typeInfo = GetOrCreateTypeMemberInfo(obj.GetType());
            
            // 处理嵌套路径
            foreach (var nestedPath in typeInfo.NestedPaths)
            {
                try
                {
                    var nestedNode = PropertyAccessor.GetValue<JsonNode>(obj, nestedPath.Path);
                    if (nestedNode != null)
                    {
                        CollectNodesRecursively(nestedNode, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的路径
                }
            }

            // 处理直接的JsonNode成员
            foreach (var member in typeInfo.JsonNodeMembers)
            {
                try
                {
                    var value = member.GetValue(obj);
                    if (value is JsonNode childNode)
                    {
                        CollectNodesRecursively(childNode, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
        }

        /// <summary>
        /// 建立节点间的层次关系 - 使用缓存和UI渲染顺序
        /// </summary>
        private void BuildHierarchy()
        {
            // 首先标记根节点
            for (int i = 0; i < _asset.Nodes.Count; i++)
            {
                var rootNode = _asset.Nodes[i];
                if (_nodeMetadataMap.TryGetValue(rootNode, out var metadata))
                {
                    metadata.RootIndex = i;
                    _rootNodes.Add(metadata);
                }
            }

            // 然后为每个节点建立父子关系
            foreach (var kvp in _nodeMetadataMap)
            {
                var parentNode = kvp.Key;
                var parentMetadata = kvp.Value;
                
                BuildChildRelationshipsWithOrder(parentNode, parentMetadata);
            }
        }

        /// <summary>
        /// 为指定节点建立子节点关系 - 考虑UI渲染顺序
        /// </summary>
        private void BuildChildRelationshipsWithOrder(JsonNode parentNode, NodeMetadata parentMetadata)
        {
            var typeInfo = GetOrCreateTypeMemberInfo(parentNode.GetType());
            
            // 按渲染顺序处理成员
            foreach (var renderInfo in typeInfo.RenderMembers)
            {
                try
                {
                    var value = renderInfo.Member.GetValue(parentNode);
                    ProcessChildMemberWithOrder(renderInfo, value, parentMetadata);
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
            
            // 处理嵌套节点容器
            ProcessNestedNodesHierarchy(parentNode, parentMetadata, typeInfo);
        }

        /// <summary>
        /// 处理子成员 - 考虑渲染顺序和特殊类型
        /// </summary>
        private void ProcessChildMemberWithOrder(MemberRenderInfo renderInfo, object value, NodeMetadata parentMetadata)
        {
            if (value == null) return;
            
            // 直接的JsonNode子节点
            if (renderInfo.ValueType.IsSubclassOf(typeof(JsonNode)) && value is JsonNode childNode)
            {
                if (_nodeMetadataMap.TryGetValue(childNode, out var childMetadata))
                {
                    childMetadata.Parent = parentMetadata;
                    childMetadata.PortName = renderInfo.Member.Name;
                    childMetadata.IsMultiPort = false;
                    childMetadata.RenderOrder = renderInfo.RenderOrder;
                    parentMetadata.Children.Add(childMetadata);
                }
            }
            // 集合类型 - 支持直接JsonNode集合和嵌套节点集合
            else if (renderInfo.IsMultiValue && value is System.Collections.IEnumerable enumerable)
            {
                ProcessCollectionChildren(enumerable, renderInfo, parentMetadata);
            }
        }

        /// <summary>
        /// 处理集合类型的子节点
        /// </summary>
        private void ProcessCollectionChildren(System.Collections.IEnumerable collection, MemberRenderInfo renderInfo, NodeMetadata parentMetadata)
        {
            int index = 0;
            foreach (var item in collection)
            {
                if (item == null)
                {
                    index++;
                    continue;
                }

                // 直接是JsonNode的情况
                if (item is JsonNode childJsonNode && _nodeMetadataMap.TryGetValue(childJsonNode, out var childMetadata))
                {
                    childMetadata.Parent = parentMetadata;
                    childMetadata.PortName = renderInfo.Member.Name;
                    childMetadata.IsMultiPort = true;
                    childMetadata.ListIndex = index;
                    childMetadata.RenderOrder = renderInfo.RenderOrder;
                    parentMetadata.Children.Add(childMetadata);
                }
                // 包含嵌套JsonNode的用户定义类型（如TimeValue）
                else if (IsUserDefinedType(item.GetType()) && ContainsJsonNodeRecursively(item.GetType()))
                {
                    ProcessNestedNodesInCollectionItem(item, index, renderInfo, parentMetadata);
                }

                index++;
            }
        }

        /// <summary>
        /// 处理集合项中的嵌套节点
        /// </summary>
        private void ProcessNestedNodesInCollectionItem(object item, int itemIndex, MemberRenderInfo renderInfo, NodeMetadata parentMetadata)
        {
            var typeInfo = GetOrCreateTypeMemberInfo(item.GetType());
            
            foreach (var nestedPath in typeInfo.NestedPaths)
            {
                try
                {
                    var nestedNode = PropertyAccessor.GetValue<JsonNode>(item, nestedPath.Path);
                    if (nestedNode != null && _nodeMetadataMap.TryGetValue(nestedNode, out var childMetadata))
                    {
                        childMetadata.Parent = parentMetadata;
                        childMetadata.PortName = $"{renderInfo.Member.Name}[{itemIndex}].{nestedPath.Path}";
                        childMetadata.IsMultiPort = true;
                        childMetadata.ListIndex = itemIndex;
                        childMetadata.RenderOrder = renderInfo.RenderOrder + nestedPath.Depth;
                        parentMetadata.Children.Add(childMetadata);
                    }
                }
                catch
                {
                    // 跳过无法访问的嵌套路径
                }
            }
        }

        /// <summary>
        /// 处理嵌套节点层次结构
        /// </summary>
        private void ProcessNestedNodesHierarchy(JsonNode parentNode, NodeMetadata parentMetadata, TypeMemberInfo typeInfo)
        {
            foreach (var nestedPath in typeInfo.NestedPaths)
            {
                try
                {
                    var nestedNode = PropertyAccessor.GetValue<JsonNode>(parentNode, nestedPath.Path);
                    if (nestedNode != null && _nodeMetadataMap.TryGetValue(nestedNode, out var childMetadata))
                    {
                        childMetadata.Parent = parentMetadata;
                        childMetadata.PortName = nestedPath.Path;
                        childMetadata.IsMultiPort = false;
                        childMetadata.RenderOrder = 500 + nestedPath.Depth; // 嵌套节点优先级较低
                        parentMetadata.Children.Add(childMetadata);
                    }
                }
                catch
                {
                    // 跳过无法访问的嵌套路径
                }
            }
        }

        /// <summary>
        /// 计算所有节点的路径和深度
        /// </summary>
        private void CalculatePathsAndDepths()
        {
            foreach (var rootMetadata in _rootNodes)
            {
                CalculatePathAndDepthRecursively(rootMetadata, $"[{rootMetadata.RootIndex}]", 0);
            }
        }

        /// <summary>
        /// 递归计算路径和深度
        /// </summary>
        private void CalculatePathAndDepthRecursively(NodeMetadata metadata, string path, int depth)
        {
            metadata.Path = path;
            metadata.Depth = depth;
            
            // 按渲染顺序排序子节点
            var sortedChildren = metadata.Children.OrderBy(c => c.RenderOrder).ThenBy(c => c.ListIndex).ToList();
            
            foreach (var child in sortedChildren)
            {
                string childPath = BuildChildPath(path, child, metadata.Node);
                CalculatePathAndDepthRecursively(child, childPath, depth + 1);
            }
        }

        /// <summary>
        /// 构建子节点路径
        /// </summary>
        private string BuildChildPath(string parentPath, NodeMetadata child, JsonNode parentNode)
        {
            if (child.IsMultiPort)
            {
                return $"{parentPath}.{child.PortName}";
            }
            else
            {
                return $"{parentPath}.{child.PortName}";
            }
        }

        #endregion

        #region 缓存管理方法

        /// <summary>
        /// 获取或创建类型成员信息 - 使用缓存
        /// </summary>
        private TypeMemberInfo GetOrCreateTypeMemberInfo(Type type)
        {
            return _typeMemberCache.GetOrAdd(type, CreateTypeMemberInfo);
        }

        /// <summary>
        /// 创建类型成员信息 - 分析UI渲染顺序和嵌套路径
        /// </summary>
        private TypeMemberInfo CreateTypeMemberInfo(Type type)
        {
            var typeInfo = new TypeMemberInfo { Type = type };
            var allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                .ToList();

            var renderMembers = new List<MemberRenderInfo>();

            foreach (var member in allMembers)
            {
                var renderInfo = CreateMemberRenderInfo(member);
                if (renderInfo != null)
                {
                    renderMembers.Add(renderInfo);
                    
                    // 分类存储以便快速访问
                    if (renderInfo.ValueType.IsSubclassOf(typeof(JsonNode)))
                    {
                        typeInfo.JsonNodeMembers.Add(member);
                    }
                    else if (IsJsonNodeCollection(renderInfo.ValueType) || IsNestedNodeCollection(renderInfo.ValueType))
                    {
                        typeInfo.CollectionMembers.Add(member);
                    }
                }
            }

            // 按UI渲染顺序排序
            typeInfo.RenderMembers = renderMembers.OrderBy(r => r.RenderOrder).ToList();
            
            // 分析嵌套JsonNode路径
            typeInfo.NestedPaths = AnalyzeNestedJsonNodePaths(type);
            
            return typeInfo;
        }

        /// <summary>
        /// 创建成员渲染信息
        /// </summary>
        private MemberRenderInfo CreateMemberRenderInfo(MemberInfo member)
        {
            var valueType = member.GetValueType();
            if (valueType == null) return null;

            var renderInfo = new MemberRenderInfo
            {
                Member = member,
                ValueType = valueType,
                RenderOrder = CalculateRenderOrder(member),
                IsChild = HasAttribute<ChildAttribute>(member),
                IsTitlePort = HasAttribute<TitlePortAttribute>(member),
                IsMultiValue = IsJsonNodeCollection(valueType) || IsNestedNodeCollection(valueType),
                GroupName = GetGroupName(member),
                IsNestedNodeContainer = ContainsJsonNodeRecursively(valueType)
            };

            return renderInfo;
        }

        /// <summary>
        /// 计算UI渲染顺序
        /// </summary>
        private int CalculateRenderOrder(MemberInfo member)
        {
            int order = 1000; // 默认顺序

            // TitlePort具有最高优先级
            if (HasAttribute<TitlePortAttribute>(member))
            {
                order = 0;
            }
            // Child属性次之
            else if (HasAttribute<ChildAttribute>(member))
            {
                var childAttr = member.GetCustomAttribute<ChildAttribute>();
                // 检查是否有Top参数，如果Child(true)表示置顶
                bool isTop = false;
                try
                {
                    // 尝试通过反射获取构造函数参数或属性值
                    var constructorArgs = childAttr?.GetType().GetProperty("Top")?.GetValue(childAttr);
                    if (constructorArgs is bool topValue)
                    {
                        isTop = topValue;
                    }
                    else
                    {
                        // 如果没有Top属性，尝试检查构造函数参数
                        // 对于Child(true)这种情况，可能是构造函数参数
                        var constructors = childAttr?.GetType().GetConstructors();
                        if (constructors?.Length > 0)
                        {
                            var firstConstructor = constructors[0];
                            var parameters = firstConstructor.GetParameters();
                            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(bool))
                            {
                                // 这里我们假设Child(true)中的true表示优先级较高
                                // 由于无法直接获取构造参数值，我们通过其他方式判断
                                // 检查特性的字符串表示或使用默认逻辑
                                var attrString = childAttr?.ToString();
                                isTop = attrString?.Contains("True") == true;
                            }
                        }
                    }
                }
                catch
                {
                    isTop = false;
                }
                
                order = isTop ? 100 : 200;
            }
            // ShowInNode属性再次之
            else if (HasAttribute<ShowInNodeAttribute>(member))
            {
                order = 300;
            }

            // Group属性影响顺序
            var groupAttr = member.GetCustomAttribute<GroupAttribute>();
            if (groupAttr != null)
            {
                order += 50; // Group内的元素稍微延后
            }

            // 根据成员名称的字母顺序作为次要排序
            order += member.Name.GetHashCode() % 100;

            return order;
        }

        /// <summary>
        /// 分析嵌套JsonNode路径 - 通用递归方法
        /// </summary>
        private List<NestedNodePath> AnalyzeNestedJsonNodePaths(Type containerType)
        {
            var paths = new List<NestedNodePath>();
            var visited = new HashSet<Type>();
            
            AnalyzeNestedPathsRecursively(containerType, "", 0, paths, visited, 3); // 最大深度3
            
            return paths;
        }

        /// <summary>
        /// 递归分析嵌套路径
        /// </summary>
        private void AnalyzeNestedPathsRecursively(Type type, string currentPath, int depth, 
            List<NestedNodePath> paths, HashSet<Type> visited, int maxDepth)
        {
            if (depth >= maxDepth || visited.Contains(type) || !IsUserDefinedType(type))
                return;

            visited.Add(type);

            try
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                    .ToList();

                foreach (var member in members)
                {
                    try
                    {
                        var memberType = member.GetValueType();
                        if (memberType == null) continue;

                        var memberPath = string.IsNullOrEmpty(currentPath) ? member.Name : $"{currentPath}.{member.Name}";

                        // 如果成员直接是JsonNode，添加路径
                        if (memberType.IsSubclassOf(typeof(JsonNode)))
                        {
                            paths.Add(new NestedNodePath
                            {
                                Path = memberPath,
                                ContainerType = type,
                                PathSegments = memberPath.Split('.'),
                                Depth = depth
                            });
                        }
                        // 如果成员可能包含JsonNode，继续递归
                        else if (IsUserDefinedType(memberType) && ContainsJsonNodeRecursively(memberType))
                        {
                            AnalyzeNestedPathsRecursively(memberType, memberPath, depth + 1, paths, visited, maxDepth);
                        }
                    }
                    catch
                    {
                        // 跳过无法处理的成员
                    }
                }
            }
            catch
            {
                // 跳过无法处理的类型
            }
            finally
            {
                visited.Remove(type);
            }
        }

        /// <summary>
        /// 检查类型是否为用户定义类型（排除Unity和System命名空间）
        /// </summary>
        private bool IsUserDefinedType(Type type)
        {
            return _userDefinedTypeCache.GetOrAdd(type, t =>
            {
                if (t == null || t.IsPrimitive || t.IsEnum || t == typeof(string))
                    return false;

                var namespaceName = t.Namespace ?? "";
                
                // 排除系统和Unity命名空间
                if (namespaceName.StartsWith("System") || 
                    namespaceName.StartsWith("Unity") || 
                    namespaceName.StartsWith("UnityEngine") ||
                    namespaceName.StartsWith("UnityEditor") ||
                    namespaceName.StartsWith("Microsoft") ||
                    namespaceName.StartsWith("Mono") ||
                    namespaceName.StartsWith("Newtonsoft"))
                {
                    return false;
                }

                // 排除集合类型（除非是用户定义的集合）
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && 
                    !t.IsArray && 
                    !namespaceName.Contains("TreeNode") && 
                    !namespaceName.Contains("SkillEditorDemo"))
                {
                    return false;
                }

                return true;
            });
        }
        
        /// <summary>
        /// 递归检查类型是否包含JsonNode（带缓存）
        /// </summary>
        private bool ContainsJsonNodeRecursively(Type type)
        {
            return _containsJsonNodeCache.GetOrAdd(type, t => 
                ContainsJsonNodeRecursivelyInternal(t, new HashSet<Type>(), 0, 3));
        }

        /// <summary>
        /// 递归检查类型是否包含JsonNode的内部实现
        /// </summary>
        private bool ContainsJsonNodeRecursivelyInternal(Type type, HashSet<Type> visited, int depth, int maxDepth)
        {
            if (type == null || depth >= maxDepth || visited.Contains(type) || !IsUserDefinedType(type))
                return false;

            // 直接是JsonNode
            if (type.IsSubclassOf(typeof(JsonNode)))
                return true;

            visited.Add(type);

            try
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field);

                foreach (var member in members)
                {
                    try
                    {
                        var memberType = member.GetValueType();
                        if (memberType != null)
                        {
                            // 检查成员类型
                            if (memberType.IsSubclassOf(typeof(JsonNode)))
                                return true;

                            // 检查集合元素类型
                            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType) && 
                                !typeof(string).IsAssignableFrom(memberType))
                            {
                                var elementType = GetCollectionElementType(memberType);
                                if (elementType != null && 
                                    (elementType.IsSubclassOf(typeof(JsonNode)) || 
                                     (IsUserDefinedType(elementType) && ContainsJsonNodeRecursivelyInternal(elementType, visited, depth + 1, maxDepth))))
                                {
                                    return true;
                                }
                            }

                            // 递归检查用户定义的复杂类型
                            if (IsUserDefinedType(memberType) && 
                                ContainsJsonNodeRecursivelyInternal(memberType, visited, depth + 1, maxDepth))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 跳过无法处理的成员
                    }
                }
            }
            catch
            {
                // 跳过无法处理的类型
            }
            finally
            {
                visited.Remove(type);
            }

            return false;
        }

        /// <summary>
        /// 获取集合的元素类型
        /// </summary>
        private Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType.IsArray)
                return collectionType.GetElementType();

            if (collectionType.IsGenericType)
            {
                var genericArgs = collectionType.GetGenericArguments();
                if (genericArgs.Length > 0)
                    return genericArgs[0];
            }

            // 检查实现的泛型接口
            foreach (var interfaceType in collectionType.GetInterfaces())
            {
                if (interfaceType.IsGenericType && 
                    (interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                     interfaceType.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                     interfaceType.GetGenericTypeDefinition() == typeof(IList<>)))
                {
                    return interfaceType.GetGenericArguments()[0];
                }
            }

            return null;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查是否有指定特性
        /// </summary>
        private bool HasAttribute<T>(MemberInfo member) where T : Attribute
        {
            return member.GetCustomAttribute<T>() != null;
        }

        /// <summary>
        /// 获取Group名称
        /// </summary>
        private string GetGroupName(MemberInfo member)
        {
            var groupAttr = member.GetCustomAttribute<GroupAttribute>();
            return groupAttr?.Name ?? string.Empty;
        }

        /// <summary>
        /// 检查是否是JsonNode集合类型
        /// </summary>
        private bool IsJsonNodeCollection(Type type)
        {
            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || 
                typeof(string).IsAssignableFrom(type))
            {
                return false;
            }

            var elementType = GetCollectionElementType(type);
            return elementType != null && elementType.IsSubclassOf(typeof(JsonNode));
        }

        /// <summary>
        /// 检查是否是包含嵌套JsonNode的集合类型（如List<TimeValue>）
        /// </summary>
        private bool IsNestedNodeCollection(Type type)
        {
            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || 
                typeof(string).IsAssignableFrom(type))
            {
                return false;
            }

            var elementType = GetCollectionElementType(type);
            return elementType != null && 
                   IsUserDefinedType(elementType) && 
                   ContainsJsonNodeRecursively(elementType);
        }

        #endregion

        #region 查询方法（保持原有接口)

        /// <summary>
        /// 获取节点的元数据
        /// </summary>
        public NodeMetadata GetNodeMetadata(JsonNode node)
        {
            return _nodeMetadataMap.TryGetValue(node, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// 根据路径查找节点
        /// </summary>
        public JsonNode FindNodeByPath(string path)
        {
            try
            {
                return _asset.GetValue<JsonNode>(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取所有节点路径
        /// </summary>
        public List<(string path, string info)> GetAllNodePaths()
        {
            var result = new List<(string, string)>();
            
            foreach (var metadata in GetSortedNodes())
            {
                result.Add((metadata.Path, $"{metadata.Path}--{metadata.Node.GetType().Name}"));
            }
            
            return result.OrderBy(x => x.Item1).ToList();
        }

        /// <summary>
        /// 获取排序后的节点列表 - 考虑UI渲染顺序
        /// </summary>
        public List<NodeMetadata> GetSortedNodes()
        {
            var allNodes = _nodeMetadataMap.Values.ToList();
            
            // 按照以下规则排序：
            // 1. 根节点索引
            // 2. 深度（父节点优先）
            // 3. 渲染顺序（UI显示顺序）
            // 4. 同级节点按列表索引排序
            return allNodes.OrderBy(n => n.RootIndex == -1 ? int.MaxValue : n.RootIndex)
                          .ThenBy(n => n.Depth)
                          .ThenBy(n => n.RenderOrder)
                          .ThenBy(n => n.ListIndex)
                          .ToList();
        }

        /// <summary>
        /// 获取节点的所有子节点（递归）
        /// </summary>
        public List<NodeMetadata> GetAllDescendants(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            if (metadata == null) return new List<NodeMetadata>();
            
            var result = new List<NodeMetadata>();
            CollectDescendantsRecursively(metadata, result);
            return result;
        }

        private void CollectDescendantsRecursively(NodeMetadata metadata, List<NodeMetadata> result)
        {
            var sortedChildren = metadata.Children.OrderBy(c => c.RenderOrder).ThenBy(c => c.ListIndex).ToList();
            foreach (var child in sortedChildren)
            {
                result.Add(child);
                CollectDescendantsRecursively(child, result);
            }
        }

        /// <summary>
        /// 获取节点的直接子节点
        /// </summary>
        public List<NodeMetadata> GetDirectChildren(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            return metadata?.Children.OrderBy(c => c.RenderOrder).ThenBy(c => c.ListIndex).ToList() ?? new List<NodeMetadata>();
        }

        /// <summary>
        /// 获取节点的根节点
        /// </summary>
        public NodeMetadata GetRoot(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            if (metadata == null) return null;
            
            while (metadata.Parent != null)
            {
                metadata = metadata.Parent;
            }
            
            return metadata;
        }

        /// <summary>
        /// 获取节点的最大子节点深度
        /// </summary>
        public int GetMaxChildDepth(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            if (metadata == null) return -1;
            
            return GetMaxDepthRecursively(metadata);
        }

        private int GetMaxDepthRecursively(NodeMetadata metadata)
        {
            int maxDepth = metadata.Depth;
            
            foreach (var child in metadata.Children)
            {
                maxDepth = Math.Max(maxDepth, GetMaxDepthRecursively(child));
            }
            
            return maxDepth;
        }

        #endregion

        #region 树视图生成

        /// <summary>
        /// 生成树状视图字符串 - 使用优化的排序
        /// </summary>
        public string GetTreeView()
        {
            if (_rootNodes.Count == 0)
                return "No nodes found";

            var treeBuilder = new StringBuilder();
            var processedNodes = new HashSet<NodeMetadata>();
            
            // 按根节点索引排序
            var sortedRootNodes = _rootNodes.OrderBy(r => r.RootIndex).ToList();
            
            // 处理每个根节点树
            for (int i = 0; i < sortedRootNodes.Count; i++)
            {
                var root = sortedRootNodes[i];
                if (processedNodes.Contains(root))
                    continue;
                
                // 添加根节点树之间的分隔线
                if (i > 0)
                {
                    treeBuilder.AppendLine();
                }
                
                BuildTreeViewRecursively(root, "", true, treeBuilder, processedNodes, true);
            }
            
            return treeBuilder.ToString().TrimEnd();
        }

        /// <summary>
        /// 递归构建树视图
        /// </summary>
        private void BuildTreeViewRecursively(NodeMetadata node, string prefix, bool isLast, 
            StringBuilder builder, HashSet<NodeMetadata> processedNodes, bool isRoot = false)
        {
            if (processedNodes.Contains(node))
                return;
                
            processedNodes.Add(node);
            
            // 添加当前节点到树中
            if (isRoot)
            {
                builder.AppendLine(node.DisplayName);
            }
            else
            {
                builder.AppendLine(prefix + (isLast ? "└── " : "├── ") + node.DisplayName);
            }
            
            // 获取按UI渲染顺序排序的子节点
            var sortedChildren = node.Children.OrderBy(c => c.RenderOrder).ThenBy(c => c.ListIndex).ToList();
            
            // 递归构建子节点
            for (int i = 0; i < sortedChildren.Count; i++)
            {
                bool isLastChild = (i == sortedChildren.Count - 1);
                string childPrefix = prefix + (isRoot ? "" : (isLast ? "    " : "│   "));
                BuildTreeViewRecursively(sortedChildren[i], childPrefix, isLastChild, builder, processedNodes, false);
            }
        }

        #endregion

        #region 验证方法

        /// <summary>
        /// 验证树结构的完整性
        /// </summary>
        public string ValidateTree()
        {
            var errors = new List<string>();
            
            // 检查所有节点是否都有正确的元数据
            foreach (var node in CollectAllNodes())
            {
                if (!_nodeMetadataMap.ContainsKey(node))
                {
                    errors.Add($"节点 {node.GetType().Name} 缺少元数据");
                }
            }
            
            // 检查循环引用
            foreach (var rootMetadata in _rootNodes)
            {
                var visited = new HashSet<NodeMetadata>();
                if (HasCircularReference(rootMetadata, visited))
                {
                    errors.Add($"检测到循环引用，根节点: {rootMetadata.DisplayName}");
                }
            }
            
            return errors.Count > 0 ? string.Join("\n", errors) : "Success";
        }

        /// <summary>
        /// 检查是否存在循环引用
        /// </summary>
        private bool HasCircularReference(NodeMetadata node, HashSet<NodeMetadata> visited)
        {
            if (visited.Contains(node))
                return true;
                
            visited.Add(node);
            
            foreach (var child in node.Children)
            {
                if (HasCircularReference(child, visited))
                    return true;
            }
            
            visited.Remove(node);
            return false;
        }

        #endregion

        #region 更新方法

        /// <summary>
        /// 标记树为脏数据，需要重建
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// 如果需要则重建树
        /// </summary>
        public void RefreshIfNeeded()
        {
            if (_isDirty)
            {
                RebuildTree();
            }
        }

        /// <summary>
        /// 添加节点后更新树结构
        /// </summary>
        public void OnNodeAdded(JsonNode node, string parentPath = null)
        {
            MarkDirty();
            RefreshIfNeeded();
        }

        /// <summary>
        /// 移除节点后更新树结构
        /// </summary>
        public void OnNodeRemoved(JsonNode node)
        {
            MarkDirty();
            RefreshIfNeeded();
        }

        /// <summary>
        /// 节点修改后更新树结构
        /// </summary>
        public void OnNodeModified(JsonNode node)
        {
            // 对于节点修改，通常不需要重建整个树，除非结构发生变化
            // 这里可以做更精细的控制
            MarkDirty();
        }

        /// <summary>
        /// 清理缓存（用于内存管理）
        /// </summary>
        public static void ClearCache()
        {
            _typeMemberCache.Clear();
            _userDefinedTypeCache.Clear();
            _containsJsonNodeCache.Clear();
        }

        #endregion

        #region 调试和分析方法

        /// <summary>
        /// 获取类型分析信息（用于调试）
        /// </summary>
        public string GetTypeAnalysisInfo(Type type)
        {
            var typeInfo = GetOrCreateTypeMemberInfo(type);
            var sb = new StringBuilder();
            
            sb.AppendLine($"类型分析: {type.Name}");
            sb.AppendLine($"命名空间: {type.Namespace}");
            sb.AppendLine($"是否用户定义类型: {IsUserDefinedType(type)}");
            sb.AppendLine($"是否包含JsonNode: {ContainsJsonNodeRecursively(type)}");
            sb.AppendLine();
            
            sb.AppendLine("嵌套JsonNode路径:");
            foreach (var path in typeInfo.NestedPaths)
            {
                sb.AppendLine($"  - {path.Path} (深度: {path.Depth})");
            }
            
            sb.AppendLine();
            sb.AppendLine("渲染成员:");
            foreach (var member in typeInfo.RenderMembers)
            {
                sb.AppendLine($"  - {member.Member.Name}: {member.ValueType.Name} (顺序: {member.RenderOrder})");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static string GetCacheStatistics()
        {
            return $"类型成员缓存: {_typeMemberCache.Count} " +
                   $"用户类型缓存: {_userDefinedTypeCache.Count} " +
                   $"JsonNode包含缓存: {_containsJsonNodeCache.Count}";
        }

        #endregion
    }
}