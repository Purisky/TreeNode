using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine.Profiling.Memory.Experimental;

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
        private Dictionary<JsonNode, int> _rootIndexCache;
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

            // 极小规模时线性搜索更快
            if (UseLinearSearch)
            {
                foreach (var kvp in _nodeMetadataMap)
                {
                    if (ReferenceEquals(kvp.Key, node))
                        return kvp.Value;
                }
                return null;
            }
            else
            {
                return _nodeMetadataMap.TryGetValue(node, out var metadata) ? metadata : null;
            }
        }

        /// <summary>
        /// 判断是否使用线性搜索
        /// 极小规模下线性搜索比字典查找更快
        /// </summary>
        private bool UseLinearSearch => _nodeMetadataMap.Count < 8;

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
            _isDirty = false;
            _asset.Nodes ??= new();
            if (_asset.Nodes.Count == 0) { return; }

            // 缓存根节点索引
            CacheRootIndexes();
             
            // 重建所有节点
            var nodeList = new List<(PAPath path, JsonNode node)>();

            for (int i = 0; i < _asset.Nodes.Count; i++)
            {
                nodeList.Clear();
                PAPath path = PAPath.Index(i);
                _asset.Nodes[i].CollectNodes(nodeList, path, -1);
                NodeMetadata metadata = new()
                {
                    Node = _asset.Nodes[i],
                    Path = path,
                    Depth = path.Depth,
                    RenderOrder = path.GetRenderOrder()
                };
                _nodeMetadataMap[_asset.Nodes[i]] = metadata;
                _rootNodes.Add(metadata);
                foreach (var (path_, node) in nodeList)
                {
                    NodeMetadata metadata_ = new NodeMetadata()
                    {
                        Node = node,
                        Path = path_,
                        Depth = path_.Depth,
                        RenderOrder = path_.GetRenderOrder()
                    };
                    _nodeMetadataMap[node] = metadata_;
                }
            }
            // 建立层次关系
            BuildHierarchyFromPaths();
        }

        /// <summary>
        /// 重建指定分支 - 处理单次操作的基础重建单元
        /// 集合内元素重建时，需要重建parent[index..]所有元素，先移除所有parent下索引>=index的节点内容
        /// </summary>
        /// <param name="path">操作的路径</param>
        /// <param name="node">相关的节点</param>
        public void RebuildBranch(PAPath path, JsonNode node)
        {
            EnsureTreeBuilt();
            
            if (path.IsEmpty)
            {
                return;
            }

            if (path.LastPart.IsIndex)
            {
                // 处理集合元素的重建
                RebuildCollectionBranch(path, node);
            }
            else
            {
                // 处理普通属性的重建
                RebuildPropertyBranch(path, node);
            }
        }

        /// <summary>
        /// 重建集合分支 - 处理集合元素的重建
        /// 当集合内元素重建时，需要重建parent[index..]所有元素，先移除所有parent下索引>=index的节点内容
        /// </summary>
        /// <param name="path">集合元素的路径</param>
        /// <param name="node">相关的节点</param>
        private void RebuildCollectionBranch(PAPath path, JsonNode node)
        {
            var parentPath = path.GetParent();
            int changedIndex = path.LastPart.Index;

            // 1. 先移除所有parent下索引>=index的节点内容
            RemoveCollectionItemsFromIndex(parentPath, changedIndex);

            // 2. 重新构建从指定索引开始的所有集合元素
            RebuildCollectionFromIndex(parentPath, changedIndex);
        }

        /// <summary>
        /// 重建普通属性分支 - 处理非集合属性的重建
        /// </summary>
        /// <param name="path">属性的路径</param>
        /// <param name="node">相关的节点</param>
        private void RebuildPropertyBranch(PAPath path, JsonNode node)
        {
            // 清理该分支下的所有元数据
            ClearBranchMetadata(path);
            
            // 重新构建该分支
            RebuildSingleBranch(path);
        }

        /// <summary>
        /// 移除集合中从指定索引开始的所有元素的元数据
        /// </summary>
        /// <param name="parentPath">父路径</param>
        /// <param name="fromIndex">起始索引</param>
        private void RemoveCollectionItemsFromIndex(PAPath parentPath, int fromIndex)
        {
            var itemsToRemove = new List<NodeMetadata>();

            // 收集需要移除的元数据
            foreach (var metadata in _nodeMetadataMap.Values.ToList())
            {
                // 检查是否是该集合下的元素，且索引 >= fromIndex
                if (IsCollectionItemToRemove(metadata.Path, parentPath, fromIndex))
                {
                    itemsToRemove.Add(metadata);
                }
            }

            // 移除收集的元数据
            foreach (var metadata in itemsToRemove)
            {
                RemoveMetadata(metadata);
            }
        }

        /// <summary>
        /// 判断是否是需要移除的集合项
        /// </summary>
        /// <param name="itemPath">项目路径</param>
        /// <param name="parentPath">父集合路径</param>
        /// <param name="fromIndex">起始索引</param>
        /// <returns>是否需要移除</returns>
        private bool IsCollectionItemToRemove(PAPath itemPath, PAPath parentPath, int fromIndex)
        {
            // 检查路径深度
            if (itemPath.Depth <= parentPath.Depth)
            {
                return false;
            }

            // 检查是否是父路径的子路径
            if (!itemPath.IsChildOf(parentPath) && !itemPath.Equals(parentPath))
            {
                return false;
            }

            // 获取在父路径基础上的相对路径
            var relativePath = GetRelativePath(itemPath, parentPath);
            if (relativePath.IsEmpty || relativePath.Parts.Length == 0)
            {
                return false;
            }

            // 检查第一个部分是否是索引且 >= fromIndex
            var firstPart = relativePath.Parts[0];
            return firstPart.IsIndex && firstPart.Index >= fromIndex;
        }

        /// <summary>
        /// 从指定索引重新构建集合元素
        /// </summary>
        /// <param name="parentPath">父路径</param>
        /// <param name="fromIndex">起始索引</param>
        private void RebuildCollectionFromIndex(PAPath parentPath, int fromIndex)
        {
            // 获取父节点
            var parentNode = GetNodeAtPath(parentPath);
            if (parentNode == null)
            {
                return;
            }

            // 收集从指定索引开始的所有节点
            var nodeList = new List<(PAPath path, JsonNode node)>();
            parentNode.CollectNodes(nodeList, parentPath, depth: -1);

            // 筛选出索引 >= fromIndex 的节点
            var filteredNodes = nodeList.Where(item => 
            {
                var relativePath = GetRelativePath(item.path, parentPath);
                if (relativePath.IsEmpty || relativePath.Parts.Length == 0)
                {
                    return false;
                }
                
                var firstPart = relativePath.Parts[0];
                return firstPart.IsIndex && firstPart.Index >= fromIndex;
            }).ToList();

            // 为这些节点创建元数据
            foreach (var (path, nodeItem) in filteredNodes)
            {
                CreateNodeMetadata(nodeItem, path);
            }

            // 重新建立层次关系
            BuildHierarchyFromPaths(filteredNodes.Select(item => item.path).ToArray());
        }

        /// <summary>
        /// 重新构建单个分支
        /// </summary>
        /// <param name="branchPath">分支路径</param>
        private void RebuildSingleBranch(PAPath branchPath)
        {
            var branchNode = GetNodeAtPath(branchPath);
            if (branchNode == null)
            {
                return;
            }

            // 收集该分支下的所有节点
            var nodeList = new List<(PAPath path, JsonNode node)>();
            branchNode.CollectNodes(nodeList, branchPath, depth: -1);

            // 为这些节点创建元数据
            foreach (var (path, node) in nodeList)
            {
                CreateNodeMetadata(node, path);
            }

            // 重新建立层次关系
            BuildHierarchyFromPaths(nodeList.Select(item => item.path).ToArray());
        }

        /// <summary>
        /// 获取指定路径的节点
        /// </summary>
        /// <param name="path">路径</param>
        /// <returns>节点或null</returns>
        private JsonNode GetNodeAtPath(PAPath path)
        {
            if (path.IsEmpty)
            {
                return null;
            }

            try
            {
                return PropertyAccessor.GetValue<JsonNode>(_asset.Nodes, path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建节点元数据
        /// </summary>
        /// <param name="node">节点</param>
        /// <param name="path">路径</param>
        private void CreateNodeMetadata(JsonNode node, PAPath path)
        {
            if (node == null || _nodeMetadataMap.ContainsKey(node))
            {
                return;
            }

            var metadata = new NodeMetadata
            {
                Node = node,
                Path = path,
                Depth = path.Depth,
                RenderOrder = path.GetRenderOrder()
            };

            _nodeMetadataMap[node] = metadata;
        }

        /// <summary>
        /// 移除元数据
        /// </summary>
        /// <param name="metadata">要移除的元数据</param>
        private void RemoveMetadata(NodeMetadata metadata)
        {
            // 从父节点的子节点列表中移除
            if (metadata.Parent != null)
            {
                metadata.Parent.Children.Remove(metadata);
            }
            else
            {
                // 从根节点列表中移除
                _rootNodes.Remove(metadata);
            }

            // 从映射表中移除
            _nodeMetadataMap.Remove(metadata.Node);
        }


        /// <summary>
        /// 重建集合操作 - 处理集合内元素的增删，更新索引和之后的所有节点
        /// </summary>
        /// <param name="changes">变更列表</param>
        public void RebuildTree((PAPath path, JsonNode node)[] changes)
        {
            if (changes == null || changes.Length == 0)
            {
                RebuildTree();
                return;
            }
            changes = FilterRedundantChanges(changes);
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].path.IsEmpty) { continue; }
                RebuildBranch(changes[i].path, changes[i].node);
            }
        }

        private static (PAPath path, JsonNode node)[] FilterRedundantChanges((PAPath path, JsonNode node)[] changes)
        {
            if (changes.Length <= 1) { return changes; }
            for (int i = 0; i < changes.Length - 1; i++)
            {
                if (changes[i].path.IsEmpty) { continue; }
                ref PAPath iPath = ref changes[i].path;
                for (int j = i + 1; j < changes.Length; j++)
                {
                    if (changes[j].path.IsEmpty) { continue; }
                    ref PAPath jPath = ref changes[j].path;
                    if (jPath.StartsWith(iPath))
                    {
                        changes[j] = (PAPath.Empty, null);
                        continue;
                    }
                    if (iPath.StartsWith(jPath))
                    {
                        changes[i] = (PAPath.Empty, null);
                        break;
                    }
                }
            }
            Dictionary<PAPath, (int index, int pos)> dict = new();
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].path.IsEmpty) { continue; }
                if (changes[i].path.ItemOfCollection)
                {
                    PAPath parent = changes[i].path.GetParent();
                    int index = changes[i].path.LastPart.Index;
                    if (dict.TryGetValue(parent, out (int index, int pos) old))
                    {
                        if (index < old.index)
                        {
                            dict[parent] = (index, i);
                            changes[i] = (PAPath.Empty, null);
                        }
                    }
                }
            }
            List<(PAPath path, JsonNode node)> list = new(changes);
            list.RemoveAll(n => n.path.IsEmpty);
            changes = list.ToArray();
            return changes;
        }
        /// <summary>
        /// 获取相对路径
        /// </summary>
        private PAPath GetRelativePath(PAPath fullPath, PAPath basePath)
        {
            if (basePath.IsEmpty) return fullPath;
            if (fullPath.Depth <= basePath.Depth) return new PAPath();

            int skipCount = basePath.Depth;
            var relativeParts = new PAPart[fullPath.Depth - skipCount];
            Array.Copy(fullPath.Parts, skipCount, relativeParts, 0, relativeParts.Length);
            
            return new PAPath(relativeParts);
        }

        /// <summary>
        /// 清理指定分支的元数据
        /// </summary>
        private void ClearBranchMetadata(PAPath branchPath)
        {
            var metadataToRemove = new List<NodeMetadata>();

            // 收集需要移除的元数据
            foreach (var metadata in _nodeMetadataMap.Values.ToList())
            {
                if (metadata.Path.Equals(branchPath) || metadata.Path.IsChildOf(branchPath))
                {
                    metadataToRemove.Add(metadata);
                }
            }

            // 移除元数据和相关关系
            foreach (var metadata in metadataToRemove)
            {
                // 从父节点的子节点列表中移除
                if (metadata.Parent != null)
                {
                    metadata.Parent.Children.Remove(metadata);
                }
                else
                {
                    // 从根节点列表中移除
                    _rootNodes.Remove(metadata);
                }

                // 从映射表中移除
                _nodeMetadataMap.Remove(metadata.Node);
            }
        }

        /// <summary>
        /// 兼容旧接口的重载方法
        /// </summary>
        /// <param name="targetPaths">目标路径数组</param>
        public void RebuildTree(PAPath[] targetPaths)
        {
            if (targetPaths == null || targetPaths.Length == 0)
            {
                RebuildTree();
                return;
            }

            // 转换为新格式，假设都是添加操作
            var targetChanges = targetPaths.Select(path => (path, (JsonNode)null)).ToArray();
            RebuildTree(targetChanges);
        }        /// <summary>
        /// 缓存根节点索引，避免重复的IndexOf调用
        /// </summary>
        private void CacheRootIndexes()
        {
            _rootIndexCache = new Dictionary<JsonNode, int>(_asset.Nodes.Count);
            for (int i = 0; i < _asset.Nodes.Count; i++)
            {
                _rootIndexCache[_asset.Nodes[i]] = i;
            }
        }
        #endregion

        #region 层次关系构建
        
        /// <summary>
        /// 构建或重建节点的层次关系
        /// </summary>
        /// <param name="targetPaths">要处理的特定路径，null表示处理所有节点</param>
        private void BuildHierarchyFromPaths(PAPath[] targetPaths = null)
        {
            // 创建路径到元数据的映射表，用于快速查找
            var pathToMetadata = new Dictionary<PAPath, NodeMetadata>(_nodeMetadataMap.Count);
            foreach (var metadata in _nodeMetadataMap.Values)
            {
                pathToMetadata[metadata.Path] = metadata;
            }

            // 确定要处理的元数据集合
            IEnumerable<NodeMetadata> targetMetadata;
            if (targetPaths == null)
            {
                // 处理所有节点
                targetMetadata = _nodeMetadataMap.Values;
            }
            else
            {
                // 只处理指定路径的节点
                targetMetadata = targetPaths
                    .Where(path => pathToMetadata.ContainsKey(path))
                    .Select(path => pathToMetadata[path]);
            }

            // 按深度排序，确保父节点先于子节点处理
            var sortedMetadata = targetMetadata.OrderBy(m => m.Depth).ToList();
            var affectedParents = new HashSet<NodeMetadata>();

            foreach (var metadata in sortedMetadata)
            {
                if (metadata.Depth == 0)
                {
                    // 处理根节点
                    SetupRootNode(metadata);
                }
                else
                {
                    // 处理子节点
                    var parent = SetupChildNode(metadata, pathToMetadata);
                    if (parent != null)
                    {
                        affectedParents.Add(parent);
                    }
                }
            }

            // 对所有受影响的父节点的子节点重新排序
            if (targetPaths == null)
            {
                // 全量重建，排序所有节点的子节点
                SortChildrenForParents(Enumerable.Empty<NodeMetadata>());
            }
            else
            {
                // 部分重建，只排序受影响的父节点
                SortChildrenForParents(affectedParents);
            }
        }

        /// <summary>
        /// 设置根节点
        /// </summary>
        private void SetupRootNode(NodeMetadata metadata)
        {
            // 设置根节点索引
            if (_rootIndexCache != null && _rootIndexCache.TryGetValue(metadata.Node, out int rootIndex))
            {
                metadata.RootIndex = rootIndex;
            }
            else
            {
                metadata.RootIndex = _asset.Nodes.IndexOf(metadata.Node);
            }

            // 添加到根节点列表（避免重复）
            if (!_rootNodes.Contains(metadata))
            {
                _rootNodes.Add(metadata);
            }
        }

        /// <summary>
        /// 设置子节点
        /// </summary>
        /// <param name="metadata">子节点元数据</param>
        /// <param name="pathToMetadata">路径到元数据的映射</param>
        /// <returns>父节点元数据</returns>
        private NodeMetadata SetupChildNode(NodeMetadata metadata, Dictionary<PAPath, NodeMetadata> pathToMetadata)
        {
            var parentPath = metadata.Path.GetParent();
            if (parentPath.IsEmpty)
            {
                return null;
            }

            // 查找父节点
            NodeMetadata parentMetadata = null;
            if (pathToMetadata.TryGetValue(parentPath, out parentMetadata))
            {
                // 在映射中找到了父节点
            }
            else
            {
                // 在现有节点中查找父节点
                var parentNode = GetNodeAtPath(parentPath);
                if (parentNode != null)
                {
                    parentMetadata = GetNodeMetadata(parentNode);
                }
            }

            if (parentMetadata != null)
            {
                var lastPart = metadata.Path.LastPart;

                // 设置父子关系
                metadata.Parent = parentMetadata;
                metadata.LocalPath = new PAPath(new[] { lastPart });
                metadata.IsMultiPort = metadata.Path.HasIndexer;

                // 设置列表索引
                if (lastPart.IsIndex)
                {
                    metadata.ListIndex = lastPart.Index;
                }

                // 添加到父节点的子节点列表（避免重复）
                if (!parentMetadata.Children.Contains(metadata))
                {
                    parentMetadata.Children.Add(metadata);
                }

                return parentMetadata;
            }

            return null;
        }

        /// <summary>
        /// 为指定的父节点集合排序子节点
        /// </summary>
        private void SortChildrenForParents(IEnumerable<NodeMetadata> parents)
        {
            // 如果是全量重建（没有指定特定父节点），为所有有子节点的节点排序
            if (!parents.Any())
            {
                foreach (var metadata in _nodeMetadataMap.Values)
                {
                    if (metadata.Children.Count > 0)
                    {
                        SortChildren(metadata);
                    }
                }
            }
            else
            {
                // 为指定的父节点排序
                foreach (var parent in parents)
                {
                    SortChildren(parent);
                }
            }
        }

        /// <summary>
        /// 智能排序子节点：根据子节点数量选择不同策略
        /// </summary>
        private void SortChildren(NodeMetadata parent)
        {
            if (parent.Children.Count > 2)
            {
                // 多个子节点时使用完整排序
                parent.Children = parent.Children
                    .OrderBy(c => c.RenderOrder)
                    .ThenBy(c => c.ListIndex)
                    .ToList();
            }
            else if (parent.Children.Count == 2)
            {
                // 两个子节点时使用简单比较交换
                var first = parent.Children[0];
                var second = parent.Children[1];

                bool shouldSwap = first.RenderOrder > second.RenderOrder ||
                                 (first.RenderOrder == second.RenderOrder && first.ListIndex > second.ListIndex);

                if (shouldSwap)
                {
                    parent.Children[0] = second;
                    parent.Children[1] = first;
                }
            }
            // 单个或零个子节点无需排序
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
                var rootPath = new PAPath($"[{rootMetadata.RootIndex}]");
                CalculatePathAndDepthRecursively(rootMetadata, rootPath, 0);
            }
        }

        /// <summary>
        /// 递归计算路径和深度
        /// </summary>
        private void CalculatePathAndDepthRecursively(NodeMetadata metadata, PAPath path, int depth)
        {
            metadata.Path = path;
            metadata.Depth = depth;

            // 按渲染顺序排序子节点
            var sortedChildren = metadata.Children.OrderBy(c => c.RenderOrder).ThenBy(c => c.ListIndex).ToList();

            foreach (var child in sortedChildren)
            {
                // 构建子节点的PAPath
                var childPath = path.Combine(child.LocalPath);
                CalculatePathAndDepthRecursively(child, childPath, depth + 1);
            }
        }

        #endregion

        #region 调试和分析方法

        /// <summary>
        /// 获取简化的类型分析信息（用于调试）
        /// </summary>
        public string GetTypeAnalysisInfo(Type type)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"类型分析: {type.Name}");
            sb.AppendLine($"命名空间: {type.Namespace}");
            sb.AppendLine($"是否为JsonNode: {typeof(JsonNode).IsAssignableFrom(type)}");

            return sb.ToString();
        }

        #endregion

        #region PropertyAccessor Integration Methods

        /// <summary>
        /// 获取带路径信息的所有 JsonNode
        /// </summary>
        /// <returns>包含路径信息的 JsonNode 枚举</returns>
        public IEnumerable<(JsonNode node, PAPath path, int depth)> GetJsonNodeHierarchy()
        {
            var nodeList = new List<(PAPath path, JsonNode node)>();
            _asset.Nodes.CollectNodes(nodeList, PAPath.Empty, depth: -1);

            foreach (var item in nodeList)
            {
                yield return (item.node, item.path, item.path.Depth);
            }
        }
        #endregion

        #region Editor Support Methods

        /// <summary>
        /// 通知有新节点被添加
        /// </summary>
        /// <param name="node">添加的节点</param>
        /// <param name="path">节点的目标路径</param>
        public void OnNodeAdded(JsonNode node, PAPath path)
        {
            if (node != null && !path.IsEmpty && !_nodeMetadataMap.ContainsKey(node))
            {
                // 使用智能分支重建
                RebuildBranch(path, node);
            }
        }

        /// <summary>
        /// 通知有节点被移除
        /// </summary>
        /// <param name="node">移除的节点</param>
        /// <param name="path">节点的路径</param>
        public void OnNodeRemoved(JsonNode node, PAPath path)
        {
            if (node != null && !path.IsEmpty && _nodeMetadataMap.ContainsKey(node))
            {
                RebuildBranch(path, node);
            }
        }

        /// <summary>
        /// 批量处理节点变更
        /// </summary>
        /// <param name="changes">变更列表，包含路径、节点和操作类型</param>
        public void OnNodesChanged((PAPath path, JsonNode node)[] changes) => RebuildTree(changes);
        public List<NodeMetadata> GetSortedNodes()
        {
            return GetNodes()
                .OrderBy(m => m.RenderOrder)
                .ThenBy(m => m.Path.ToString())
                .ToList();
        }
        
        public IEnumerable<NodeMetadata> GetNodes()
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.Values;
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
