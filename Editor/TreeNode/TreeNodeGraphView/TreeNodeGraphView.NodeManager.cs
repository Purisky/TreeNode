using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TreeNode.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

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
            var createOperation =  NodeOperation.Create(node,$"[{Asset.Data.Nodes.Count-1}]" , this);
            Window.History.Record(createOperation);

            NodeTree.OnNodeAdded(node);
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Asset.Data.Nodes.Add(node);
                var createOperation = NodeOperation.Create(node, $"[{Asset.Data.Nodes.Count-1}]", this);
                Window.History.Record(createOperation);

                NodeTree.OnNodeAdded(node);
                return true;
            }
            try
            {
                string path_ = path;
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
                        path_ = $"{path_}[0]";
                    }
                }
                if (oldValue is IList nodeList)
                {
                    nodeList.Add(node);
                    path_ = $"{path_}[{nodeList.Count-1}]";
                }
                else
                {
                    PropertyAccessor.SetValue(parent, last, node);
                }
                var moveOperation = NodeOperation.Create(node, path, this);
                Window.History.Record(moveOperation);
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
            var deleteOperation = NodeOperation.Delete(node,$"[{index}]", this);
            Window.History.Record(deleteOperation);
            
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
            return viewNode;
        }

        /// <summary>
        /// 专用于工具添加节点的方法
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
        /// 为工具添加的节点创建连接 - 优化版本
        /// </summary>
        private void CreateToolNodeConnection(ViewNode childViewNode, string nodePath)
        {
            try
            {
                // 通过现有的GetPort方法查找端口
                var childPort = GetPort(nodePath);
                if (childPort != null && childViewNode.ParentPort != null)
                {
                    // 立即创建连接
                    CreateConnectionImmediately(childPort, childViewNode);
                }
                else
                {
                    Debug.LogWarning($"工具节点连接失败: 找不到端口 - 路径: {nodePath}");
                    
                    // 尝试延迟连接创建 - 可能端口还未创建完成
                    schedule.Execute(() => 
                    {
                        var retryPort = GetPort(nodePath);
                        if (retryPort != null && childViewNode.ParentPort != null)
                        {
                            CreateConnectionImmediately(retryPort, childViewNode);
                            Debug.Log($"延迟重试成功创建工具节点连接: {nodePath}");
                        }
                    }).ExecuteLater(100); // 100ms后重试
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"工具节点连接创建失败: {e.Message}");
            }
        }

        /// <summary>
        /// 立即创建连接
        /// </summary>
        private void CreateConnectionImmediately(ChildPort childPort, ViewNode childViewNode)
        {
            var edge = childPort.ConnectTo(childViewNode.ParentPort);
            AddElement(edge);
            
            // 处理多端口索引 - 优化索引计算
            if (childPort is MultiPort multiPort)
            {
                var childValues = multiPort.GetChildValues();
                int newIndex = Math.Max(0, childValues.Count - 1);
                childViewNode.ParentPort.SetIndex(newIndex);
                Debug.Log($"设置MultiPort索引: {newIndex}");
            }
            
            Debug.Log($"立即创建工具节点连接: {childPort.node.Data.GetType().Name} -> {childViewNode.Data.GetType().Name}");
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
        /// 批量创建Edge连接
        /// </summary>
        private async Task CreateEdgesAsync(CancellationToken cancellationToken)
        {
            // 在主线程中收集所有需要创建边的元数据
            var edgeMetadataList = new List<Runtime.JsonNodeTree.NodeMetadata>();

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

            Debug.Log($"收集到 {edgeMetadataList.Count} 个边连接需要创建");

            // 批量创建边连接 - 使用并行Task但在主线程执行UI操作
            var edgeCreationTasks = new List<Task>();
            var batchSize = Math.Max(5, edgeMetadataList.Count / 10); // 动态批次大小
            
            for (int i = 0; i < edgeMetadataList.Count; i += batchSize)
            {
                var batch = edgeMetadataList.Skip(i).Take(batchSize).ToList();
                var task = CreateEdgeBatchAsync(batch, cancellationToken);
                edgeCreationTasks.Add(task);
            }

            await Task.WhenAll(edgeCreationTasks);
            
            // 渲染后处理 - 检查并修复可能缺失的连接
            await PostRenderProcessAsync(cancellationToken);
            
            Debug.Log("所有边连接创建完成");
        }

        /// <summary>
        /// 批量创建边连接 - 分批处理避免UI线程阻塞
        /// </summary>
        private async Task CreateEdgeBatchAsync(List<JsonNodeTree.NodeMetadata> batch, CancellationToken cancellationToken)
        {
            await ExecuteOnMainThreadAsync(() =>
            {
                foreach (var metadata in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (NodeDic.TryGetValue(metadata.Node, out var childViewNode) &&
                        NodeDic.TryGetValue(metadata.Parent.Node, out var parentViewNode))
                    {
                        CreateEdgeConnection(parentViewNode, childViewNode, metadata);
                    }
                    else
                    {
                        Debug.LogWarning($"无法找到节点ViewNode进行边连接: 子节点={metadata.Node?.GetType().Name}, 父节点={metadata.Parent?.Node?.GetType().Name}");
                    }
                }
            });
        }

        /// <summary>
        /// 渲染后处理
        /// </summary>
        private async Task PostRenderProcessAsync(CancellationToken cancellationToken)
        {
            await ExecuteOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                int fixedConnections = 0;
                int missingConnections = 0;
                
                // 检查所有节点的连接状态
                foreach (var kvp in NodeDic)
                {
                    var node = kvp.Key;
                    var viewNode = kvp.Value;
                    
                    // 如果节点有ParentPort但没有连接，尝试修复
                    if (viewNode.ParentPort != null && !viewNode.ParentPort.connected)
                    {
                        if (TryRestoreNodeConnection(node, viewNode))
                        {
                            fixedConnections++;
                        }
                        else
                        {
                            missingConnections++;
                        }
                    }
                }
                
                if (fixedConnections > 0)
                {
                    Debug.Log($"渲染后处理完成: 修复了 {fixedConnections} 个缺失连接");
                }
                
                if (missingConnections > 0)
                {
                    Debug.LogWarning($"渲染后处理发现 {missingConnections} 个无法修复的缺失连接");
                }
            });
        }

        /// <summary>
        /// 尝试恢复节点连接 (新增)
        /// </summary>
        private bool TryRestoreNodeConnection(JsonNode node, ViewNode viewNode)
        {
            try
            {
                // 通过NodeTree查找该节点应该连接的父节点
                var nodeMetadata = NodeTree.GetNodeMetadata(node);
                if (nodeMetadata?.Parent != null)
                {
                    if (NodeDic.TryGetValue(nodeMetadata.Parent.Node, out var parentViewNode))
                    {
                        CreateEdgeConnection(parentViewNode, viewNode, nodeMetadata);
                        Debug.Log($"成功修复节点连接: {parentViewNode.Data.GetType().Name} -> {viewNode.Data.GetType().Name}");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"尝试修复节点连接时发生异常: {e.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// 为指定节点创建边连接
        /// </summary>
        private async Task CreateEdgeForNodeAsync(Runtime.JsonNodeTree.NodeMetadata childMetadata, CancellationToken cancellationToken)
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
        /// 创建具体的边连接 (优化错误处理和日志)
        /// </summary>
        private void CreateEdgeConnection(ViewNode parentViewNode, ViewNode childViewNode, Runtime.JsonNodeTree.NodeMetadata childMetadata)
        {
            try
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
                else
                {
                    Debug.LogWarning($"无法创建边连接: 端口查找失败 - 父节点={parentViewNode.Data.GetType().Name}, 端口名={childMetadata.PortName}, 子节点={childViewNode.Data.GetType().Name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"创建边连接时发生异常: {e.Message}\n父节点={parentViewNode.Data.GetType().Name}, 子节点={childViewNode.Data.GetType().Name}");
            }
        }

        /// <summary>
        /// 根据名称和索引查找ChildPort (优化查找逻辑)
        /// </summary>
        private ChildPort FindChildPortByName(ViewNode parentViewNode, string portName, bool isMultiPort, int listIndex)
        {
            try
            {
                foreach (var childPort in parentViewNode.ChildPorts)
                {
                    // 通过PropertyElement获取端口路径信息
                    var propertyElement = childPort.GetFirstAncestorOfType<PropertyElement>();
                    if (propertyElement != null)
                    {
                        var memberName = propertyElement.MemberMeta.Path.LastPart.Name;
                        if (memberName == portName)
                        {
                            // 精确匹配端口类型
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
            }
            catch (Exception e)
            {
                Debug.LogError($"查找ChildPort时发生异常: {e.Message}");
            }
            
            return null;
        }

        public virtual void CreateEdge(Edge edge)
        {
            //Debug.Log("CreateEdge");
            ViewNode childNode = edge.ParentPort().node;
            ViewNode parentNode = edge.ChildPort().node;
            PAPath from = PAPath.Index(Asset.Data.Nodes.IndexOf(childNode.Data));
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childPortOfParentNode = edge.ChildPort();
            PAPath to = parentNode.GetNodePath().Combine(childPortOfParentNode.SetNodeValue(childNode.Data, false));
            Window.History.Record(NodeOperation.Move(childNode.Data, from, to, this));
            edge.ParentPort().Connect(edge);
            childPortOfParentNode.Connect(edge);
            childPortOfParentNode.OnAddEdge(edge);
        }

        public virtual void RemoveEdge(Edge edge)
        {
            try
            {
                ViewNode parent = edge.ChildPort()?.node;
                ViewNode child = edge.ParentPort()?.node;
                if (parent == null || child == null) { return; }
                PAPath from = child.GetNodePath();
                //Debug.Log($"RemoveEdge: {from}");
                ChildPort childPortOfParent = edge.ChildPort();
                childPortOfParent.SetNodeValue(child.Data);
                Asset.Data.Nodes.Add(child.Data);
                Window.History.Record(NodeOperation.Move(child.Data, from, PAPath.Index(Asset.Data.Nodes.Count-1), this));
                edge.ParentPort().DisconnectAll();
                childPortOfParent.OnRemoveEdge(edge);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                throw e;
            }

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
            Debug.Log($"启动批量操作");
            Window.History.BeginBatch();
            // 处理删除操作
            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
            {
                ProcessRemoveOperations(graphViewChange.elementsToRemove.OfType<ViewNode>().ToList(),
                    graphViewChange.elementsToRemove.OfType<Edge>().ToList(),
                    graphViewChange);
            }
            // 处理创建操作
            if (graphViewChange.edgesToCreate != null&& graphViewChange.edgesToCreate.Count > 0)
            {
                ProcessCreateOperations(graphViewChange.edgesToCreate.ToList());
            }
            if (graphViewChange.movedElements != null && graphViewChange.movedElements.Count > 0&& !graphViewChange.moveDelta.Equals(Vector2.zero))
            {
                ProcessNodeMoveOperations(graphViewChange.movedElements.OfType<ViewNode>().ToList(), graphViewChange.moveDelta);
            }
            Debug.Log($"结束批量操作");
            Window.History.EndBatch();
            return graphViewChange;
        }
        private void ProcessRemoveOperations(List<ViewNode> nodesToRemove, List<Edge> edgesToRemove, 
            GraphViewChange graphViewChange)
        {
            Debug.Log($"开始处理删除操作 - 节点:{nodesToRemove.Count}个, 直接删除边:{edgesToRemove.Count}个");
            
            // 1. 首先收集所有需要删除的边（包括节点关联的边)
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
            
            // 3. 删除节点（按深度倒序，避免引用问题)
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
        private void ProcessCreateOperations(List<Edge> edgesToCreate)
        {
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
        private void ProcessNodeMoveOperations(List<ViewNode> viewNodes, Vector2 delta)
        {
            for (int i = 0; i < viewNodes.Count; i++)
            {
                ViewNode viewNode = viewNodes[i];
                try
                {
                    Vec2 from = viewNode.Data.Position;
                    // 更新逻辑层数据
                    viewNode.Data.Position += delta;
                    Vec2 to = viewNode.Data.Position;
                    // 记录节点移动操作
                    var moveOperation = new FieldModifyOperation<Vec2>(viewNode.Data, PAPath.Position, from, to, this);
                    Window.History.Record(moveOperation);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"移动节点时出错: {e.Message}");
                }
            }
        }
        #endregion

    }
}
