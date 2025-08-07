using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        /// 紧凑内存布局的节点记录结构
        /// 使用值类型减少内存碎片和GC压力
        /// </summary>
        private struct NodeRecord
        {
            public int NodeId;              // 节点ID
            public int ParentId;            // 父节点ID (-1表示根节点)
            public int FirstChildIndex;     // 第一个子节点在子关系数组中的索引
            public short TypeId;            // 类型ID (用于快速类型查找)
            public short Depth;             // 节点深度
            public short ChildCount;        // 子节点数量
            public short RenderOrder;       // UI渲染顺序
        }

        /// <summary>
        /// 类型信息记录
        /// </summary>
        private struct TypeRecord
        {
            public Type Type;
            public string TypeName;
            public INodeAccessor Accessor;
        }

        #endregion

        #region 核心存储结构

        // 紧凑存储结构 - 数组优化内存布局
        private NodeRecord[] _nodes;
        private JsonNode[] _nodeObjects;
        private string[] _nodePaths;
        private List<int> _childRelations;  // 子节点ID列表，按FirstChildIndex索引
        
        // 快速查找索引
        private Dictionary<JsonNode, int> _nodeToId;
        private Dictionary<string, int> _pathToId;
        private Dictionary<Type, short> _typeToId;
        private TypeRecord[] _typeRecords;
        
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
        public JsonNodeTreeV2(TreeNodeAsset asset, INodeAccessorProvider accessorProvider = null)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _accessorProvider = accessorProvider ?? new ExpressionTreeAccessorGenerator();
            
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
            _nodes = new NodeRecord[_capacity];
            _nodeObjects = new JsonNode[_capacity];
            _nodePaths = new string[_capacity];
            _childRelations = new List<int>();
            
            _nodeToId = new Dictionary<JsonNode, int>();
            _pathToId = new Dictionary<string, int>();
            _typeToId = new Dictionary<Type, short>();
            _typeRecords = new TypeRecord[256]; // 最多256种类型
            
            _nodeCount = 0;
        }

        /// <summary>
        /// 扩展存储容量
        /// </summary>
        private void ExpandCapacity()
        {
            var newCapacity = _capacity * 2;
            Array.Resize(ref _nodes, newCapacity);
            Array.Resize(ref _nodeObjects, newCapacity);
            Array.Resize(ref _nodePaths, newCapacity);
            _capacity = newCapacity;
        }

        #endregion

        #region 核心重建方法

        /// <summary>
        /// 重新构建整个树结构 - 高性能版本
        /// </summary>
        public void RebuildTree()
        {
            using (new Timer("JsonNodeTreeV2.RebuildTree"))
            {
                // 1. 清理现有数据
                ClearExistingData();
                
                // 2. 收集所有节点 - O(N)
                var allNodes = CollectAllNodesOptimized();
                
                // 3. 分配存储空间
                EnsureCapacity(allNodes.Count);
                
                // 4. 并行处理节点基础信息
                ProcessNodesInParallel(allNodes);
                
                // 5. 构建父子关系和路径索引
                BuildRelationshipsAndPaths();
                
                // 6. 计算深度和渲染顺序
                CalculateDepthsAndOrders();
                
                _isDirty = false;
            }
        }

        /// <summary>
        /// 清理现有数据
        /// </summary>
        private void ClearExistingData()
        {
            _nodeCount = 0;
            _childRelations.Clear();
            _nodeToId.Clear();
            _pathToId.Clear();
            
            // 快速清零数组的关键部分
            if (_nodes.Length > 0)
            {
                Array.Clear(_nodes, 0, _nodeCount);
                Array.Clear(_nodeObjects, 0, _nodeCount);
                Array.Clear(_nodePaths, 0, _nodeCount);
            }
        }

        /// <summary>
        /// 高效收集所有节点 - 使用访问器优化
        /// </summary>
        private List<JsonNode> CollectAllNodesOptimized()
        {
            var allNodes = new HashSet<JsonNode>();
            var nodeQueue = new Queue<JsonNode>();
            
            // 从根节点开始
            foreach (var rootNode in _asset.Nodes)
            {
                if (rootNode != null)
                {
                    nodeQueue.Enqueue(rootNode);
                }
            }
            
            // 使用队列进行广度优先遍历，避免递归栈溢出
            while (nodeQueue.Count > 0)
            {
                var currentNode = nodeQueue.Dequeue();
                
                if (allNodes.Contains(currentNode))
                    continue;
                    
                allNodes.Add(currentNode);
                
                // 使用高性能访问器收集子节点
                try
                {
                    var accessor = _accessorProvider.GetAccessor(currentNode.GetType());
                    var children = new List<JsonNode>();
                    accessor.CollectChildren(currentNode, children);
                    
                    foreach (var child in children)
                    {
                        if (child != null && !allNodes.Contains(child))
                        {
                            nodeQueue.Enqueue(child);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 如果访问器失败，回退到反射方式
                    Debug.LogWarning($"Accessor failed for {currentNode.GetType().Name}, falling back to reflection: {ex.Message}");
                    CollectChildrenFallback(currentNode, nodeQueue, allNodes);
                }
            }
            
            return allNodes.ToList();
        }

        /// <summary>
        /// 回退到反射方式收集子节点
        /// </summary>
        private void CollectChildrenFallback(JsonNode node, Queue<JsonNode> queue, HashSet<JsonNode> visited)
        {
            var type = node.GetType();
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                .Where(m => m.GetCustomAttribute<ChildAttribute>() != null || 
                           m.GetCustomAttribute<TitlePortAttribute>() != null);
            
            foreach (var member in members)
            {
                try
                {
                    var value = member.GetValue(node);
                    if (value is JsonNode childNode && !visited.Contains(childNode))
                    {
                        queue.Enqueue(childNode);
                    }
                    else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode child && !visited.Contains(child))
                            {
                                queue.Enqueue(child);
                            }
                        }
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
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
        /// 并行处理节点基础信息
        /// </summary>
        private void ProcessNodesInParallel(List<JsonNode> allNodes)
        {
            _nodeCount = allNodes.Count;
            
            // 创建类型映射
            var typeSet = new HashSet<Type>();
            foreach (var node in allNodes)
            {
                typeSet.Add(node.GetType());
            }
            
            RegisterTypes(typeSet);
            
            // 并行设置节点基础信息
            System.Threading.Tasks.Parallel.For(0, _nodeCount, i =>
            {
                var node = allNodes[i];
                var typeId = _typeToId[node.GetType()];
                
                _nodes[i] = new NodeRecord
                {
                    NodeId = i,
                    ParentId = -1, // 稍后设置
                    FirstChildIndex = -1, // 稍后设置
                    TypeId = typeId,
                    Depth = 0, // 稍后计算
                    ChildCount = 0, // 稍后计算
                    RenderOrder = 0 // 稍后计算
                };
                
                _nodeObjects[i] = node;
                _nodeToId[node] = i;
            });
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
                    _typeRecords[typeId] = new TypeRecord
                    {
                        Type = type,
                        TypeName = type.Name,
                        Accessor = _accessorProvider.GetAccessor(type)
                    };
                    typeId++;
                }
            }
        }

        /// <summary>
        /// 构建父子关系和路径索引
        /// </summary>
        private void BuildRelationshipsAndPaths()
        {
            // 设置根节点路径
            for (int i = 0; i < _asset.Nodes.Count && i < _nodeCount; i++)
            {
                var rootNode = _asset.Nodes[i];
                if (_nodeToId.TryGetValue(rootNode, out var nodeId))
                {
                    _nodePaths[nodeId] = $"[{i}]";
                    _pathToId[_nodePaths[nodeId]] = nodeId;
                }
            }
            
            // 构建父子关系
            var childRelationsTemp = new List<List<int>>();
            for (int i = 0; i < _nodeCount; i++)
            {
                childRelationsTemp.Add(new List<int>());
            }
            
            for (int parentId = 0; parentId < _nodeCount; parentId++)
            {
                var parentNode = _nodeObjects[parentId];
                var accessor = _typeRecords[_nodes[parentId].TypeId].Accessor;
                
                try
                {
                    var childrenWithMetadata = new List<(JsonNode, string, int)>();
                    accessor.CollectChildrenWithMetadata(parentNode, childrenWithMetadata);
                    
                    foreach (var (childNode, localPath, renderOrder) in childrenWithMetadata)
                    {
                        if (_nodeToId.TryGetValue(childNode, out var childId))
                        {
                            // 设置父子关系
                            _nodes[childId].ParentId = parentId;
                            _nodes[childId].RenderOrder = (short)renderOrder;
                            childRelationsTemp[parentId].Add(childId);
                            
                            // 构建完整路径
                            var fullPath = string.IsNullOrEmpty(_nodePaths[parentId]) ? localPath : $"{_nodePaths[parentId]}.{localPath}";
                            _nodePaths[childId] = fullPath;
                            _pathToId[fullPath] = childId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to build relationships for {parentNode.GetType().Name}: {ex.Message}");
                }
            }
            
            // 优化子关系存储
            OptimizeChildRelations(childRelationsTemp);
        }

        /// <summary>
        /// 优化子关系存储
        /// </summary>
        private void OptimizeChildRelations(List<List<int>> childRelationsTemp)
        {
            _childRelations.Clear();
            
            for (int parentId = 0; parentId < _nodeCount; parentId++)
            {
                var children = childRelationsTemp[parentId];
                if (children.Count == 0)
                {
                    _nodes[parentId].FirstChildIndex = -1;
                    _nodes[parentId].ChildCount = 0;
                }
                else
                {
                    // 按渲染顺序排序
                    children.Sort((a, b) =>
                    {
                        var orderA = _nodes[a].RenderOrder;
                        var orderB = _nodes[b].RenderOrder;
                        return orderA.CompareTo(orderB);
                    });
                    
                    _nodes[parentId].FirstChildIndex = _childRelations.Count;
                    _nodes[parentId].ChildCount = (short)children.Count;
                    _childRelations.AddRange(children);
                }
            }
        }

        /// <summary>
        /// 计算深度和渲染顺序
        /// </summary>
        private void CalculateDepthsAndOrders()
        {
            // 从根节点开始计算深度
            for (int i = 0; i < _asset.Nodes.Count && i < _nodeCount; i++)
            {
                var rootNode = _asset.Nodes[i];
                if (_nodeToId.TryGetValue(rootNode, out var rootId))
                {
                    CalculateDepthRecursively(rootId, 0);
                }
            }
        }

        /// <summary>
        /// 递归计算深度
        /// </summary>
        private void CalculateDepthRecursively(int nodeId, short depth)
        {
            _nodes[nodeId].Depth = depth;
            
            var firstChildIndex = _nodes[nodeId].FirstChildIndex;
            var childCount = _nodes[nodeId].ChildCount;
            
            if (firstChildIndex >= 0 && childCount > 0)
            {
                for (int i = 0; i < childCount; i++)
                {
                    var childId = _childRelations[firstChildIndex + i];
                    CalculateDepthRecursively(childId, (short)(depth + 1));
                }
            }
        }

        #endregion

        #region 高性能查询接口

        /// <summary>
        /// 根据路径获取节点 - 直接索引查找 O(1)
        /// </summary>
        public JsonNode GetNodeByPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !_pathToId.TryGetValue(path, out var nodeId))
            {
                return null;
            }
            
            return _nodeObjects[nodeId];
        }

        /// <summary>
        /// 获取子节点 - 基于紧凑存储的高效迭代
        /// </summary>
        public IEnumerable<JsonNode> GetChildren(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                yield break;
            }
            
            var firstChildIndex = _nodes[nodeId].FirstChildIndex;
            var childCount = _nodes[nodeId].ChildCount;
            
            if (firstChildIndex >= 0 && childCount > 0)
            {
                for (int i = 0; i < childCount; i++)
                {
                    var childId = _childRelations[firstChildIndex + i];
                    yield return _nodeObjects[childId];
                }
            }
        }

        /// <summary>
        /// 获取子节点数量 - O(1)
        /// </summary>
        public int GetChildCount(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return 0;
            }
            
            return _nodes[nodeId].ChildCount;
        }

        /// <summary>
        /// 检查是否有子节点 - O(1)
        /// </summary>
        public bool HasChildren(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return false;
            }
            
            return _nodes[nodeId].ChildCount > 0;
        }

        /// <summary>
        /// 获取节点深度 - O(1)
        /// </summary>
        public int GetDepth(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return -1;
            }
            
            return _nodes[nodeId].Depth;
        }

        /// <summary>
        /// 获取父节点 - O(1)
        /// </summary>
        public JsonNode GetParent(JsonNode node)
        {
            if (node == null || !_nodeToId.TryGetValue(node, out var nodeId))
            {
                return null;
            }
            
            var parentId = _nodes[nodeId].ParentId;
            return parentId >= 0 ? _nodeObjects[parentId] : null;
        }

        /// <summary>
        /// 获取根节点列表
        /// </summary>
        public IEnumerable<JsonNode> GetRootNodes()
        {
            return _asset.Nodes.Where(n => n != null);
        }

        /// <summary>
        /// 获取所有节点（按深度优先顺序）
        /// </summary>
        public IEnumerable<JsonNode> GetAllNodes()
        {
            for (int i = 0; i < _nodeCount; i++)
            {
                yield return _nodeObjects[i];
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
            // 对于小幅修改，可以考虑增量更新
            // 这里先简单标记为脏数据
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
                   $"Types: {_typeToId.Count}, Child Relations: {_childRelations.Count}";
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
                    var node = _nodes[i];
                    
                    // 检查子节点关系
                    if (node.FirstChildIndex >= 0)
                    {
                        if (node.FirstChildIndex + node.ChildCount > _childRelations.Count)
                            return false;
                            
                        for (int j = 0; j < node.ChildCount; j++)
                        {
                            var childId = _childRelations[node.FirstChildIndex + j];
                            if (childId < 0 || childId >= _nodeCount) return false;
                            if (_nodes[childId].ParentId != i) return false;
                        }
                    }
                    
                    // 检查父节点关系
                    if (node.ParentId >= 0)
                    {
                        if (node.ParentId >= _nodeCount) return false;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}