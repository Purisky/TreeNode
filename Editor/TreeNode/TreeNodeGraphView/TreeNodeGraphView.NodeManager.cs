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

        public ViewNode AddViewNode(JsonNode node, ChildPort childPort = null)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; }

            if (node.PrefabData != null)
            {
                viewNode = new PrefabViewNode(node, this, childPort);
            }
            else
            {
                viewNode = new(node, this, childPort);
            }
            viewNode.SetPosition(new Rect(node.Position, new()));
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);

            viewNode.AddChildNodesUntilListInited();
            return viewNode;
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
            
            // 智能批量检测策略
            if (ShouldStartBatch(nodesToRemove, edgesToRemove, edgesToCreate, out batchDescription))
            {
                isBatchOperation = true;
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
                Window.History.EndBatch();
            }
            else if (totalOperations > 0)
            {
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
            
            // 多选删除节点
            if (nodesToRemove.Count > 1)
            {
                description = $"批量删除 {nodesToRemove.Count} 个节点";
                return true;
            }
            
            // 节点删除伴随多个边删除
            if (nodesToRemove.Count == 1 && edgesToRemove.Count > 2)
            {
                description = $"删除节点及其 {edgesToRemove.Count} 个连接";
                return true;
            }
            
            // 批量创建边连接
            if (edgesToCreate.Count > 1)
            {
                description = $"批量创建 {edgesToCreate.Count} 个边连接";
                return true;
            }
            
            // 复杂的混合操作
            if (nodesToRemove.Count > 0 && edgesToCreate.Count > 0)
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
            // 1. 首先收集所有需要删除的边（包括节点关联的边）
            var allEdgesToRemove = new HashSet<Edge>(edgesToRemove);
            
            foreach (ViewNode viewNode in nodesToRemove)
            {
                var nodeEdges = viewNode.GetAllEdges();
                foreach (var edge in nodeEdges)
                {
                    allEdgesToRemove.Add(edge);
                }
            }
            
            // 2. 按照依赖关系顺序删除边
            var edgeList = allEdgesToRemove.ToList();
            ProcessEdgeRemovalInOrder(edgeList);
            
            // 3. 删除节点（按深度倒序，避免引用问题）
            var sortedNodes = nodesToRemove.OrderByDescending(n => n.GetDepth()).ToList();
            foreach (ViewNode viewNode in sortedNodes)
            {
                RemoveViewNode(viewNode);
            }
            
            // 4. 更新graphViewChange以包含所有边
            graphViewChange.elementsToRemove.Clear();
            graphViewChange.elementsToRemove.AddRange(allEdgesToRemove);
            graphViewChange.elementsToRemove.AddRange(nodesToRemove);
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
            
            foreach (Edge edge in sortedEdges)
            {
                try
                {
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
