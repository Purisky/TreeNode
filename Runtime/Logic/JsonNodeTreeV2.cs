using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TreeNode.Runtime.Generated;
using TreeNode.Runtime.Logic;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime.Logic
{
    /// <summary>
    /// 高性能JsonNodeTree V2 - 基于依赖注入的访问器
    /// 使用紧凑内存布局和高性能访问器实现极致性能
    /// </summary>
    public sealed class JsonNodeTreeV2
    {
        #region 内部数据结构

        /// <summary>
        /// 紧凑内存布局的节点记录结构 - 合并优化
        /// </summary>
        private struct CompactNodeRecord
        {
            public JsonNode Node;           // 节点引用
            public string Path;             // 节点路径
            public int ParentId;            // 父节点ID (-1表示根节点)
            public int FirstChildIndex;     // 第一个子节点在子关系数组中的索引
            public short TypeId;            // 类型ID (用于快速类型查找)
            public short Depth;             // 节点深度
            public short ChildCount;        // 子节点数量
            public short RenderOrder;       // UI渲染顺序
        }

        #endregion

        #region 核心存储结构 - 优化版本

        // 合并的紧凑存储结构 - 减少内存访问和提高缓存局部性
        private CompactNodeRecord[] _compactNodes;
        private List<int> _childRelations;  // 子节点ID列表，按FirstChildIndex索引
        
        // 优化的查找索引 - 预缓存访问器
        private Dictionary<JsonNode, int> _nodeToId;
        private Dictionary<string, int> _pathToId;
        private Dictionary<Type, short> _typeToId;
        
        // 静态的访问器缓存 - 优化方案1：减少重复创建
        private static readonly ConcurrentDictionary<Type, INodeAccessor> _staticAccessorCache = new();
        
        // 访问器提供者
        private readonly INodeAccessorProvider _accessorProvider;
        private readonly TreeNodeAsset _asset;
        
        // 容量和统计
        private int _nodeCount;
        private int _capacity;
        private bool _isDirty = true;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public JsonNodeTreeV2(TreeNodeAsset asset, [NotNull]INodeAccessorProvider accessorProvider)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _accessorProvider = accessorProvider ?? throw new ArgumentNullException(nameof(accessorProvider));
            
            // 静态访问器缓存无需初始化
            
            _capacity = 1024; // 初始容量
            InitializeStorage();
            RebuildTree();
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取节点总数
        /// </summary>
        public int NodeCount => _nodeCount;

        /// <summary>
        /// 是否需要重建
        /// </summary>
        public bool IsDirty => _isDirty;

        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化存储结构
        /// </summary>
        private void InitializeStorage()
        {
            _compactNodes = new CompactNodeRecord[_capacity];
            _childRelations = new List<int>();
            
            _nodeToId = new Dictionary<JsonNode, int>();
            _pathToId = new Dictionary<string, int>();
            _typeToId = new Dictionary<Type, short>();
            
            _nodeCount = 0;
        }

        /// <summary>
        /// 扩展存储容量
        /// </summary>
        private void ExpandCapacity()
        {
            var newCapacity = _capacity * 2;
            Array.Resize(ref _compactNodes, newCapacity);
            _capacity = newCapacity;
        }

        #endregion

        #region 核心重建方法 - 优化版本

        /// <summary>
        /// 重新构建整个树结构 - 优化的单次遍历版本
        /// </summary>
        public void RebuildTree()
        {
            // 1. 清理现有数据
            ClearExistingData();
            
            // 2. 收集所有节点并预缓存访问器
            var allNodes = CollectAllNodesAndCacheAccessors();
            
            // 3. 分配存储空间
            EnsureCapacity(allNodes.Count);
            
            // 4. 优化的单次处理：合并多个阶段到一次遍历中
            BuildTreeOptimized(allNodes);
            
            _isDirty = false;
        }

        /// <summary>
        /// 清理现有数据
        /// </summary>
        private void ClearExistingData()
        {
            _nodeToId.Clear();
            _pathToId.Clear();
            _childRelations.Clear();
            
            // 清理访问器缓存中不再需要的条目
            // 保留缓存以提高重复重建的性能
            
            _nodeCount = 0;
        }

        /// <summary>
        /// 收集所有节点并预缓存访问器 - 优化方案1
        /// </summary>
        private List<JsonNode> CollectAllNodesAndCacheAccessors()
        {
            var allNodes = new List<JsonNode>();
            var visited = new HashSet<JsonNode>();
            var uniqueTypes = new HashSet<Type>();
            
            // 获取根节点并开始收集
            if (_asset?.Nodes != null)
            {
                foreach (var rootNode in _asset.Nodes.Where(n => n != null))
                {
                    CollectNodeTreeRecursiveOptimized(rootNode, allNodes, visited, uniqueTypes);
                }
            }
            
            // 预缓存所有访问器 - 避免重复获取
            PreCacheAccessors(uniqueTypes);
            
            return allNodes;
        }
        
        /// <summary>
        /// 递归收集节点并记录类型 - 优化版本
        /// </summary>
        private void CollectNodeTreeRecursiveOptimized(JsonNode node, List<JsonNode> allNodes, 
            HashSet<JsonNode> visited, HashSet<Type> uniqueTypes)
        {
            if (node == null || visited.Contains(node))
                return;
                
            visited.Add(node);
            allNodes.Add(node);
            uniqueTypes.Add(node.GetType()); // 记录类型用于预缓存
            
            try
            {
                // 获取预缓存的访问器（如果有）或使用提供者
                var accessor = GetCachedAccessor(node.GetType());
                var children = new List<JsonNode>();
                accessor.CollectChildren(node, children);
                
                // 递归处理子节点
                foreach (var child in children.Where(c => c != null))
                {
                    CollectNodeTreeRecursiveOptimized(child, allNodes, visited, uniqueTypes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to collect children for {node.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 预缓存访问器 - 优化方案1：使用静态缓存
        /// </summary>
        private void PreCacheAccessors(HashSet<Type> uniqueTypes)
        {
            foreach (var type in uniqueTypes)
            {
                if (!_staticAccessorCache.ContainsKey(type))
                {
                    try
                    {
                        var accessor = _accessorProvider.GetAccessor(type);
                        _staticAccessorCache.TryAdd(type, accessor);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to cache accessor for {type.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 获取缓存的访问器 - 使用静态缓存
        /// </summary>
        private INodeAccessor GetCachedAccessor(Type nodeType)
        {
            if (_staticAccessorCache.TryGetValue(nodeType, out var cachedAccessor))
            {
                return cachedAccessor;
            }
            
            // 如果缓存中没有，获取并缓存
            var accessor = _accessorProvider.GetAccessor(nodeType);
            _staticAccessorCache.TryAdd(nodeType, accessor);
            return accessor;
        }

        /// <summary>
        /// 确保存储容量足够
        /// </summary>
        private void EnsureCapacity(int requiredCapacity)
        {
            while (_capacity < requiredCapacity)
            {
                ExpandCapacity();
            }
        }

        /// <summary>
        /// 优化的树构建 - 合并多个处理阶段 (优化方案3)
        /// </summary>
        private void BuildTreeOptimized(List<JsonNode> allNodes)
        {
            _nodeCount = allNodes.Count;
            
            // 第一阶段：创建类型映射和节点基础信息
            CreateTypeMappingAndBasicInfo(allNodes);
            
            // 第二阶段：构建父子关系和路径 - 合并处理
            BuildRelationshipsAndPathsOptimized();
            
            // 第三阶段：优化子关系存储
            OptimizeChildRelationsStorage();
        }

        /// <summary>
        /// 创建类型映射和节点基础信息
        /// </summary>
        private void CreateTypeMappingAndBasicInfo(List<JsonNode> allNodes)
        {
            // 创建类型映射
            var typeSet = new HashSet<Type>();
            foreach (var node in allNodes)
            {
                typeSet.Add(node.GetType());
            }
            
            RegisterTypes(typeSet);
            
            // 设置节点基础信息
            for (int i = 0; i < _nodeCount; i++)
            {
                var node = allNodes[i];
                var typeId = _typeToId[node.GetType()];
                
                _compactNodes[i] = new CompactNodeRecord
                {
                    Node = node,
                    Path = "", // 稍后设置
                    ParentId = -1, // 稍后设置
                    FirstChildIndex = -1, // 稍后设置
                    TypeId = typeId,
                    Depth = 0, // 稍后计算
                    ChildCount = 0, // 稍后计算
                    RenderOrder = 0 // 稍后计算
                };
                
                _nodeToId[node] = i;
            }
        }
        
        /// <summary>
        /// 注册类型
        /// </summary>
        private void RegisterTypes(HashSet<Type> types)
        {
            short typeId = 0;
            foreach (var type in types)
            {
                if (!_typeToId.ContainsKey(type))
                {
                    _typeToId[type] = typeId;
                    typeId++;
                }
            }
        }

        /// <summary>
        /// 构建父子关系和路径 - 优化合并版本
        /// </summary>
        private void BuildRelationshipsAndPathsOptimized()
        {
            // 第一阶段：设置根节点路径
            for (int i = 0; i < _asset.Nodes.Count && i < _nodeCount; i++)
            {
                var rootNode = _asset.Nodes[i];
                if (_nodeToId.TryGetValue(rootNode, out var nodeId))
                {
                    var rootPath = $"[{i}]";
                    var record = _compactNodes[nodeId];
                    record.Path = rootPath;
                    _compactNodes[nodeId] = record;
                    _pathToId[rootPath] = nodeId;
                }
            }
            
            // 第二阶段：单次遍历构建关系和路径
            var childRelationsTemp = new Dictionary<int, List<int>>();
            
            for (int parentId = 0; parentId < _nodeCount; parentId++)
            {
                var parentRecord = _compactNodes[parentId];
                var parentNode = parentRecord.Node;
                var accessor = GetCachedAccessor(parentNode.GetType()); // 直接使用静态缓存获取访问器
                var localChildren = new List<int>();
                
                try
                {
                    var childrenWithMetadata = new List<(JsonNode, string, int)>();
                    accessor.CollectChildrenWithMetadata(parentNode, childrenWithMetadata);
                    
                    foreach (var (childNode, localPath, renderOrder) in childrenWithMetadata)
                    {
                        if (_nodeToId.TryGetValue(childNode, out var childId))
                        {
                            // 设置父子关系
                            var childRecord = _compactNodes[childId];
                            childRecord.ParentId = parentId;
                            childRecord.RenderOrder = (short)renderOrder;
                            
                            // 构建完整路径
                            var parentPath = parentRecord.Path;
                            var fullPath = string.IsNullOrEmpty(parentPath) ? localPath : $"{parentPath}.{localPath}";
                            childRecord.Path = fullPath;
                            _compactNodes[childId] = childRecord;
                            _pathToId[fullPath] = childId;
                            
                            localChildren.Add(childId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to build relationships for {parentNode.GetType().Name}: {ex.Message}");
                }
                
                if (localChildren.Count > 0)
                {
                    childRelationsTemp[parentId] = localChildren;
                }
            }
            
            // 第三阶段：优化子关系存储和计算深度
            OptimizeChildRelationsAndDepth(childRelationsTemp);
        }

        /// <summary>
        /// 优化子关系存储和计算深度 - 合并操作
        /// </summary>
        private void OptimizeChildRelationsAndDepth(Dictionary<int, List<int>> childRelationsTemp)
        {
            _childRelations.Clear();
            
            for (int parentId = 0; parentId < _nodeCount; parentId++)
            {
                var parentRecord = _compactNodes[parentId];
                
                if (childRelationsTemp.TryGetValue(parentId, out var children))
                {
                    // 按渲染顺序排序
                    children.Sort((a, b) =>
                    {
                        var orderA = _compactNodes[a].RenderOrder;
                        var orderB = _compactNodes[b].RenderOrder;
                        return orderA.CompareTo(orderB);
                    });
                    
                    parentRecord.FirstChildIndex = _childRelations.Count;
                    parentRecord.ChildCount = (short)children.Count;
                    _childRelations.AddRange(children);
                }
                else
                {
                    parentRecord.FirstChildIndex = -1;
                    parentRecord.ChildCount = 0;
                }
                
                _compactNodes[parentId] = parentRecord;
            }
            
            // 计算深度 - 从根节点开始
            CalculateDepthsOptimized();
        }

        /// <summary>
        /// 优化的深度计算
        /// </summary>
        private void CalculateDepthsOptimized()
        {
            for (int i = 0; i < _asset.Nodes.Count && i < _nodeCount; i++)
            {
                var rootNode = _asset.Nodes[i];
                if (_nodeToId.TryGetValue(rootNode, out var rootId))
                {
                    CalculateDepthRecursive(rootId, 0);
                }
            }
        }
        
        /// <summary>
        /// 递归计算深度
        /// </summary>
        private void CalculateDepthRecursive(int nodeId, short depth)
        {
            var nodeRecord = _compactNodes[nodeId];
            nodeRecord.Depth = depth;
            _compactNodes[nodeId] = nodeRecord;
            
            var firstChildIndex = nodeRecord.FirstChildIndex;
            var childCount = nodeRecord.ChildCount;
            
            if (firstChildIndex >= 0 && childCount > 0)
            {
                for (int i = 0; i < childCount; i++)
                {
                    var childId = _childRelations[firstChildIndex + i];
                    CalculateDepthRecursive(childId, (short)(depth + 1));
                }
            }
        }

        /// <summary>
        /// 优化子关系存储 - 简化版本
        /// </summary>
        private void OptimizeChildRelationsStorage()
        {
            // 这个方法在优化版本中已经合并到其他方法中
            // 保留为兼容性接口
        }

        #endregion

        #region 高性能查询接口

        /// <summary>
        /// 根据路径获取节点 O(1)
        /// </summary>
        public JsonNode GetNodeByPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !_pathToId.TryGetValue(path, out var nodeId))
            {
                return null;
            }
            
            return nodeId < _nodeCount ? _compactNodes[nodeId].Node : null;
        }

        /// <summary>
        /// 获取子节点
        /// </summary>
        public IEnumerable<JsonNode> GetChildren(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                yield break;
            }
            
            if (nodeId >= _nodeCount) yield break;
            
            var record = _compactNodes[nodeId];
            var firstChildIndex = record.FirstChildIndex;
            var childCount = record.ChildCount;
            
            if (firstChildIndex >= 0 && childCount > 0)
            {
                for (int i = 0; i < childCount; i++)
                {
                    var childId = _childRelations[firstChildIndex + i];
                    yield return _compactNodes[childId].Node;
                }
            }
        }

        /// <summary>
        /// 获取子节点数量 O(1)
        /// </summary>
        public int GetChildCount(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return 0;
            }
            
            return nodeId < _nodeCount ? _compactNodes[nodeId].ChildCount : 0;
        }

        /// <summary>
        /// 检查是否有子节点 O(1)
        /// </summary>
        public bool HasChildren(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return false;
            }
            
            return nodeId < _nodeCount && _compactNodes[nodeId].ChildCount > 0;
        }

        /// <summary>
        /// 获取节点深度 O(1)
        /// </summary>
        public int GetDepth(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return -1;
            }
            
            return nodeId < _nodeCount ? _compactNodes[nodeId].Depth : -1;
        }

        /// <summary>
        /// 获取父节点 O(1)
        /// </summary>
        public JsonNode GetParent(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return null;
            }
            
            if (nodeId >= _nodeCount) return null;
            
            var parentId = _compactNodes[nodeId].ParentId;
            return parentId >= 0 ? _compactNodes[parentId].Node : null;
        }

        /// <summary>
        /// 获取所有节点（按深度优先顺序）
        /// </summary>
        public IEnumerable<JsonNode> GetAllNodes()
        {
            for (int i = 0; i < _nodeCount; i++)
            {
                yield return _compactNodes[i].Node;
            }
        }

        #endregion

        #region 更新和维护方法

        /// <summary>
        /// 标记为需要重建
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// 如果需要则重建
        /// </summary>
        public void RefreshIfNeeded()
        {
            if (_isDirty)
            {
                RebuildTree();
            }
        }

        /// <summary>
        /// 节点添加后的更新
        /// </summary>
        public void OnNodeAdded(JsonNode node)
        {
            MarkDirty();
        }

        /// <summary>
        /// 节点移除后的更新
        /// </summary>
        public void OnNodeRemoved(JsonNode node)
        {
            MarkDirty();
        }

        /// <summary>
        /// 节点修改后的更新
        /// </summary>
        public void OnNodeModified(JsonNode node)
        {
            MarkDirty();
        }

        #endregion

        #region 调试和分析方法

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public string GetPerformanceStats()
        {
            return $"Nodes: {_nodeCount}, Capacity: {_capacity}, " +
                   $"Types: {_typeToId.Count}, Child Relations: {_childRelations.Count}, " +
                   $"Static Cached Accessors: {_staticAccessorCache.Count}";
        }

        /// <summary>
        /// 验证树结构完整性
        /// </summary>
        public bool ValidateIntegrity()
        {
            try
            {
                // 检查基本约束
                if (_nodeCount > _capacity) return false;
                if (_nodeToId.Count != _nodeCount) return false;
                if (_pathToId.Count != _nodeCount) return false;
                
                // 检查父子关系一致性
                for (int i = 0; i < _nodeCount; i++)
                {
                    var record = _compactNodes[i];
                    
                    // 检查子节点关系
                    if (record.FirstChildIndex >= 0)
                    {
                        if (record.FirstChildIndex + record.ChildCount > _childRelations.Count)
                            return false;
                            
                        for (int j = 0; j < record.ChildCount; j++)
                        {
                            var childId = _childRelations[record.FirstChildIndex + j];
                            if (childId < 0 || childId >= _nodeCount) return false;
                            if (_compactNodes[childId].ParentId != i) return false;
                        }
                    }
                    
                    // 检查父节点关系
                    if (record.ParentId >= 0)
                    {
                        if (record.ParentId >= _nodeCount) return false;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 静态缓存不需要在实例销毁时清理
            // 只在应用程序退出时清理：ClearStaticCache()
        }

        /// <summary>
        /// 清理静态访问器缓存 - 应用程序级别的清理
        /// </summary>
        public static void ClearStaticCache()
        {
            _staticAccessorCache.Clear();
        }
        #endregion
    }
}