using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    /// <summary>
    /// JsonNode树状逻辑处理器
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
            public PAPath Path { get; set; }
            public PAPath LocalPath { get; set; }
            public int Depth { get; set; }
            public NodeMetadata Parent { get; set; }
            public List<NodeMetadata> Children { get; set; } = new();
            public int RootIndex { get; set; } = -1;
            public int ListIndex { get; set; } = 0;
            public bool IsMultiPort { get; set; }
            public int RenderOrder { get; set; } = 0; // UI渲染顺序
            
            public bool IsRoot => Parent == null;
            public string DisplayName => Node.GetInfo();
        }

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
        }

        #endregion

        #region 公共查询接口

        /// <summary>
        /// 获取所有根节点
        /// </summary>
        public IReadOnlyList<NodeMetadata> GetRootNodes()
        {
            EnsureTreeBuilt();
            return _rootNodes.AsReadOnly();
        }

        /// <summary>
        /// 获取指定节点的元数据
        /// </summary>
        public NodeMetadata GetNodeMetadata(JsonNode node)
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.TryGetValue(node, out var metadata) ? metadata : null;
        }

        /// <summary>
        /// 获取指定节点的所有子节点
        /// </summary>
        public IReadOnlyList<NodeMetadata> GetChildren(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            return metadata?.Children?.AsReadOnly() ?? new List<NodeMetadata>().AsReadOnly();
        }

        /// <summary>
        /// 获取指定节点的父节点
        /// </summary>
        public NodeMetadata GetParent(JsonNode node)
        {
            var metadata = GetNodeMetadata(node);
            return metadata?.Parent;
        }

        /// <summary>
        /// 获取所有节点
        /// </summary>
        public IEnumerable<NodeMetadata> GetAllNodes()
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.Values;
        }

        /// <summary>
        /// 标记为需要重建
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// 确保树已构建
        /// </summary>
        private void EnsureTreeBuilt()
        {
            if (_isDirty)
            {
                RebuildTree();
                _isDirty = false;
            }
        }

        #endregion

        #region 树构建核心逻辑

        /// <summary>
        /// 重新构建整个树结构 - 基于 PropertyAccessor
        /// </summary>
        public void RebuildTree()
        {
            _nodeMetadataMap.Clear();
            _rootNodes.Clear();
            
            if (_asset?.Nodes == null || _asset.Nodes.Count == 0)
                return;

            // 步骤1：使用现有的 PropertyAccessor.CollectNodes 收集所有节点
            var nodeList = new List<(PAPath path, JsonNode node)>();
            PropertyAccessor.CollectNodes(_asset, nodeList, PAPath.Empty, depth: -1);
            
            // 步骤2：创建元数据映射
            foreach (var (path, node) in nodeList)
            {
                var metadata = new NodeMetadata
                {
                    Node = node,
                    Path = path,
                    Depth = path.Depth,
                    RenderOrder = path.GetRenderOrder()
                };
                
                _nodeMetadataMap[node] = metadata;
            }
            
            // 步骤3：建立层次关系（简化版）
            BuildHierarchyFromPaths();
        }

        /// <summary>
        /// 收集所有JsonNode - 基于 PropertyAccessor（保持向后兼容）
        /// </summary>
        [Obsolete("Use PropertyAccessor.CollectNodes instead")]
        private HashSet<JsonNode> CollectAllNodes()
        {
            // 临时实现，直接使用 PropertyAccessor.CollectNodes
            var nodeList = new List<(PAPath path, JsonNode node)>();
            PropertyAccessor.CollectNodes(_asset, nodeList, PAPath.Empty, depth: -1);
            return new HashSet<JsonNode>(nodeList.Select(item => item.node));
        }

        /// <summary>
        /// 递归收集节点 - 使用 TypeCacheSystem 统一缓存
        /// </summary>
        private void CollectNodesRecursively(JsonNode node, HashSet<JsonNode> collected)
        {
            if (node == null || collected.Contains(node))
                return;
                
            collected.Add(node);
            
            var typeInfo = TypeCacheSystem.GetTypeInfo(node.GetType());
            
            // 处理直接的JsonNode成员
            foreach (var member in typeInfo.GetJsonNodeMembers())
            {
                try
                {
                    var value = member.Getter(node);
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
            foreach (var member in typeInfo.GetCollectionMembers())
            {
                try
                {
                    var value = member.Getter(node);
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
            var typeInfo = TypeCacheSystem.GetTypeInfo(node.GetType());
            
            // 获取可能包含嵌套结构的成员并进行运行时递归检查
            foreach (var member in typeInfo.GetNestedCandidateMembers())
            {
                try
                {
                    var value = member.Getter(node);
                    if (value != null)
                    {
                        // 递归检查嵌套的JsonNode
                        CollectNestedJsonNodesFromValue(value, collected);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
        }

        /// <summary>
        /// 从值中递归收集嵌套的JsonNode - 统一处理方法
        /// </summary>
        private void CollectNestedJsonNodesFromValue(object value, HashSet<JsonNode> collected)
        {
            if (value == null) return;

            // 直接是JsonNode的情况
            if (value is JsonNode jsonNode)
            {
                CollectNodesRecursively(jsonNode, collected);
                return;
            }

            // 集合类型的情况
            if (value is System.Collections.IEnumerable enumerable)
            {
                CollectNodesFromCollection(enumerable, collected);
                return;
            }

            // 可能包含嵌套JsonNode的对象
            var valueType = value.GetType();
            var typeInfo = TypeCacheSystem.GetTypeInfo(valueType);
            
            if (typeInfo.ContainsJsonNode)
            {
                // 处理直接的JsonNode成员
                foreach (var member in typeInfo.GetJsonNodeMembers())
                {
                    try
                    {
                        var nestedValue = member.Getter(value);
                        if (nestedValue is JsonNode nestedNode)
                        {
                            CollectNodesRecursively(nestedNode, collected);
                        }
                    }
                    catch
                    {
                        // 跳过无法访问的成员
                    }
                }

                // 处理集合成员
                foreach (var member in typeInfo.GetCollectionMembers())
                {
                    try
                    {
                        var nestedValue = member.Getter(value);
                        if (nestedValue is System.Collections.IEnumerable nestedEnumerable)
                        {
                            CollectNodesFromCollection(nestedEnumerable, collected);
                        }
                    }
                    catch
                    {
                        // 跳过无法访问的成员
                    }
                }

                // 递归处理可能包含嵌套结构的成员
                foreach (var member in typeInfo.GetNestedCandidateMembers())
                {
                    try
                    {
                        var nestedValue = member.Getter(value);
                        if (nestedValue != null)
                        {
                            CollectNestedJsonNodesFromValue(nestedValue, collected);
                        }
                    }
                    catch
                    {
                        // 跳过无法访问的成员
                    }
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
                else
                {
                    var itemTypeInfo = TypeCacheSystem.GetTypeInfo(item.GetType());
                    if (itemTypeInfo.IsUserDefinedType && itemTypeInfo.ContainsJsonNode)
                    {
                        CollectNestedJsonNodesFromValue(item, collected);
                    }
                }
            }
        }

        #endregion

        #region 层次关系构建

        /// <summary>
        /// 基于路径信息建立层次关系
        /// </summary>
        private void BuildHierarchyFromPaths()
        {
            // 按路径深度排序处理
            var sortedMetadata = _nodeMetadataMap.Values.OrderBy(m => m.Depth).ToList();
            
            foreach (var metadata in sortedMetadata)
            {
                if (metadata.Depth == 0)
                {
                    // 根节点
                    metadata.RootIndex = _asset.Nodes.IndexOf(metadata.Node);
                    _rootNodes.Add(metadata);
                }
                else
                {
                    // 查找父节点
                    var parentPath = metadata.Path.GetParent();
                    if (!parentPath.IsEmpty)
                    {
                        try
                        {
                            var parentNode = PropertyAccessor.GetValue<JsonNode>(_asset, parentPath);
                            
                            if (parentNode != null && _nodeMetadataMap.TryGetValue(parentNode, out var parentMetadata))
                            {
                                var lastPart = metadata.Path.GetLastPart();
                                
                                metadata.Parent = parentMetadata;
                                
                                // 设置 LocalPath - 创建包含最后一部分的路径
                                metadata.LocalPath = new PAPath(new[] { lastPart });
                                metadata.IsMultiPort = metadata.Path.IsMultiPortPath();
                                
                                // 设置 ListIndex（如果是集合中的项）
                                if (lastPart.IsIndex)
                                {
                                    metadata.ListIndex = lastPart.Index;
                                }
                                
                                parentMetadata.Children.Add(metadata);
                            }
                        }
                        catch
                        {
                            // 跳过无法访问的父节点
                        }
                    }
                }
            }
            
            // 排序子节点
            foreach (var metadata in _nodeMetadataMap.Values)
            {
                if (metadata.Children.Count > 0)
                {
                    metadata.Children = metadata.Children
                        .OrderBy(c => c.RenderOrder)
                        .ThenBy(c => c.ListIndex)
                        .ToList();
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
            
            // 去重处理：移除重复的子节点
            DeduplicateChildren();
        }

        /// <summary>
        /// 对所有节点的Children列表进行去重处理
        /// </summary>
        private void DeduplicateChildren()
        {
            foreach (var kvp in _nodeMetadataMap)
            {
                var metadata = kvp.Value;
                if (metadata.Children.Count <= 1)
                    continue; // 0个或1个子节点无需去重
                
                // 使用Dictionary进行高效去重，键为JsonNode，值为最优的NodeMetadata
                var uniqueChildren = new Dictionary<JsonNode, NodeMetadata>();
                
                foreach (var child in metadata.Children)
                {
                    if (uniqueChildren.TryGetValue(child.Node, out var existingChild))
                    {
                        // 如果已存在，选择优先级更高的（RenderOrder更小，或ListIndex更小）
                        if (child.RenderOrder < existingChild.RenderOrder ||
                            (child.RenderOrder == existingChild.RenderOrder && child.ListIndex < existingChild.ListIndex))
                        {
                            uniqueChildren[child.Node] = child;
                        }
                    }
                    else
                    {
                        uniqueChildren[child.Node] = child;
                    }
                }
                
                // 重新构建Children列表，保持排序
                metadata.Children = uniqueChildren.Values
                    .OrderBy(child => child.RenderOrder)
                    .ThenBy(child => child.ListIndex)
                    .ToList();
            }
        }

        /// <summary>
        /// 为指定节点建立子节点关系 - 考虑UI渲染顺序
        /// </summary>
        private void BuildChildRelationshipsWithOrder(JsonNode parentNode, NodeMetadata parentMetadata)
        {
            var typeInfo = TypeCacheSystem.GetTypeInfo(parentNode.GetType());
            
            // 按渲染顺序处理成员 - 使用统一的成员列表
            foreach (var member in typeInfo.GetAllMembers())
            {
                try
                {
                    var value = member.Getter(parentNode);
                    ProcessChildMemberWithOrder(member, value, parentMetadata);
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
        }

        /// <summary>
        /// 处理子成员 - 考虑渲染顺序和特殊类型
        /// </summary>
        private void ProcessChildMemberWithOrder(TypeCacheSystem.UnifiedMemberInfo memberInfo, object value, NodeMetadata parentMetadata)
        {
            if (value == null) return;
            
            // 直接的JsonNode子节点
            if (memberInfo.Category == TypeCacheSystem.MemberCategory.JsonNode && value is JsonNode childNode)
            {
                if (_nodeMetadataMap.TryGetValue(childNode, out var childMetadata))
                {
                    childMetadata.Parent = parentMetadata;
                    childMetadata.LocalPath = memberInfo.Name;
                    childMetadata.IsMultiPort = false;
                    childMetadata.RenderOrder = memberInfo.RenderOrder;
                    parentMetadata.Children.Add(childMetadata);
                }
            }
            // 集合类型 - 支持直接JsonNode集合和嵌套节点集合
            else if (memberInfo.IsMultiValue && value is System.Collections.IEnumerable enumerable)
            {
                ProcessCollectionChildren(enumerable, memberInfo, parentMetadata);
            }
        }

        /// <summary>
        /// 处理集合类型的子节点
        /// </summary>
        private void ProcessCollectionChildren(System.Collections.IEnumerable collection, TypeCacheSystem.UnifiedMemberInfo memberInfo, NodeMetadata parentMetadata)
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
                    childMetadata.LocalPath = $"{memberInfo.Name}[{index}]"; // 修复：包含索引信息
                    childMetadata.IsMultiPort = true;
                    childMetadata.ListIndex = index;
                    childMetadata.RenderOrder = memberInfo.RenderOrder;
                    parentMetadata.Children.Add(childMetadata);
                }
                // 包含嵌套JsonNode的用户定义类型（如TimeValue）
                else
                {
                    var itemTypeInfo = TypeCacheSystem.GetTypeInfo(item.GetType());
                    if (itemTypeInfo.IsUserDefinedType && itemTypeInfo.ContainsJsonNode)
                    {
                        ProcessNestedNodesInCollectionItem(item, index, memberInfo, parentMetadata);
                    }
                }

                index++;
            }
        }

        /// <summary>
        /// 处理集合项中的嵌套节点
        /// </summary>
        private void ProcessNestedNodesInCollectionItem(object item, int itemIndex, TypeCacheSystem.UnifiedMemberInfo memberInfo, NodeMetadata parentMetadata)
        {
            var itemTypeInfo = TypeCacheSystem.GetTypeInfo(item.GetType());
            
            // 直接处理JsonNode成员
            foreach (var jsonNodeMember in itemTypeInfo.GetJsonNodeMembers())
            {
                try
                {
                    var nestedNode = jsonNodeMember.Getter(item);
                    if (nestedNode is JsonNode jsonNode && _nodeMetadataMap.TryGetValue(jsonNode, out var childMetadata))
                    {
                        childMetadata.Parent = parentMetadata;
                        childMetadata.LocalPath = $"{memberInfo.Name}[{itemIndex}].{jsonNodeMember.Name}";
                        childMetadata.IsMultiPort = true;
                        childMetadata.ListIndex = itemIndex;
                        childMetadata.RenderOrder = memberInfo.RenderOrder + jsonNodeMember.RenderOrder;
                        parentMetadata.Children.Add(childMetadata);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }

            // 递归处理可能包含嵌套JsonNode的成员
            foreach (var nestedCandidate in itemTypeInfo.GetNestedCandidateMembers())
            {
                try
                {
                    var nestedValue = nestedCandidate.Getter(item);
                    if (nestedValue != null)
                    {
                        ProcessNestedJsonNodesInItemRecursive(nestedValue, itemIndex, memberInfo, nestedCandidate.Name, parentMetadata, memberInfo.RenderOrder + nestedCandidate.RenderOrder);
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
        }

        /// <summary>
        /// 递归处理集合项中深层嵌套的JsonNode
        /// </summary>
        private void ProcessNestedJsonNodesInItemRecursive(object value, int itemIndex, TypeCacheSystem.UnifiedMemberInfo rootMemberInfo, string currentPath, NodeMetadata parentMetadata, int baseRenderOrder)
        {
            if (value == null) return;

            if (value is JsonNode jsonNode && _nodeMetadataMap.TryGetValue(jsonNode, out var childMetadata))
            {
                childMetadata.Parent = parentMetadata;
                childMetadata.LocalPath = $"{rootMemberInfo.Name}[{itemIndex}].{currentPath}";
                childMetadata.IsMultiPort = true;
                childMetadata.ListIndex = itemIndex;
                childMetadata.RenderOrder = baseRenderOrder;
                parentMetadata.Children.Add(childMetadata);
                return;
            }

            var valueTypeInfo = TypeCacheSystem.GetTypeInfo(value.GetType());
            if (valueTypeInfo.ContainsJsonNode)
            {
                // 继续深度搜索JsonNode
                foreach (var member in valueTypeInfo.GetJsonNodeMembers())
                {
                    try
                    {
                        var nestedValue = member.Getter(value);
                        if (nestedValue is JsonNode nestedNode)
                        {
                            ProcessNestedJsonNodesInItemRecursive(nestedNode, itemIndex, rootMemberInfo, $"{currentPath}.{member.Name}", parentMetadata, baseRenderOrder + member.RenderOrder);
                        }
                    }
                    catch
                    {
                        // 跳过无法访问的成员
                    }
                }

                // 递归处理可能包含嵌套JsonNode的成员
                foreach (var nestedCandidate in valueTypeInfo.GetNestedCandidateMembers())
                {
                    try
                    {
                        var nestedValue = nestedCandidate.Getter(value);
                        if (nestedValue != null)
                        {
                            ProcessNestedJsonNodesInItemRecursive(nestedValue, itemIndex, rootMemberInfo, $"{currentPath}.{nestedCandidate.Name}", parentMetadata, baseRenderOrder + nestedCandidate.RenderOrder);
                        }
                    }
                    catch
                    {
                        // 跳过无法访问的成员
                    }
                }
            }
        }

        /// <summary>
        /// 构建子节点路径
        /// </summary>
        private string BuildChildPath(string parentPath, NodeMetadata child, JsonNode parentNode)
        {
            // 现在LocalPath已经包含了正确的索引信息，直接使用即可
            return $"{parentPath}.{child.LocalPath}";
        }

        #endregion

        #region 路径和深度计算

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

        #endregion

        #region 调试和分析方法

        /// <summary>
        /// 获取类型分析信息（用于调试）
        /// </summary>
        public string GetTypeAnalysisInfo(Type type)
        {
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var sb = new StringBuilder();
            
            sb.AppendLine($"类型分析: {type.Name}");
            sb.AppendLine($"命名空间: {type.Namespace}");
            sb.AppendLine($"是否用户定义类型: {typeInfo.IsUserDefinedType}");
            sb.AppendLine($"是否包含JsonNode: {typeInfo.ContainsJsonNode}");
            sb.AppendLine();
            
            sb.AppendLine("JsonNode成员:");
            foreach (var member in typeInfo.GetJsonNodeMembers())
            {
                sb.AppendLine($"  - {member.Name} ({member.ValueType.Name}) [Order: {member.RenderOrder}]");
            }
            sb.AppendLine();
            
            sb.AppendLine("集合成员:");
            foreach (var member in typeInfo.GetCollectionMembers())
            {
                sb.AppendLine($"  - {member.Name} ({member.ValueType.Name}) [Order: {member.RenderOrder}]");
            }
            sb.AppendLine();
            
            sb.AppendLine("可能包含嵌套结构的成员:");
            foreach (var member in typeInfo.GetNestedCandidateMembers())
            {
                sb.AppendLine($"  - {member.Name} ({member.ValueType.Name}) [Order: {member.RenderOrder}]");
            }
            sb.AppendLine();
            
            sb.AppendLine("所有成员:");
            foreach (var member in typeInfo.GetAllMembers())
            {
                sb.AppendLine($"  - {member.MemberType}.{member.Name} ({member.ValueType.Name}) - {member.Category} [Order: {member.RenderOrder}]");
            }
            
            return sb.ToString();
        }

        #endregion
        
        #region PropertyAccessor Integration Methods
        
        /// <summary>
        /// 使用 PropertyAccessor 收集所有 JsonNode
        /// </summary>
        /// <returns>所有 JsonNode 的集合</returns>
        public HashSet<JsonNode> GetAllJsonNodes()
        {
            var nodeList = new List<(PAPath path, JsonNode node)>();
            PropertyAccessor.CollectNodes(_asset, nodeList, PAPath.Empty, depth: -1);
            return new HashSet<JsonNode>(nodeList.Select(item => item.node));
        }
        
        /// <summary>
        /// 获取带路径信息的所有 JsonNode
        /// </summary>
        /// <returns>包含路径信息的 JsonNode 枚举</returns>
        public IEnumerable<(JsonNode node, PAPath path, int depth)> GetJsonNodeHierarchy()
        {
            var nodeList = new List<(PAPath path, JsonNode node)>();
            PropertyAccessor.CollectNodes(_asset, nodeList, PAPath.Empty, depth: -1);
            
            foreach (var item in nodeList)
            {
                yield return (item.node, item.path, item.path.Depth);
            }
        }
        
        /// <summary>
        /// 通过路径获取 JsonNode
        /// </summary>
        /// <param name="path">节点路径</param>
        /// <returns>找到的 JsonNode，如果不存在则返回 null</returns>
        public JsonNode GetNodeByPath(PAPath path)
        {
            try
            {
                return PropertyAccessor.GetValue<JsonNode>(_asset, path);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 通过字符串路径获取 JsonNode
        /// </summary>
        /// <param name="pathString">字符串路径</param>
        /// <returns>找到的 JsonNode，如果不存在则返回 null</returns>
        public JsonNode GetNodeByPath(string pathString)
        {
            try
            {
                var path = PAPath.Create(pathString);
                return PropertyAccessor.GetValue<JsonNode>(_asset, path);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 批量获取指定路径的 JsonNode
        /// </summary>
        /// <param name="paths">路径集合</param>
        /// <returns>路径到 JsonNode 的映射</returns>
        public Dictionary<PAPath, JsonNode> GetNodesByPaths(IEnumerable<PAPath> paths)
        {
            var result = new Dictionary<PAPath, JsonNode>();
            
            foreach (var path in paths)
            {
                var node = GetNodeByPath(path);
                if (node != null)
                {
                    result[path] = node;
                }
            }
            
            return result;
        }

        #endregion
        
        #region Editor Support Methods
        
        /// <summary>
        /// 通知有新节点被添加
        /// </summary>
        /// <param name="node">添加的节点</param>
        public void OnNodeAdded(JsonNode node)
        {
            if (node != null && !_nodeMetadataMap.ContainsKey(node))
            {
                RebuildTree();
            }
        }
        
        /// <summary>
        /// 通知有新节点被添加到指定路径
        /// </summary>
        /// <param name="node">添加的节点</param>
        /// <param name="path">节点路径</param>
        public void OnNodeAdded(JsonNode node, string path)
        {
            if (node != null && !_nodeMetadataMap.ContainsKey(node))
            {
                RebuildTree();
            }
        }
        
        /// <summary>
        /// 通知有节点被移除
        /// </summary>
        /// <param name="node">移除的节点</param>
        public void OnNodeRemoved(JsonNode node)
        {
            if (node != null && _nodeMetadataMap.ContainsKey(node))
            {
                RebuildTree();
            }
        }
        
        /// <summary>
        /// 获取排序后的节点列表
        /// </summary>
        /// <returns>排序后的节点元数据列表</returns>
        public List<NodeMetadata> GetSortedNodes()
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.Values
                .OrderBy(m => m.RenderOrder)
                .ThenBy(m => m.Path.ToString())
                .ToList();
        }
        
        /// <summary>
        /// 验证树结构的完整性
        /// </summary>
        /// <returns>验证是否通过</returns>
        public bool ValidateTree()
        {
            try
            {
                EnsureTreeBuilt();
                return _nodeMetadataMap.Count > 0;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 如果需要则刷新树结构
        /// </summary>
        public void RefreshIfNeeded()
        {
            if (_isDirty)
            {
                RebuildTree();
            }
        }
        
        /// <summary>
        /// 获取所有节点的路径信息
        /// </summary>
        /// <returns>路径和显示名称的元组列表</returns>
        public List<(string path, string displayName)> GetAllNodePaths()
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.Values
                .Select(m => (m.Path.ToString(), m.DisplayName))
                .OrderBy(item => item.Item1)
                .ToList();
        }
        
        /// <summary>
        /// 获取总节点数量
        /// </summary>
        public int TotalNodeCount => _nodeMetadataMap.Count;
        
        /// <summary>
        /// 获取树形视图的字符串表示
        /// </summary>
        /// <returns>树形结构的文本表示</returns>
        public string GetTreeView()
        {
            EnsureTreeBuilt();
            
            if (_rootNodes.Count == 0)
            {
                return "Empty Tree";
            }
            
            var sb = new StringBuilder();
            for (int i = 0; i < _rootNodes.Count; i++)
            {
                bool isLast = i == _rootNodes.Count - 1;
                BuildTreeView(_rootNodes[i], sb, "", isLast);
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// 递归构建树形视图
        /// </summary>
        private void BuildTreeView(NodeMetadata metadata, StringBuilder sb, string prefix, bool isLast)
        {
            string connector = isLast ? "└── " : "├── ";
            string typeName = metadata.Node?.GetType().Name ?? "Unknown";
            sb.AppendLine($"{prefix}{connector}{metadata.DisplayName} ({typeName})");
            
            string childPrefix = prefix + (isLast ? "    " : "│   ");
            
            for (int i = 0; i < metadata.Children.Count; i++)
            {
                bool isLastChild = i == metadata.Children.Count - 1;
                BuildTreeView(metadata.Children[i], sb, childPrefix, isLastChild);
            }
        }
        
        #endregion
    }
}
