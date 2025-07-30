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
        private Runtime.JsonNodeTree _nodeTree;
        public Runtime.JsonNodeTree NodeTree => _nodeTree;

        // ListView初始化状态管理器
        private readonly ListViewInitializationTracker _listViewTracker = new();

        #region ListView初始化状态管理

        /// <summary>
        /// ListView初始化状态管理器 - 智能等待ListView完全初始化
        /// </summary>
        private class ListViewInitializationTracker
        {
            private readonly HashSet<ListView> _pendingListViews = new();
            private readonly object _lock = new object();
            
            public void RegisterListView(ListView listView)
            {
                lock (_lock)
                {
                    _pendingListViews.Add(listView);
                }
                Debug.Log($"注册ListView到初始化跟踪器, 当前待初始化数量: {_pendingListViews.Count}");
            }
            
            public void MarkListViewReady(ListView listView)
            {
                lock (_lock)
                {
                    if (_pendingListViews.Remove(listView))
                    {
                        Debug.Log($"ListView初始化完成, 剩余待初始化数量: {_pendingListViews.Count}");
                    }
                }
            }
            
            public bool AllListViewsReady()
            {
                lock (_lock)
                {
                    return _pendingListViews.Count == 0;
                }
            }
            
            public int PendingCount()
            {
                lock (_lock)
                {
                    return _pendingListViews.Count;
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _pendingListViews.Clear();
                }
            }
        }

        /// <summary>
        /// 智能检测是否有ListView节点需要等待初始化
        /// </summary>
        private async Task<bool> CheckForListViewNodesAsync(List<Runtime.JsonNodeTree.NodeMetadata> edgeMetadataList, CancellationToken cancellationToken)
        {
            bool hasListViewNodes = false;
            
            await ExecuteOnMainThreadAsync(() =>
            {
                // 检查是否有节点的端口处在ListView内部
                foreach (var metadata in edgeMetadataList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (NodeDic.TryGetValue(metadata.Parent.Node, out var parentViewNode))
                    {
                        // 检查该节点是否包含ListView
                        var listViews = parentViewNode.Query<ListView>().ToList();
                        if (listViews.Any())
                        {
                            hasListViewNodes = true;
                            
                            // 注册所有未初始化的ListView
                            foreach (var listView in listViews)
                            {
                                if (!(listView.userData is bool initialized && initialized))
                                {
                                    _listViewTracker.RegisterListView(listView);
                                }
                            }
                        }
                    }
                }
            });
            
            return hasListViewNodes;
        }

        /// <summary>
        /// 等待ListView初始化完成 - 智能超时机制
        /// </summary>
        private async Task WaitForListViewInitializationAsync(CancellationToken cancellationToken)
        {
            const int maxWaitTime = 5000; // 5秒超时
            const int checkInterval = 50; // 每50ms检查一次
            int elapsedTime = 0;
            
            while (!_listViewTracker.AllListViewsReady() && elapsedTime < maxWaitTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await Task.Delay(checkInterval, cancellationToken);
                elapsedTime += checkInterval;
                
                if (elapsedTime % 500 == 0) // 每500ms输出一次进度
                {
                    Debug.Log($"等待ListView初始化... 剩余: {_listViewTracker.PendingCount()}个, 已等待: {elapsedTime}ms");
                }
            }
            
            if (_listViewTracker.AllListViewsReady())
            {
                Debug.Log($"所有ListView初始化完成，总耗时: {elapsedTime}ms");
            }
            else
            {
                Debug.LogWarning($"ListView初始化等待超时 ({maxWaitTime}ms)，强制继续连接创建");
            }
        }

        #endregion
        #region 节点管理

        public virtual void AddNode(JsonNode node)
        {
            Asset.Data.Nodes.Add(node);
            var createOperation = new NodeCreateOperation(node,$"[{Asset.Data.Nodes.Count}]" , this);
            Window.History.RecordOperation(createOperation);

            NodeTree.OnNodeAdded(node);
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Asset.Data.Nodes.Add(node);
                var createOperation = new NodeCreateOperation(node, $"[{Asset.Data.Nodes.Count}]", this);
                Window.History.RecordOperation(createOperation);

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
            var deleteOperation = new NodeDeleteOperation(node,$"[{index}]", this);
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
        /// 专用于工具添加节点的方法 - 支持立即连接创建 (已优化ListView支持)
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
        /// 为工具添加的节点创建连接 - 智能ListView端口查找 (优化版本)
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
        /// 立即创建连接 - 用于普通端口 (优化索引设置)
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

        /// <summary>
        /// 为ListView端口创建连接 - 等待ListView初始化 (优化重试机制)
        /// </summary>
        private void CreateConnectionForListViewPort(ChildPort childPort, ViewNode childViewNode, ListView listView)
        {
            // 检查ListView是否已经初始化
            if (listView.userData is bool isInitialized && isInitialized)
            {
                // 已初始化，立即创建连接
                CreateConnectionImmediately(childPort, childViewNode);
                return;
            }

            // 未初始化，使用优化的延迟创建机制
            Debug.Log("ListView未初始化，启动智能延迟连接创建...");
            
            var connectionAttempt = new ListViewConnectionAttempt
            {
                ChildPort = childPort,
                ChildViewNode = childViewNode,
                ListView = listView,
                MaxRetries = 100, // 最多重试100次 (5秒)
                RetryInterval = 50, // 每50ms重试一次
                StartTime = DateTime.Now
            };
            
            ScheduleListViewConnection(connectionAttempt);
        }

        /// <summary>
        /// ListView连接创建尝试信息
        /// </summary>
        private class ListViewConnectionAttempt
        {
            public ChildPort ChildPort;
            public ViewNode ChildViewNode;
            public ListView ListView;
            public int MaxRetries;
            public int RetryInterval;
            public DateTime StartTime;
            public int CurrentRetry = 0;
        }

        /// <summary>
        /// 调度ListView连接创建 - 智能重试机制
        /// </summary>
        private void ScheduleListViewConnection(ListViewConnectionAttempt attempt)
        {
            void CheckAndCreateConnection()
            {
                try
                {
                    // 检查ListView是否已初始化
                    if (attempt.ListView.userData is bool initialized && initialized)
                    {
                        CreateConnectionImmediately(attempt.ChildPort, attempt.ChildViewNode);
                        var elapsed = (DateTime.Now - attempt.StartTime).TotalMilliseconds;
                        Debug.Log($"ListView延迟连接创建成功 (重试{attempt.CurrentRetry}次, 耗时{elapsed:F0}ms)");
                        return;
                    }
                    
                    // 检查是否超过最大的重试次数
                    if (attempt.CurrentRetry >= attempt.MaxRetries)
                    {
                        var elapsed = (DateTime.Now - attempt.StartTime).TotalMilliseconds;
                        Debug.LogWarning($"ListView连接创建超时: 等待{elapsed:F0}ms后放弃 (重试{attempt.CurrentRetry}次)");
                        return;
                    }
                    
                    // 继续重试
                    attempt.CurrentRetry++;
                    schedule.Execute(CheckAndCreateConnection).ExecuteLater(attempt.RetryInterval);
                }
                catch (Exception e)
                {
                    Debug.LogError($"ListView连接创建过程中发生异常: {e.Message}");
                }
            }
            
            CheckAndCreateConnection();
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
        /// 批量创建Edge连接 - 智能ListView初始化等待版本 (完善版本)
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

            // 🔍 智能检测：是否有节点需要ListView初始化
            bool hasListViewNodes = await CheckForListViewNodesAsync(edgeMetadataList, cancellationToken);
            
            if (hasListViewNodes)
            {
                Debug.Log("检测到ListView节点，等待ListView完全初始化...");
                await WaitForListViewInitializationAsync(cancellationToken);
            }
            else
            {
                Debug.Log("未检测到ListView节点，跳过ListView初始化等待");
            }

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
        private async Task CreateEdgeBatchAsync(List<Runtime.JsonNodeTree.NodeMetadata> batch, CancellationToken cancellationToken)
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
        /// 渲染后处理 - 修复可能缺失的连接 (新增)
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
        /// 为指定节点创建边连接 (优化异常处理)
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
                    
                    // Debug信息只在开发模式下输出
                    #if UNITY_EDITOR && TREE_NODE_DEBUG
                    Debug.Log($"创建边连接: {parentViewNode.Data.GetType().Name}.{childMetadata.PortName} -> {childViewNode.Data.GetType().Name}");
                    #endif
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
                        var memberName = propertyElement.MemberMeta.Path.Split('.').LastOrDefault();
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
                
                // 如果精确匹配失败，尝试模糊匹配
                foreach (var childPort in parentViewNode.ChildPorts)
                {
                    var propertyElement = childPort.GetFirstAncestorOfType<PropertyElement>();
                    if (propertyElement != null)
                    {
                        var path = propertyElement.MemberMeta.Path;
                        if (path.Contains(portName))
                        {
                            Debug.LogWarning($"使用模糊匹配找到端口: {path} 匹配 {portName}");
                            return childPort;
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
            ViewNode parentNode = edge.ChildPort()?.node;
            
            // 记录边创建操作
            if (parentNode != null && childNode != null)
            {
                ChildPort childPortOfParent = edge.ChildPort();
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
            // 🔥 重要修复：移除这里的AddStep调用
            // 如果操作都是通过RecordOperation记录的原子操作，让智能合并机制自动处理
            // 只有在使用传统非原子操作时才需要手动AddStep
            else if (totalOperations > 0)
            {
                Debug.Log($"单步操作完成，操作数量: {totalOperations}");
                // 不再调用 Window.History.AddStep()，因为原子操作已经通过RecordOperation处理了
                // Window.History.AddStep();
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
        #region ListView初始化公共接口

        /// <summary>
        /// 注册ListView到初始化跟踪器 - 供ListDrawer调用
        /// </summary>
        public void RegisterListViewForTracking(ListView listView)
        {
            _listViewTracker.RegisterListView(listView);
        }

        /// <summary>
        /// 标记ListView为就绪状态 - 供ListDrawer调用
        /// </summary>
        public void MarkListViewAsReady(ListView listView)
        {
            _listViewTracker.MarkListViewReady(listView);
        }
        
        #endregion
    }
}
