using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TreeNode.Utility;
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
            public TypeCacheSystem.TypeReflectionInfo ReflectionInfo { get; set; }
            public PAPath Path { get; set; }
            public PAPath LocalPath { get; set; }
            public NodeMetadata Parent { get; set; }
            public List<NodeMetadata> Children { get; set; } = new();
            public bool IsMultiPort { get; set; }
            public bool IsRoot => Parent == null;
            public string DisplayName => Node.GetInfo();

            public NodeMetadata(JsonNode node, PAPath path)
            {
                Node = node;
                Path = path;
                ReflectionInfo = TypeCacheSystem.GetTypeInfo(node.GetType());
                Children = new();
            }
            public void SortChildren(Dictionary<JsonNode, NodeMetadata> dict)
            {
                List<(PAPath, JsonNode)> list = ListPool<(PAPath, JsonNode)>.GetList();
                Node.CollectNodes(list, Path, 1);
                Children.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    Children.Add(dict[list[i].Item2]);
                }
                list.Release();
            }
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
        /// 获取指定节点的元数据
        /// </summary>
        public NodeMetadata GetNodeMetadata(JsonNode node)
        {
            EnsureTreeBuilt();
            return _nodeMetadataMap.TryGetValue(node, out var metadata) ? metadata : null;
        }
        public void MarkDirty()
        {
            _isDirty = true;
        }
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

        public void RebuildTree()
        {
            _nodeMetadataMap.Clear();
            _rootNodes.Clear();
            _isDirty = false;
            _asset.Nodes ??= new();
            if (_asset.Nodes.Count == 0) { return; }
            var nodeList = ListPool<(PAPath path, JsonNode node)>.GetList();
            for (int i = 0; i < _asset.Nodes.Count; i++)
            {
                PAPath path = PAPath.Index(i);
                NodeMetadata root = BuildNode(path, _asset.Nodes[i], nodeList);
                root.LocalPath = path;
            }
            nodeList.Release();
        }

        public NodeMetadata BuildNode(PAPath path, JsonNode node, List<(PAPath path, JsonNode node)> list)
        {
            NodeMetadata metadata = new(node, path);
            if (path.Root)
            {
                _rootNodes.Add(metadata);
            }
            _nodeMetadataMap[node] = metadata;
            list.Clear();
            node.CollectNodes(list, path, 1);
            var temp = ListPool<(PAPath path, JsonNode node)>.GetList();
            for (int i = 0; i < list.Count; i++)
            {
                NodeMetadata child = BuildNode(list[i].path, list[i].node, temp);
                child.Parent = metadata;
                child.LocalPath =  list[i].path.GetSubPath(path.Depth);
                metadata.Children.Add(child);
            }
            temp.Release();
            return metadata;
        }
        public void RebuildBranch(PAPath path, JsonNode node, bool remove)
        {
            EnsureTreeBuilt();

            if (path.IsEmpty)
            {
                return;
            }

            if (path.LastPart.IsIndex)
            {
                // 处理集合元素的重建
                RebuildCollectionBranch(path, node, remove);
            }
            else
            {
                // 处理普通属性的重建
                RebuildFieldBranch(path, node, remove);
            }
        }
        private void RebuildCollectionBranch(PAPath path, JsonNode node, bool remove)
        {
            var parentPath = path.GetParent();
            int changedIndex = path.LastPart.Index;
            RemoveCollectionItemsFromIndex(parentPath, changedIndex);
            if (remove) { return; }
            BuildCollectionFromIndex(parentPath, changedIndex);
        }
        private void RebuildFieldBranch(PAPath path, JsonNode node, bool remove)
        {
            // 清理该分支下的所有元数据
            ClearBranchMetadata(path);
            if (remove) { return; }
            var list = ListPool<(PAPath path, JsonNode node)>.GetList();
            int index = 0;
            var jsonnodes = ListPool<(int depth, JsonNode node)>.GetList();
            _asset.Nodes.GetAllInPath(ref path, ref index, jsonnodes);
            JsonNode parent = jsonnodes.Last().node == node ? jsonnodes[^2].node : node;
            NodeMetadata parentMeta = GetNodeMetadata(parent);
            BuildNode(path, node, list);
            parentMeta.SortChildren(_nodeMetadataMap);
            list.Release();
        }
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
            if (!itemPath.IsChildOf(parentPath)) { return false; }
            ref PAPart Index = ref itemPath.Parts[parentPath.Depth];
            return Index.IsIndex && Index.Index >= fromIndex;
        }

        /// <summary>
        /// 从指定索引重新构建集合元素
        /// </summary>
        /// <param name="parentPath">父路径</param>
        /// <param name="fromIndex">起始索引</param>
        private void BuildCollectionFromIndex(PAPath parentPath, int fromIndex)
        {
            JsonNode parent = null;
            NodeMetadata parentMeta = null;
            var nodeList = ListPool<(PAPath path, JsonNode node)>.GetList();
            if (!parentPath.IsEmpty)
            {
                parent = GetNodeAtPath(parentPath);
                if (parent == null)
                {
                    return;
                }
                parentMeta = GetNodeMetadata(GetNodeAtPath(parentPath));
                parent.CollectNodes(nodeList, parentPath, 1);
                if (nodeList.Count == 0) { return; }
            }
            else
            {
                _asset.Nodes.CollectNodes(nodeList, PAPath.Empty, 1);
            }
            var temp = ListPool<(PAPath path, JsonNode node)>.GetList();
            for (int i = fromIndex; i < nodeList.Count; i++)
            {
                PAPath path = parentPath.Append(i);
                NodeMetadata metadata = BuildNode(path, nodeList[i].node, temp);
                metadata.LocalPath = PAPath.Index(i);
                if (parentPath.IsEmpty)
                {
                    _rootNodes.Add(metadata);
                }
                else
                {
                    parentMeta.Children.Add(metadata);
                }
            }
            temp.Release();
            nodeList.Release();
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
                int index = 0;
                return _asset.Nodes.GetValueInternal<JsonNode>(ref path, ref index);
            }
            catch
            {
                return null;
            }
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
        public void RebuildTree((PAPath path, JsonNode node,bool remove)[] changes)
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
                RebuildBranch(changes[i].path, changes[i].node, changes[i].remove);
            }
        }

        private static (PAPath path, JsonNode node,bool remove)[] FilterRedundantChanges((PAPath path, JsonNode node, bool remove)[] changes)
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
                        changes[j] = (PAPath.Empty, null,true);
                        continue;
                    }
                    if (iPath.StartsWith(jPath))
                    {
                        changes[i] = (PAPath.Empty, null, true);
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
                            changes[i] = (PAPath.Empty, null, true);
                        }
                    }
                }
            }
            List<(PAPath path, JsonNode node,bool remove)> list = new(changes);
            list.RemoveAll(n => n.path.IsEmpty);
            changes = list.ToArray();
            return changes;
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
                RebuildBranch(path, node,false);
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
                RebuildBranch(path, node, true);
            }
        }

        /// <summary>
        /// 批量处理节点变更
        /// </summary>
        /// <param name="changes">变更列表，包含路径、节点和操作类型</param>
        public void OnNodesChanged((PAPath path, JsonNode node,bool remove)[] changes) => RebuildTree(changes);
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
