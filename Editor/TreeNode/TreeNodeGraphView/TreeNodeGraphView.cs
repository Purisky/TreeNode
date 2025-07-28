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
        public TreeNodeWindowSearchProvider SearchProvider;
        public VisualElement ViewContainer;
        protected ContentZoomer m_Zoomer;

        // 异步渲染相关
        private CancellationTokenSource _renderCancellationSource;
        private readonly object _renderLock = new object();
        private volatile bool _isDrawing = false;

        // 节点移动历史记录相关
        private Dictionary<ViewNode, Vec2> _nodePositionsBeforeMove = new Dictionary<ViewNode, Vec2>();
        public bool _isRecordingMove = false; // 改为public，让ViewNode能访问
        private readonly object _moveRecordLock = new object();

        #region 构造函数和初始化

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

            // 使用优化的异步方式渲染节点
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

        #endregion

        #region GraphView变化事件处理

        /// <summary>
        /// 处理GraphView的变化事件，包括节点移动、删除等
        /// </summary>
        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            try
            {
                // 处理节点移动
                if (graphViewChange.movedElements != null && graphViewChange.movedElements.Count > 0)
                {
                    HandleNodesMoved(graphViewChange.movedElements);
                }

                // 处理元素删除
                if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
                {
                    HandleElementsToRemove(graphViewChange.elementsToRemove);
                }

                // 处理边连接变化
                if (graphViewChange.edgesToCreate != null && graphViewChange.edgesToCreate.Count > 0)
                {
                    HandleEdgesToCreate(graphViewChange.edgesToCreate);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"处理GraphView变化时出错: {e.Message}");
                UnityEngine.Debug.LogException(e);
            }

            return graphViewChange;
        }

        /// <summary>
        /// 处理节点移动事件 - 记录Position字段变化
        /// </summary>
        private void HandleNodesMoved(List<GraphElement> movedElements)
        {
            lock (_moveRecordLock)
            {
                if (_isRecordingMove)
                    return; // 防止递归调用

                _isRecordingMove = true;
                
                try
                {
                    var movedNodes = movedElements.OfType<ViewNode>().ToList();
                    if (movedNodes.Count == 0)
                        return;

                    // 开始批量操作记录多个节点的移动
                    if (movedNodes.Count > 1)
                    {
                        Window.History.BeginBatch($"移动 {movedNodes.Count} 个节点");
                    }

                    foreach (var viewNode in movedNodes)
                    {
                        RecordNodePositionChange(viewNode);
                    }

                    // 结束批量操作
                    if (movedNodes.Count > 1)
                    {
                        Window.History.EndBatch();
                    }
                    else
                    {
                        // 单个节点移动直接添加步骤
                        Window.History.AddStep();
                    }
                }
                finally
                {
                    _isRecordingMove = false;
                }
            }
        }

        /// <summary>
        /// 记录单个节点的位置变化
        /// </summary>
        private void RecordNodePositionChange(ViewNode viewNode)
        {
            if (viewNode?.Data == null)
                return;

            try
            {
                // 获取移动前的位置
                Vec2 oldPosition = new Vec2(0, 0);
                if (_nodePositionsBeforeMove.TryGetValue(viewNode, out var cachedOldPos))
                {
                    oldPosition = cachedOldPos;
                    _nodePositionsBeforeMove.Remove(viewNode);
                }
                else
                {
                    // 如果没有缓存的旧位置，使用JsonNode中的当前位置作为旧位置
                    oldPosition = viewNode.Data.Position;
                }

                // 获取移动后的新位置
                var currentRect = viewNode.GetPosition();
                Vec2 newPosition = new Vec2((int)currentRect.x, (int)currentRect.y);

                // 检查位置是否真的发生了变化
                if (oldPosition.x == newPosition.x && oldPosition.y == newPosition.y)
                    return;

                // 更新JsonNode的Position
                viewNode.Data.Position = newPosition;

                // 使用专门的位置修改操作
                var positionOperation = new PositionModifyOperation(
                    viewNode.Data,
                    oldPosition,
                    newPosition,
                    this
                );

                // 记录到History系统
                Window.History.RecordOperation(positionOperation);

                //Debug.Log($"记录节点 {viewNode.Data.GetType().Name} 位置变化: ({oldPosition.x},{oldPosition.y}) -> ({newPosition.x},{newPosition.y})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"记录节点位置变化失败: {e.Message}");
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// 处理元素删除
        /// </summary>
        private void HandleElementsToRemove(List<GraphElement> elementsToRemove)
        {
            var nodesToRemove = elementsToRemove.OfType<ViewNode>().ToList();
            if (nodesToRemove.Count == 0)
                return;

            // 开始批量删除操作
            if (nodesToRemove.Count > 1)
            {
                Window.History.BeginBatch($"删除 {nodesToRemove.Count} 个节点");
            }

            foreach (var viewNode in nodesToRemove)
            {
                if (viewNode?.Data != null)
                {
                    // 确定节点在当前结构中的位置
                    var currentLocation = DetermineNodeLocation(viewNode.Data);
                    
                    // 记录删除操作
                    var deleteOperation = new NodeDeleteOperation(viewNode.Data, currentLocation, this);
                    Window.History.RecordOperation(deleteOperation);
                }
            }

            // 结束批量操作
            if (nodesToRemove.Count > 1)
            {
                Window.History.EndBatch();
            }
            else
            {
                Window.History.AddStep();
            }
        }

        /// <summary>
        /// 处理边连接创建
        /// </summary>
        private void HandleEdgesToCreate(List<Edge> edgesToCreate)
        {
            foreach (var edge in edgesToCreate)
            {
                if (edge.output is ChildPort childPort &&
                    edge.input is ParentPort parentPort &&
                    childPort.node is ViewNode parentViewNode &&
                    parentPort.node is ViewNode childViewNode)
                {
                    var edgeOperation = new EdgeCreateOperation(
                        parentViewNode.Data,
                        childViewNode.Data,
                        childPort.portName,
                        this
                    );
                    
                    Window.History.RecordOperation(edgeOperation);
                }
            }
            
            if (edgesToCreate.Count > 0)
            {
                Window.History.AddStep();
            }
        }

        /// <summary>
        /// 确定节点的位置信息
        /// </summary>
        private NodeLocation DetermineNodeLocation(JsonNode node)
        {
            // 检查是否为根节点
            int rootIndex = Asset.Data.Nodes.IndexOf(node);
            if (rootIndex >= 0)
            {
                return NodeLocation.Root(rootIndex);
            }

            // 检查是否为子节点（这里需要遍历所有节点查找父子关系）
            // 这是一个简化的实现，实际项目中可能需要更复杂的逻辑
            try
            {
                if (NodeDic.TryGetValue(node, out var viewNode) && viewNode.GetParent() != null)
                {
                    var parentViewNode = viewNode.GetParent();
                    if (parentViewNode?.ParentPort?.connections?.FirstOrDefault()?.output is ChildPort childPort)
                    {
                        return NodeLocation.Child(
                            parentViewNode.Data,
                            childPort.portName,
                            childPort is MultiPort,
                            childPort is MultiPort ? (viewNode.ParentPort?.Index ?? 0) : 0
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"确定节点位置时出错: {e.Message}");
            }

            return NodeLocation.Unknown();
        }

        /// <summary>
        /// 在节点开始移动时缓存其原始位置
        /// </summary>
        public void CacheNodePositionBeforeMove(ViewNode viewNode)
        {
            if (viewNode?.Data == null)
                return;

            lock (_moveRecordLock)
            {
                _nodePositionsBeforeMove[viewNode] = viewNode.Data.Position;
            }
        }

        /// <summary>
        /// 批量缓存多个节点的移动前位置
        /// </summary>
        public void CacheNodePositionsBeforeMove(IEnumerable<ViewNode> viewNodes)
        {
            lock (_moveRecordLock)
            {
                foreach (var viewNode in viewNodes)
                {
                    if (viewNode?.Data != null)
                    {
                        _nodePositionsBeforeMove[viewNode] = viewNode.Data.Position;
                    }
                }
            }
        }

        #endregion

        #region 右键菜单和用户交互

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

        private void ShowSearchWindow(NodeCreationContext context)
        {
            SearchProvider.Target = (VisualElement)focusController.focusedElement;
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), SearchProvider);
        }

        #endregion
        #region 复制粘贴功能

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

        #endregion
        #region 资源管理和保存

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

        public virtual void OnSave() { }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _renderCancellationSource?.Cancel();
            _renderCancellationSource?.Dispose();
        }

        #endregion

        #region 高性能异步渲染系统

        /// <summary>
        /// 高性能异步节点渲染 - 第三阶段性能优化
        /// </summary>
        private async Task DrawNodesAsync()
        {
            lock (_renderLock)
            {
                if (_isDrawing)
                {
                    return;
                }
                _isDrawing = true;
            }

            try
            {
                // 创建新的取消令牌
                _renderCancellationSource?.Cancel();
                _renderCancellationSource = new CancellationTokenSource();
                var cancellationToken = _renderCancellationSource.Token;

                var startTime = DateTime.Now;
                Debug.Log($"开始异步渲染 {_nodeTree.TotalNodeCount} 个节点");

                // 第一步：并行创建所有ViewNode（主线程执行UI创建）
                await CreateViewNodesAsync(cancellationToken);

                // 检查取消状态
                cancellationToken.ThrowIfCancellationRequested();

                // 第二步：异步建立Edge连接
                await CreateEdgesAsync(cancellationToken);

                // 检查取消状态
                cancellationToken.ThrowIfCancellationRequested();

                // 第三步：优化布局和最终渲染
                await OptimizeLayoutAsync(cancellationToken);

                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                Debug.Log($"异步渲染完成，耗时: {elapsed:F2}ms");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("渲染任务被取消");
            }
            catch (Exception e)
            {
                Debug.LogError($"异步渲染失败: {e.Message}");
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                lock (_renderLock)
                {
                    _isDrawing = false;
                }
            }
        }

        /// <summary>
        /// 并行创建ViewNode - 基于逻辑层排序的优化版本
        /// </summary>
        private async Task CreateViewNodesAsync(CancellationToken cancellationToken)
        {
            var sortedNodes = _nodeTree.GetSortedNodes();
            var batchSize = Math.Max(10, sortedNodes.Count / 4); // 动态批次大小
            var batches = new List<List<JsonNodeTree.NodeMetadata>>();

            // 将节点分批处理
            for (int i = 0; i < sortedNodes.Count; i += batchSize)
            {
                var batch = sortedNodes.Skip(i).Take(batchSize).ToList();
                batches.Add(batch);
            }

            // 在主线程中批量创建ViewNode
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ExecuteOnMainThreadAsync(() =>
                {
                    foreach (var metadata in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CreateViewNodeSafe(metadata.Node);
                    }
                });

                // 给UI线程喘息的机会
                await Task.Yield();
            }
        }

        /// <summary>
        /// 安全创建ViewNode - 避免重复创建
        /// </summary>
        private void CreateViewNodeSafe(JsonNode node)
        {
            if (NodeDic.ContainsKey(node))
            {
                return; // 已存在，跳过
            }

            try
            {
                ViewNode viewNode;
                if (node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(node, this, null);
                }
                else
                {
                    viewNode = new ViewNode(node, this, null);
                }

                viewNode.SetPosition(new Rect(node.Position, new Vector2()));
                ViewNodes.Add(viewNode);
                NodeDic.Add(node, viewNode);
                AddElement(viewNode);

                // 使用优化的同步子节点初始化
                viewNode.AddChildNodesSynchronously();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"创建ViewNode失败 {node?.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// 异步创建边连接
        /// </summary>
        private async Task CreateEdgesAsync(CancellationToken cancellationToken)
        {
            await ExecuteOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 为所有ViewNode建立子节点连接
                foreach (var viewNode in ViewNodes.ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        // 使用同步方式初始化子节点，建立Edge连接
                        viewNode.AddChildNodesUntilListInited();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"建立节点 {viewNode.Data?.GetType().Name} 的边连接失败: {e.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// 优化布局 - 异步版本
        /// </summary>
        private async Task OptimizeLayoutAsync(CancellationToken cancellationToken)
        {
            await ExecuteOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 执行布局优化
                var rootNodes = ViewNodes.Where(n => n.GetDepth() == 0).ToList();
                
                // 按类型分组进行布局优化
                var nodeGroups = rootNodes.GroupBy(n => n.Data.GetType()).ToList();
                
                float yOffset = 50f;
                foreach (var group in nodeGroups)
                {
                    float xOffset = 50f;
                    foreach (var node in group)
                    {
                        var currentPos = node.GetPosition();
                        if (currentPos.position == Vector2.zero)
                        {
                            node.SetPosition(new Rect(xOffset, yOffset, currentPos.width, currentPos.height));
                            xOffset += currentPos.width + 100f;
                        }
                    }
                    yOffset += 200f;
                }
            });
        }

        /// <summary>
        /// 在主线程执行异步操作 - Unity编辑器优化版本
        /// </summary>
        private async Task ExecuteOnMainThreadAsync(Action action)
        {
            // Unity 编辑器中的主线程执行优化
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == 1)
            {
                // 已经在主线程，直接执行
                action();
                return;
            }

            // 使用TaskCompletionSource在主线程执行
            var tcs = new TaskCompletionSource<bool>();

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

        /// <summary>
        /// 优化的重绘方法 - 支持增量和全量渲染
        /// </summary>
        public void Redraw()
        {
            lock (_renderLock)
            {
                // 取消当前的渲染任务
                _renderCancellationSource?.Cancel();
            }

            // 清理现有视图
            ViewNodes.Clear();
            NodeDic.Clear();
            ViewContainer.Query<Layer>().ForEach(p => p.Clear());

            // 重新初始化逻辑层
            InitializeNodeTreeSync();

            // 启动异步重新渲染
            _ = DrawNodesAsync();
        }

        /// <summary>
        /// 增量重绘 - 只更新变化的部分
        /// </summary>
        public void IncrementalRedraw(IEnumerable<JsonNode> changedNodes = null)
        {
            if (changedNodes == null)
            {
                Redraw();
                return;
            }

            var nodesToUpdate = changedNodes.ToList();
            if (nodesToUpdate.Count > ViewNodes.Count / 2)
            {
                // 如果变化太多，使用全量重绘
                Redraw();
                return;
            }

            // 增量更新
            foreach (var node in nodesToUpdate)
            {
                if (NodeDic.TryGetValue(node, out var viewNode))
                {
                    // 刷新单个节点
                    viewNode.RefreshPropertyElements();
                }
            }
        }

        #endregion
    }
}
