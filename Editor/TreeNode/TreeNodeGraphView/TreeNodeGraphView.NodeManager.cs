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
            Asset.Data.Nodes.Add(node);
            NodeTree.OnNodeAdded(node);
            Window.History.AddStep();
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
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
                if (oldValue is IList list)
                {
                    list.Add(node);
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
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(childNode.Data, false);
            edge.ParentPort().Connect(edge);
            childport_of_parent.Connect(edge);
            childport_of_parent.OnAddEdge(edge);
        }

        public virtual void RemoveEdge(Edge edge)
        {
            ViewNode parent = edge.ChildPort()?.node;
            ViewNode child = edge.ParentPort()?.node;
            if (parent == null || child == null) { return; }
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(child.Data);
            Asset.Data.Nodes.Add(child.Data);
            edge.ParentPort().DisconnectAll();
            childport_of_parent.OnRemoveEdge(edge);
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

        public void Redraw()
        {
            // 取消当前的渲染任务
            lock (_renderLock)
            {
                _renderCancellationSource?.Cancel();
            }

            ViewNodes.Clear();
            NodeDic.Clear();
            ViewContainer.Query<Layer>().ForEach(p => p.Clear());

            // 重新初始化逻辑层
            InitializeNodeTreeSync();

            // 异步重新渲染
            _ = DrawNodesAsync();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.elementsToRemove != null)
            {
                IEnumerable<ViewNode> nodes = graphViewChange.elementsToRemove.OfType<ViewNode>().Reverse();
                List<Edge> temp = new();
                if (nodes.Any())
                {
                    foreach (ViewNode viewNode in nodes)
                    {
                        temp.AddRange(viewNode.GetAllEdges());
                    }
                }
                graphViewChange.elementsToRemove.AddRange(temp);
                IEnumerable<Edge> edges = graphViewChange.elementsToRemove.OfType<Edge>().Distinct();
                if (edges.Any())
                {
                    foreach (Edge edge in edges)
                    {
                        RemoveEdge(edge);
                    }
                }
                foreach (ViewNode viewNode in nodes)
                {
                    RemoveViewNode(viewNode);
                }
            }
            if (graphViewChange.edgesToCreate != null)
            {
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    CreateEdge(edge);
                }
            }
            Window.History.AddStep();
            return graphViewChange;
        }

        #endregion
    }
}
