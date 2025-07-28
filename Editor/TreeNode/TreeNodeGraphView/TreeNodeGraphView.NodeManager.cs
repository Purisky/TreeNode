using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using TreeNode.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//节点管理系统
    {
        public List<ViewNode> ViewNodes;
        public Dictionary<JsonNode, ViewNode> NodeDic;

        // 逻辑层树结构处理器 - 改为立即初始化
        private JsonNodeTree _nodeTree;
        public JsonNodeTree NodeTree => _nodeTree;
        #region 节点管理

        public virtual void AddNode(JsonNode node)
        {
            // 记录节点创建操作
            var location = NodeLocation.Root(Asset.Data.Nodes.Count);
            var createOperation = new NodeCreateOperation(node, location, this);
            Window.History.RecordOperation(createOperation);
            
            Asset.Data.Nodes.Add(node);
            NodeTree.OnNodeAdded(node);
            Window.History.AddStep();
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                // 记录节点创建操作
                var location = NodeLocation.Root(Asset.Data.Nodes.Count);
                var createOperation = new NodeCreateOperation(node, location, this);
                Window.History.RecordOperation(createOperation);
                
                Asset.Data.Nodes.Add(node);
                NodeTree.OnNodeAdded(node);
                return true;
            }
            try
            {
                object parent = PropertyAccessor.GetParentObject(Asset.Data.Nodes, path, out string last);
                object oldValue = PropertyAccessor.GetValue<object>(parent, last);
                if (oldValue is null)
                {
                    Type parentType = parent.GetType();
                    Type valueType = parentType.GetMember(last).First().GetValueType();
                    if (valueType.Inherited(typeof(IList)))
                    {
                        oldValue = Activator.CreateInstance(valueType);
                        PropertyAccessor.SetValue(parent, last, oldValue);
                    }
                }
                
                // 记录节点移动操作
                JsonNode parentNode = parent as JsonNode;
                if (parentNode != null)
                {
                    var fromLocation = NodeLocation.Root(-1); // 从根移动
                    int listIndex = 0;
                    if (oldValue is IList targetList)
                    {
                        listIndex = targetList.Count;
                    }
                    var toLocation = NodeLocation.Child(parentNode, last, oldValue is IList, listIndex);
                    var moveOperation = new NodeMoveOperation(node, fromLocation, toLocation, this);
                    Window.History.RecordOperation(moveOperation);
                }
                
                if (oldValue is IList nodeList)
                {
                    nodeList.Add(node);
                }
                else
                {
                    PropertyAccessor.SetValue(parent, last, node);
                }
                NodeTree.OnNodeAdded(node, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error setting node by path: {e.Message}");
                return false;
            }
        }

        void RemoveNode(JsonNode node)
        {
            // 记录节点删除操作
            int index = Asset.Data.Nodes.IndexOf(node);
            var location = NodeLocation.Root(index);
            var deleteOperation = new NodeDeleteOperation(node, location, this);
            Window.History.RecordOperation(deleteOperation);
            
            Asset.Data.Nodes.Remove(node);
            NodeTree.OnNodeRemoved(node);
        }

        public ViewNode AddViewNode(JsonNode node)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; }

            if (node.PrefabData != null)
            {
                viewNode = new PrefabViewNode(node, this);
            }
            else
            {
                viewNode = new ViewNode(node, this);
            }
            viewNode.SetPosition(new Rect(node.Position, new()));
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);

            // ✅ 移除子节点递归创建逻辑 - 连接将在批量创建阶段统一处理
            return viewNode;
        }

        /// <summary>
        /// 专用于工具添加节点的方法 - 支持立即连接创建
        /// 解决MCPTools等外部工具的连接缺失问题
        /// </summary>
        public ViewNode AddViewNodeWithConnection(JsonNode node, string nodePath)
        {
            // 1. 创建ViewNode（使用现有的AddViewNode方法）
            ViewNode viewNode = AddViewNode(node);
            
            // 2. 如果指定了路径，尝试创建连接
            if (!string.IsNullOrEmpty(nodePath))
            {
                CreateToolNodeConnection(viewNode, nodePath);
            }
            
            return viewNode;
        }

        /// <summary>
        /// 为工具添加的节点创建连接 - 智能ListView端口查找
        /// </summary>
        private void CreateToolNodeConnection(ViewNode childViewNode, string nodePath)
        {
            try
            {
                // 通过现有的GetPort方法查找端口
                var childPort = GetPort(nodePath);
                if (childPort != null && childViewNode.ParentPort != null)
                {
                    // 如果端口在ListView中，需要等待ListView初始化
                    var listView = childPort.GetFirstAncestorOfType<ListView>();
                    if (listView != null)
                    {
                        // ListView节点：延迟创建连接
                        CreateConnectionForListViewPort(childPort, childViewNode, listView);
                    }
                    else
                    {
                        // 普通节点：立即创建连接
                        CreateConnectionImmediately(childPort, childViewNode);
                    }
                }
                else
                {
                    Debug.LogWarning($"工具节点连接失败: 找不到端口 - 路径: {nodePath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"工具节点连接创建失败: {e.Message}");
            }
        }

        /// <summary>
        /// 立即创建连接 - 用于普通端口
        /// </summary>
        private void CreateConnectionImmediately(ChildPort childPort, ViewNode childViewNode)
        {
            var edge = childPort.ConnectTo(childViewNode.ParentPort);
            AddElement(edge);
            
            // 处理多端口索引
            if (childPort is MultiPort)
            {
                var childValues = childPort.GetChildValues();
                childViewNode.ParentPort.SetIndex(childValues.Count - 1);
            }
            
            Debug.Log($"立即创建工具节点连接: {childPort.node.Data.GetType().Name} -> {childViewNode.Data.GetType().Name}");
        }

        /// <summary>
        /// 为ListView端口创建连接 - 等待ListView初始化
        /// </summary>
        private void CreateConnectionForListViewPort(ChildPort childPort, ViewNode childViewNode, ListView listView)
        {
            // 检查ListView是否已经初始化
            if (listView.userData is bool isInitialized && isInitialized)
            {
                // 已初始化，立即创建连接
                CreateConnectionImmediately(childPort, childViewNode);
            }
            else
            {
                // 未初始化，延迟创建连接
                Debug.Log("ListView未初始化，延迟创建工具节点连接...");
                
                // 使用调度器等待ListView初始化
                var maxRetries = 100; // 最多重试100次 (5秒)
                var retryCount = 0;
                
                void CheckAndCreateConnection()
                {
                    if (listView.userData is bool initialized && initialized)
                    {
                        CreateConnectionImmediately(childPort, childViewNode);
                        Debug.Log($"延迟创建工具节点连接成功 (重试{retryCount}次)");
                    }
                    else if (retryCount < maxRetries)
                    {
                        retryCount++;
                        // 50ms后重试
                        schedule.Execute(CheckAndCreateConnection).ExecuteLater(50);
                    }
                    else
                    {
                        Debug.LogWarning($"工具节点连接创建超时: ListView初始化等待失败");
                    }
                }
                
                CheckAndCreateConnection();
            }
        }

        public virtual void RemoveViewNode(ViewNode node)
        {
            RemoveNode(node.Data);
            ViewNodes.Remove(node);
            NodeDic.Remove(node.Data);
        }

        #endregion
        #region Edge连接管理

        /// <summary>
        /// 并行创建Edge连接 - 基于逻辑层的层次结构（修复主线程问题）
        /// </summary>
        private async Task CreateEdgesAsync(CancellationToken cancellationToken)
        {
            // 在主线程中收集所有需要创建边的元数据
            var edgeMetadataList = new List<JsonNodeTree.NodeMetadata>();

            await ExecuteOnMainThreadAsync(() =>
            {
                foreach (var metadata in _nodeTree.GetSortedNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (metadata.Parent != null)
                    {
                        edgeMetadataList.Add(metadata);
                    }
                }
            });

            // 现在可以安全地并行创建边连接
            var edgeCreationTasks = new List<Task>();

            foreach (var metadata in edgeMetadataList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 直接在主线程上创建边连接，避免线程切换开销
                var task = CreateEdgeForNodeAsync(metadata, cancellationToken);
                edgeCreationTasks.Add(task);
            }

            await Task.WhenAll(edgeCreationTasks);
        }

        /// <summary>
        /// 为指定节点创建边连接
        /// </summary>
        private async Task CreateEdgeForNodeAsync(JsonNodeTree.NodeMetadata childMetadata, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 在主线程执行UI操作
            await ExecuteOnMainThreadAsync(() =>
            {
                if (NodeDic.TryGetValue(childMetadata.Node, out var childViewNode) &&
                    NodeDic.TryGetValue(childMetadata.Parent.Node, out var parentViewNode))
                {
                    CreateEdgeConnection(parentViewNode, childViewNode, childMetadata);
                }
            });
        }

        /// <summary>
        /// 创建具体的边连接
        /// </summary>
        private void CreateEdgeConnection(ViewNode parentViewNode, ViewNode childViewNode, JsonNodeTree.NodeMetadata childMetadata)
        {
            // 查找对应的ChildPort
            var childPort = FindChildPortByName(parentViewNode, childMetadata.PortName, childMetadata.IsMultiPort, childMetadata.ListIndex);
            if (childPort != null && childViewNode.ParentPort != null)
            {
                var edge = childPort.ConnectTo(childViewNode.ParentPort);
                AddElement(edge);

                // 设置多端口索引
                if (childMetadata.IsMultiPort)
                {
                    childViewNode.ParentPort.SetIndex(childMetadata.ListIndex);
                }
            }
        }

        /// <summary>
        /// 根据名称和索引查找ChildPort
        /// </summary>
        private ChildPort FindChildPortByName(ViewNode parentViewNode, string portName, bool isMultiPort, int listIndex)
        {
            foreach (var childPort in parentViewNode.ChildPorts)
            {
                // 通过PropertyElement获取端口路径信息
                var propertyElement = childPort.GetFirstAncestorOfType<PropertyElement>();
                if (propertyElement != null)
                {
                    var memberName = propertyElement.MemberMeta.Path.Split('.').LastOrDefault();
                    if (memberName == portName)
                    {
                        if (isMultiPort && childPort is MultiPort)
                        {
                            return childPort;
                        }
                        else if (!isMultiPort && childPort is not MultiPort)
                        {
                            return childPort;
                        }
                    }
                }
            }
            return null;
        }

        public virtual void CreateEdge(Edge edge)
        {
            //Debug.Log("CreateEdge");
            ViewNode childNode = edge.ParentPort().node;
            ViewNode parentNode = edge.ChildPort()?.node;
            
            // 记录边创建操作
            if (parentNode != null && childNode != null)
            {
                ChildPort childPortOfParent = edge.ChildPort();
                string portName = GetPortName(childPortOfParent);
                var edgeCreateOperation = new EdgeCreateOperation(parentNode.Data, childNode.Data, portName, this);
                Window.History.RecordOperation(edgeCreateOperation);
            }
            
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childPortOfParentNode = edge.ChildPort();
            childPortOfParentNode.SetNodeValue(childNode.Data, false);
            edge.ParentPort().Connect(edge);
            childPortOfParentNode.Connect(edge);
            childPortOfParentNode.OnAddEdge(edge);
        }

        public virtual void RemoveEdge(Edge edge)
        {
            ViewNode parent = edge.ChildPort()?.node;
            ViewNode child = edge.ParentPort()?.node;
            if (parent == null || child == null) { return; }
            
            // 记录边断开操作
            ChildPort childPortOfParent = edge.ChildPort();
            string portName = GetPortName(childPortOfParent);
            var edgeRemoveOperation = new EdgeRemoveOperation(parent.Data, child.Data, portName, this);
            Window.History.RecordOperation(edgeRemoveOperation);
            
            childPortOfParent.SetNodeValue(child.Data);
            Asset.Data.Nodes.Add(child.Data);
            edge.ParentPort().DisconnectAll();
            childPortOfParent.OnRemoveEdge(edge);
        }

        /// <summary>
        /// 获取端口名称
        /// </summary>
        private string GetPortName(ChildPort childPort)
        {
            var propertyElement = childPort.GetFirstAncestorOfType<PropertyElement>();
            if (propertyElement != null)
            {
                return propertyElement.MemberMeta.Path.Split('.').LastOrDefault() ?? "";
            }
            return "";
        }

        #endregion
        #region 端口兼容性检查

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> allPorts = new();
            List<Port> ports = new();

            foreach (var node in ViewNodes)
            {
                if (node == startPort.node) { continue; }

                if (startPort.direction == Direction.Output)
                {
                    if (node.ParentPort != null)
                    {
                        allPorts.Add(node.ParentPort);
                    }
                }
                else
                {
                    allPorts.AddRange(node.ChildPorts);
                }
            }

            ports = allPorts.Where(x => CheckPort(startPort, x)).ToList();
            return ports;
        }

        public virtual bool CheckPort(Port start, Port end)
        {
            if (start.portType != end.portType) { return false; }
            ParentPort parentPort;
            ChildPort childPort;
            if (start is ParentPort)
            {
                parentPort = start as ParentPort;
                childPort = end as ChildPort;
            }
            else
            {
                parentPort = end as ParentPort;
                childPort = start as ChildPort;
            }
            return CheckMulti(parentPort, childPort) && CheckLoop(childPort.node, parentPort.node);
        }

        public bool CheckMulti(ParentPort parentPort, ChildPort childPort)
        {
            return !parentPort.Collection || childPort is MultiPort;
        }

        public bool CheckLoop(ViewNode parent, ViewNode child)
        {
            ViewNode node = parent;
            while (node != null)
            {
                if (node == child) { return false; }
                node = node.GetParent();
            }
            return true;
        }

        #endregion
        #region 数据访问和查询

        public PropertyElement Find(string path)
        {
            JsonNode node = PropertyAccessor.GetLast<JsonNode>(Asset.Data.Nodes, path, false, out int index);
            if (node is null) { return null; }
            if (index >= path.Length - 1) { return null; }
            string local = path[index..];
            return NodeDic[node]?.FindByLocalPath(local);
        }

        public ChildPort GetPort(string path) => Find(path)?.Q<ChildPort>();

        /// <summary>
        /// 验证 - 使用逻辑层验证
        /// </summary>
        public virtual string Validate()
        {
            return NodeTree.ValidateTree();
        }

        /// <summary>
        /// 获取排序后的ViewNode列表 - 基于逻辑层数据
        /// </summary>
        public List<ViewNode> GetSortNodes()
        {
            var sortedMetadata = NodeTree.GetSortedNodes();
            var sortedViewNodes = new List<ViewNode>();

            foreach (var metadata in sortedMetadata)
            {
                if (NodeDic.TryGetValue(metadata.Node, out ViewNode viewNode))
                {
                    sortedViewNodes.Add(viewNode);
                }
            }

            return sortedViewNodes;
        }

        /// <summary>
        /// 获取所有节点路径 - 使用逻辑层实现
        /// </summary>
        public virtual List<(string, string)> GetAllNodePaths()
        {
            return NodeTree.GetAllNodePaths();
        }

        /// <summary>
        /// 获取节点总数 - 使用逻辑层实现
        /// </summary>
        public int GetTotalJsonNodeCount()
        {
            return NodeTree.TotalNodeCount;
        }

        /// <summary>
        /// 获取树视图 - 使用逻辑层实现
        /// </summary>
        public virtual string GetTreeView()
        {
            return NodeTree.GetTreeView();
        }

        #endregion
        #region 图表视图管理

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            // 智能批量操作检测
            bool isBatchOperation = false;
            int totalOperations = 0;
            string batchDescription = "";
            
            // 分析操作复杂性
            var nodesToRemove = graphViewChange.elementsToRemove?.OfType<ViewNode>().ToList() ?? new List<ViewNode>();
            var edgesToRemove = graphViewChange.elementsToRemove?.OfType<Edge>().ToList() ?? new List<Edge>();
            var edgesToCreate = graphViewChange.edgesToCreate?.ToList() ?? new List<Edge>();
            
            totalOperations = nodesToRemove.Count + edgesToRemove.Count + edgesToCreate.Count;
            
            Debug.Log($"GraphView变更检测 - 删除节点:{nodesToRemove.Count}, 删除边:{edgesToRemove.Count}, 创建边:{edgesToCreate.Count}");
            
            // 智能批量检测策略
            if (ShouldStartBatch(nodesToRemove, edgesToRemove, edgesToCreate, out batchDescription))
            {
                isBatchOperation = true;
                Debug.Log($"启动批量操作: {batchDescription}");
                Window.History.BeginBatch(batchDescription);
            }
            
            // 处理删除操作
            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
            {
                ProcessRemoveOperations(nodesToRemove, edgesToRemove, graphViewChange);
            }
            
            // 处理创建操作
            if (edgesToCreate.Count > 0)
            {
                ProcessCreateOperations(edgesToCreate, ref isBatchOperation);
            }
            
            // 结束批量操作或添加单步记录
            if (isBatchOperation)
            {
                Debug.Log($"结束批量操作: {batchDescription}");
                Window.History.EndBatch();
            }
            else if (totalOperations > 0)
            {
                Debug.Log("添加单步历史记录");
                Window.History.AddStep();
            }
            
            return graphViewChange;
        }

        /// <summary>
        /// 智能判断是否应该开启批量模式
        /// </summary>
        private bool ShouldStartBatch(List<ViewNode> nodesToRemove, List<Edge> edgesToRemove, 
            List<Edge> edgesToCreate, out string description)
        {
            description = "";
            
            // 关键修改：任何节点删除操作都应该是批量操作
            // 这确保了删除节点时，断开边和移除节点都在同一个批量操作中，Undo时能一起恢复
            if (nodesToRemove.Count > 0)
            {
                if (nodesToRemove.Count == 1)
                {
                    // 单个节点删除：统计其关联的边数量
                    var nodeEdges = nodesToRemove[0].GetAllEdges();
                    int totalEdges = nodeEdges.Count + edgesToRemove.Count;
                    description = $"删除节点 '{nodesToRemove[0].Data.GetType().Name}' 及其 {totalEdges} 个连接";
                }
                else
                {
                    // 多选删除节点
                    description = $"批量删除 {nodesToRemove.Count} 个节点";
                }
                return true;
            }
            
            // 纯边删除操作：多个边删除时使用批量模式
            if (edgesToRemove.Count > 1)
            {
                description = $"批量断开 {edgesToRemove.Count} 个连接";
                return true;
            }
            
            // 批量创建边连接
            if (edgesToCreate.Count > 1)
            {
                description = $"批量创建 {edgesToCreate.Count} 个边连接";
                return true;
            }
            
            // 复杂的混合操作
            if (edgesToRemove.Count > 0 && edgesToCreate.Count > 0)
            {
                description = "复杂的节点重组操作";
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 优化的删除操作处理
        /// </summary>
        private void ProcessRemoveOperations(List<ViewNode> nodesToRemove, List<Edge> edgesToRemove, 
            GraphViewChange graphViewChange)
        {
            Debug.Log($"开始处理删除操作 - 节点:{nodesToRemove.Count}个, 直接删除边:{edgesToRemove.Count}个");
            
            // 1. 首先收集所有需要删除的边（包括节点关联的边）
            var allEdgesToRemove = new HashSet<Edge>(edgesToRemove);
            int nodeAssociatedEdges = 0;
            
            foreach (ViewNode viewNode in nodesToRemove)
            {
                var nodeEdges = viewNode.GetAllEdges();
                foreach (var edge in nodeEdges)
                {
                    if (allEdgesToRemove.Add(edge)) // Add方法返回true表示是新添加的
                    {
                        nodeAssociatedEdges++;
                    }
                }
            }
            
            Debug.Log($"收集到节点关联边:{nodeAssociatedEdges}个, 总删除边数:{allEdgesToRemove.Count}个");
            
            // 2. 按照依赖关系顺序删除边
            var edgeList = allEdgesToRemove.ToList();
            ProcessEdgeRemovalInOrder(edgeList);
            
            // 3. 删除节点（按深度倒序，避免引用问题）
            var sortedNodes = nodesToRemove.OrderByDescending(n => n.GetDepth()).ToList();
            Debug.Log($"按深度排序删除节点，顺序: {string.Join(" -> ", sortedNodes.Select(n => $"{n.Data.GetType().Name}(深度{n.GetDepth()})"))}");
            
            foreach (ViewNode viewNode in sortedNodes)
            {
                Debug.Log($"删除节点: {viewNode.Data.GetType().Name}");
                RemoveViewNode(viewNode);
            }
            
            // 4. 更新graphViewChange以包含所有边
            graphViewChange.elementsToRemove.Clear();
            graphViewChange.elementsToRemove.AddRange(allEdgesToRemove);
            graphViewChange.elementsToRemove.AddRange(nodesToRemove);
            
            Debug.Log($"删除操作完成 - 实际删除边:{allEdgesToRemove.Count}个, 节点:{nodesToRemove.Count}个");
        }

        /// <summary>
        /// 按正确顺序处理边删除，避免引用错误
        /// </summary>
        private void ProcessEdgeRemovalInOrder(List<Edge> edges)
        {
            // 按连接深度排序，先删除深层的边，再删除浅层的边
            var sortedEdges = edges
                .Where(e => e?.ParentPort()?.node != null)
                .OrderByDescending(e => e.ParentPort().node.GetDepth())
                .ToList();
            
            Debug.Log($"按深度排序删除 {sortedEdges.Count} 条边");
            
            foreach (Edge edge in sortedEdges)
            {
                try
                {
                    Debug.Log(edge.GetHashCode());
                    var parentNode = edge.ChildPort()?.node?.Data?.GetType().Name ?? "Unknown";
                    var childNode = edge.ParentPort()?.node?.Data?.GetType().Name ?? "Unknown";
                    Debug.Log($"删除边: {parentNode} -> {childNode}");
                    RemoveEdge(edge);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"删除边时出错: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 优化的创建操作处理
        /// </summary>
        private void ProcessCreateOperations(List<Edge> edgesToCreate, ref bool isBatchOperation)
        {
            // 如果是单个边创建且不在批量模式中，检查是否需要启动批量模式
            if (edgesToCreate.Count > 1 && !isBatchOperation)
            {
                isBatchOperation = true;
                Window.History.BeginBatch($"批量创建 {edgesToCreate.Count} 个边连接");
            }
            
            // 按源节点深度排序，确保创建顺序正确
            var sortedEdges = edgesToCreate
                .Where(e => e?.ChildPort()?.node != null)
                .OrderBy(e => e.ChildPort().node.GetDepth())
                .ToList();
            
            foreach (Edge edge in sortedEdges)
            {
                try
                {
                    CreateEdge(edge);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"创建边时出错: {e.Message}");
                }
            }
        }

        #endregion
    }
}
