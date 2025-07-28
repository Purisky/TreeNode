using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView : GraphView
    {
        public JsonAsset Asset => Window.JsonAsset;
        public TreeNodeGraphWindow Window;

        public List<ViewNode> ViewNodes;
        public Dictionary<JsonNode, ViewNode> NodeDic;

        // 逻辑层树结构处理器 - 改为立即初始化
        private JsonNodeTree _nodeTree;
        public JsonNodeTree NodeTree => _nodeTree;

        // 异步渲染状态管理
        private bool _isRenderingAsync = false;
        private readonly object _renderLock = new object();
        private System.Threading.CancellationTokenSource _renderCancellationSource;

        // 渲染任务队列和并发控制
        private readonly ConcurrentQueue<Func<Task>> _renderTasks = new();
        private readonly SemaphoreSlim _renderSemaphore = new(Environment.ProcessorCount);

        public TreeNodeWindowSearchProvider SearchProvider;
        public VisualElement ViewContainer;
        protected ContentZoomer m_Zoomer;
        
        public TreeNodeGraphView(TreeNodeGraphWindow window)
        {
            Window = window;
            style.flexGrow = 1;
            
            // 立即初始化逻辑层树结构
            InitializeNodeTreeSync();
            
            StyleSheet styleSheet = ResourcesUtil.LoadStyleSheet("TreeNodeGraphView");
            styleSheets.Add(styleSheet);
            ViewNodes = new();
            NodeDic = new();
            SearchProvider = ScriptableObject.CreateInstance<TreeNodeWindowSearchProvider>();
            SearchProvider.Graph = this;
            this.nodeCreationRequest = ShowSearchWindow;
            graphViewChanged = OnGraphViewChanged;

            ViewContainer = this.Q("contentViewContainer");
            GridBackground background = new()
            {
                name = "Grid",
            };
            Insert(0, background);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            // 使用异步方式渲染节点
            _ = DrawNodesAsync();

            SetupZoom(0.2f, 2f);
            canPasteSerializedData = CanPaste;
            serializeGraphElements = Copy;
            unserializeAndPaste = Paste;
        }

        /// <summary>
        /// 同步初始化JsonNodeTree - 确保逻辑层数据准备完成
        /// </summary>
        private void InitializeNodeTreeSync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _nodeTree = new JsonNodeTree(Asset.Data);
            stopwatch.Stop();
            
            //Debug.Log($"JsonNodeTree initialized in {stopwatch.ElapsedMilliseconds}ms with {_nodeTree.TotalNodeCount} nodes");
        }

        /// <summary>
        /// 异步渲染所有节点和边 - 高性能版本（Unity编辑器优化）
        /// </summary>
        private async Task DrawNodesAsync()
        {
            lock (_renderLock)
            {
                if (_isRenderingAsync)
                {
                    _renderCancellationSource?.Cancel();
                }
                _isRenderingAsync = true;
                _renderCancellationSource = new System.Threading.CancellationTokenSource();
            }

            var cancellationToken = _renderCancellationSource.Token;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 阶段1: 在主线程准备ViewNode数据（避免Unity API线程问题）
                var viewNodeCreationTasks = await PrepareViewNodesAsync(cancellationToken);
                
                // 阶段2: 批量添加ViewNode到UI（在主线程执行）
                await AddViewNodesToUIAsync(viewNodeCreationTasks, cancellationToken);
                
                // 阶段3: 创建Edge连接（在主线程执行）
                await CreateEdgesAsync(cancellationToken);
                
                stopwatch.Stop();
                //Debug.Log($"Async rendering completed in {stopwatch.ElapsedMilliseconds}ms for {ViewNodes.Count} nodes");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Async rendering was cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"Async rendering failed: {e.Message}\n{e.StackTrace}");
                
                // 在异常情况下，回退到同步渲染
                try
                {
                    Debug.Log("Falling back to synchronous rendering...");
                    DrawNodesSynchronously();
                }
                catch (Exception fallbackException)
                {
                    Debug.LogError($"Fallback synchronous rendering also failed: {fallbackException.Message}");
                }
            }
            finally
            {
                lock (_renderLock)
                {
                    _isRenderingAsync = false;
                    _renderCancellationSource?.Dispose();
                    _renderCancellationSource = null;
                }
            }
        }

        /// <summary>
        /// 同步渲染备用方案
        /// </summary>
        private void DrawNodesSynchronously()
        {
            Debug.Log("Using synchronous rendering as fallback");
            
            var sortedMetadata = _nodeTree.GetSortedNodes();
            
            // 创建ViewNode
            foreach (var metadata in sortedMetadata)
            {
                if (!NodeDic.ContainsKey(metadata.Node))
                {
                    var creationTask = PrepareViewNodeCreation(metadata);
                    if (creationTask != null)
                    {
                        CreateAndAddViewNode(creationTask);
                    }
                }
            }
            
            // 创建Edge连接
            foreach (var metadata in sortedMetadata)
            {
                if (metadata.Parent != null)
                {
                    if (NodeDic.TryGetValue(metadata.Node, out var childViewNode) &&
                        NodeDic.TryGetValue(metadata.Parent.Node, out var parentViewNode))
                    {
                        CreateEdgeConnection(parentViewNode, childViewNode, metadata);
                    }
                }
            }
        }

        /// <summary>
        /// 并行准备ViewNode数据 - 使用逻辑层优化的排序（修复主线程问题）
        /// </summary>
        private async Task<List<ViewNodeCreationTask>> PrepareViewNodesAsync(System.Threading.CancellationToken cancellationToken)
        {
            var sortedMetadata = _nodeTree.GetSortedNodes();
            var creationTasks = new List<ViewNodeCreationTask>();
            
            // 在主线程中预处理所有ViewNode创建数据，避免在后台线程中调用Unity API
            await ExecuteOnMainThreadAsync(() =>
            {
                foreach (var metadata in sortedMetadata)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var creationTask = PrepareViewNodeCreation(metadata);
                    if (creationTask != null)
                    {
                        creationTasks.Add(creationTask);
                    }
                }
            });
            
            return creationTasks;
        }

        /// <summary>
        /// 准备单个ViewNode的创建数据
        /// </summary>
        private ViewNodeCreationTask PrepareViewNodeCreation(JsonNodeTree.NodeMetadata metadata)
        {
            var node = metadata.Node;
            
            // 跳过已存在的节点
            if (NodeDic.ContainsKey(node))
                return null;

            // 预处理节点类型信息
            var nodeType = node.GetType();
            var nodeInfo = nodeType.GetCustomAttribute<NodeInfoAttribute>();
            
            return new ViewNodeCreationTask
            {
                Node = node,
                Metadata = metadata,
                NodeType = nodeType,
                NodeInfo = nodeInfo,
                Position = new Rect(node.Position, Vector2.zero)
            };
        }

        /// <summary>
        /// 批量添加ViewNode到UI - 在主线程执行以确保UI安全（Unity编辑器优化）
        /// </summary>
        private async Task AddViewNodesToUIAsync(List<ViewNodeCreationTask> creationTasks, System.Threading.CancellationToken cancellationToken)
        {
            const int batchSize = 25; // 减少批次大小以提高响应性
            const int maxBatchesPerFrame = 2; // 每帧最多处理2个批次
            
            for (int i = 0; i < creationTasks.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var batch = creationTasks.Skip(i).Take(batchSize);
                
                // 直接在主线程执行UI操作，避免不必要的线程切换
                foreach (var task in batch)
                {
                    CreateAndAddViewNode(task);
                }
                
                // 定期让出控制权，保持编辑器响应性
                if ((i / batchSize) % maxBatchesPerFrame == 0)
                {
                    // 使用Unity编辑器友好的延时方式，避免Unity API调用
                    await Task.Yield();
                    
                    // 给编辑器一些处理时间，不使用Unity Time API
                    await Task.Delay(16, cancellationToken); // 约1帧的时间（60fps = 16.67ms）
                }
            }
        }

        /// <summary>
        /// 创建并添加ViewNode到图表
        /// </summary>
        private void CreateAndAddViewNode(ViewNodeCreationTask creationTask)
        {
            if (NodeDic.ContainsKey(creationTask.Node))
                return;

            ViewNode viewNode;
            if (creationTask.Node.PrefabData != null)
            {
                viewNode = new PrefabViewNode(creationTask.Node, this);
            }
            else
            {
                viewNode = new ViewNode(creationTask.Node, this);
            }
            
            viewNode.SetPosition(creationTask.Position);
            ViewNodes.Add(viewNode);
            NodeDic.Add(creationTask.Node, viewNode);
            AddElement(viewNode);
            
            // 使用同步方式初始化子节点以提升性能
            viewNode.AddChildNodesSynchronously();
        }

        /// <summary>
        /// 并行创建Edge连接 - 基于逻辑层的层次结构（修复主线程问题）
        /// </summary>
        private async Task CreateEdgesAsync(System.Threading.CancellationToken cancellationToken)
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
        private async Task CreateEdgeForNodeAsync(JsonNodeTree.NodeMetadata childMetadata, System.Threading.CancellationToken cancellationToken)
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

        /// <summary>
        /// 在主线程执行操作 - 简化版本，避免Unity API调用
        /// </summary>
        private async Task ExecuteOnMainThreadAsync(System.Action action)
        {
            // 简化主线程检测，避免复杂的线程ID判断
            // 在Unity编辑器环境中，大多数情况下我们已经在主线程
            try
            {
                // 直接执行操作，如果不在主线程会抛出异常
                action();
                return;
            }
            catch (System.InvalidOperationException)
            {
                // 如果不在主线程，使用调度器
            }
            
            var tcs = new TaskCompletionSource<bool>();
            
            // 使用Unity编辑器的调度器确保在主线程执行
            schedule.Execute(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            
            await tcs.Task;
        }

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

        // ...existing methods remain unchanged...

        public virtual string Copy(IEnumerable<GraphElement> elements)
        {
            return "";
        }
        public virtual bool CanPaste(string data)
        {
            return false;
        }

        public virtual void Paste(string operationName, string data)
        {

        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            //Debug.Log(evt.target.GetType());
            if (evt.target is PrefabViewNode node)
            {
                evt.menu.AppendAction(I18n.Goto, (d) => { node.OpenPrefabAsset(); }, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView && nodeCreationRequest != null)
            {
                evt.menu.AppendAction(I18n.CreateNode, OnContextMenuNodeCreate, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is PropertyElement element)
            {
                evt.menu.AppendAction(I18n.PrintFieldPath, delegate
                {
                    string path = element.GetGlobalPath();
                    Debug.Log(path);
                });
                evt.menu.AppendSeparator();
            }
            if (evt.target is ViewNode viewNode)
            {
                evt.menu.AppendAction(I18n.EditNode, delegate
                {
                    EditNodeScript(viewNode.Data.GetType());
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction(I18n.PrintNodePath, delegate
                {
                    Debug.Log(viewNode.GetNodePath());
                });
                evt.menu.AppendSeparator();
            }


            if (evt.target is GraphView || evt.target is Node || evt.target is Group || evt.target is Edge)
            {
                evt.menu.AppendAction(I18n.Delete, delegate
                {
                    DeleteSelectionCallback(AskUser.DontAskUser);
                }, (DropdownMenuAction a) => canDeleteSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
            }
            if (evt.target is GraphView)
            {
                evt.menu.AppendAction(I18n.Format, FormatAllNodes, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendAction("Show Tree View", ShowTreeView, DropdownMenuAction.AlwaysEnabled);
                evt.menu.AppendSeparator();
            }
        }

        /// <summary>
        /// Open the script editor
        /// </summary>
        /// <param name="type"></param>
        void EditNodeScript(Type type)
        {
            string typeName = type.Name;
            string[] guids = AssetDatabase.FindAssets("t:Script a:assets");
            System.Text.RegularExpressions.Regex classRegex = new($@"\bclass\s+{typeName}\b");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string[] fileLines = File.ReadAllLines(assetPath);

                for (int i = 0; i < fileLines.Length; i++)
                {
                    if (classRegex.IsMatch(fileLines[i]))
                    {
                        // 打开脚本文件并定位到类定义的行
                        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath), i + 1);
                        return;
                    }
                }
            }

            Debug.LogError($"Script for {type.Name} not found.");
        }

        private void FormatAllNodes(DropdownMenuAction a)
        {
            // 调用实际的格式化方法
            FormatNodes();
            Window.History.AddStep();
        }

        private void ShowTreeView(DropdownMenuAction a)
        {
            string treeView = GetTreeView();
            Debug.Log("Node Tree View:\n" + treeView);
        }

        private void OnContextMenuNodeCreate(DropdownMenuAction a)
        {
            RequestNodeCreation(null, -1, a.eventInfo.mousePosition);
        }
        private void RequestNodeCreation(VisualElement target, int index, Vector2 position)
        {
            if (nodeCreationRequest != null)
            {
                Vector2 screenMousePosition = Window.position.position + position;
                nodeCreationRequest(new NodeCreationContext
                {
                    screenMousePosition = screenMousePosition,
                    target = target,
                    index = index
                });
            }
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

        private void ShowSearchWindow(NodeCreationContext context)
        {
            SearchProvider.Target = (VisualElement)focusController.focusedElement;
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), SearchProvider);
        }


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

        public virtual void OnSave() { }

        public virtual void RemoveViewNode(ViewNode node)
        {
            RemoveNode(node.Data);
            ViewNodes.Remove(node);
            NodeDic.Remove(node.Data);
        }

        public void MakeDirty()
        {
            Window.MakeDirty();
        }

        public void SaveAsset()
        {
            Asset.Data.Nodes = Asset.Data.Nodes.Distinct().ToList();
            NodeTree.RefreshIfNeeded();
            File.WriteAllText(Window.Path, Json.ToJson(Asset));
        }

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

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _renderCancellationSource?.Cancel();
            _renderCancellationSource?.Dispose();
            _renderSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// ViewNode创建任务数据结构
    /// </summary>
    internal class ViewNodeCreationTask
    {
        public JsonNode Node { get; set; }
        public JsonNodeTree.NodeMetadata Metadata { get; set; }
        public Type NodeType { get; set; }
        public NodeInfoAttribute NodeInfo { get; set; }
        public Rect Position { get; set; }
    }
}
