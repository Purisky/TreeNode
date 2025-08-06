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
using UnityEngine.Profiling.Memory.Experimental;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;
using static TreeNode.Runtime.JsonNodeTree;
using Timer = TreeNode.Utility.Timer;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView : GraphView
    {
        public JsonAsset Asset => Window.JsonAsset;
        public TreeNodeGraphWindow Window;
        public TreeNodeWindowSearchProvider SearchProvider;
        public VisualElement ViewContainer;
        protected ContentZoomer m_Zoomer;
        

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
             DrawNodesAsync();

            SetupZoom(0.2f, 2f);
            canPasteSerializedData = CanPaste;
            serializeGraphElements = Copy;
            unserializeAndPaste = Paste;
            window.RemoveChangeMark();
        }



        /// <summary>
        /// 同步初始化JsonNodeTree - 确保逻辑层数据准备完成
        /// 使用Runtime命名空间中的JsonNodeTree
        /// </summary>
        private void InitializeNodeTreeSync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _nodeTree = new Runtime.JsonNodeTree(Asset.Data);
            stopwatch.Stop();
            
            //Debug.Log($"JsonNodeTree initialized in {stopwatch.ElapsedMilliseconds}ms with {_nodeTree.TotalNodeCount} nodes");
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
            FormatNodes();
            //Window.History.AddStep();
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

        #endregion

        #region 高性能异步渲染系统

        /// <summary>
        /// 高性能异步节点渲染 - 优化版本
        /// </summary>
        private void DrawNodesAsync()
        {
            using (new Timer($"渲染[{Window.Path}]"))
            {
                //using (new Timer("CreateViewNodesAsyncOptimized"))
                {
                    CreateViewNodesAsyncOptimized();
                }
                //using (new Timer("CreateEdgesAsync"))
                {
                    CreateEdgesAsync();
                }
            }

        }

        /// <summary>
        /// 优化的异步ViewNode创建 - 实现真正的异步优化
        /// </summary>
        private void CreateViewNodesAsyncOptimized()
        {
            var sortedNodes = _nodeTree.GetSortedNodes();
            var totalNodes = sortedNodes.Count;

            if (totalNodes == 0) return;
            NodePrepData[] nodeDataPrep;
            //using (new Timer("异步数据预准备"))
            {
                // 预准备节点数据 - 异步执行
                nodeDataPrep = PrepareNodeDataParallel(sortedNodes);
            }


            //using (new Timer($"UI创建[{nodeDataPrep.Length}]"))
            {
                // 第二阶段：渐进式UI创建（主线程小批次）
                CreateUIProgressively(nodeDataPrep);
            }

        }

        /// <summary>
        /// 异步数据预准备阶段 - 在后台线程执行
        /// </summary>
        private NodePrepData[] PrepareNodeDataParallel(List<NodeMetadata> sortedNodes)
        {
            var prepDataList = new NodePrepData[sortedNodes.Count];
            //using (new Timer("PrepareNodeDataParallel"))
            {
                Parallel.For(0, sortedNodes.Count, (i) =>
                {
                    var metadata = sortedNodes[i];
                    var nodeType = metadata.Node.GetType();
                    var prepData = new NodePrepData
                    {
                        Node = metadata.Node,
                        NodeType = nodeType,
                        IsPrefab = metadata.Node.PrefabData != null,
                        Drawer = DrawerManager.Get(nodeType), // 预获取Drawer
                        NodeInfo = nodeType.GetCustomAttribute<NodeInfoAttribute>(),
                        Position = metadata.Node.Position
                    };
                    prepDataList[i] = prepData;
                });
            }
            return prepDataList;

        }
        private void CreateUIProgressively(NodePrepData[] nodeDataPrep)
        {
            for (int i = 0; i < nodeDataPrep.Length; i++)
            {
                CreateViewNodeOptimized(nodeDataPrep[i]);
            }
        }

        /// <summary>
        /// 优化的ViewNode创建 - 使用预准备数据
        /// </summary>
        private void CreateViewNodeOptimized(NodePrepData prepData)
        {
            if (NodeDic.ContainsKey(prepData.Node))
            {
                return; // 已存在，跳过
            }
            ViewNode viewNode;
            // 使用预准备的数据快速创建
            if (prepData.IsPrefab)
            {
                viewNode = new PrefabViewNode(prepData.Node, this);
            }
            else
            {

                viewNode = CreateViewNodeFast(prepData);
            }

            ViewNodes.Add(viewNode);
            NodeDic.Add(prepData.Node, viewNode);
            AddElement(viewNode);

            // 延迟复杂初始化（可选优化）
            if (prepData.Drawer != null)
            {
                schedule.Execute(() =>
                {
                    CompleteViewNodeInitialization(viewNode, prepData);
                }).ExecuteLater(1);
            }
        }

        /// <summary>
        /// 快速ViewNode创建 - 最小化构造时间
        /// </summary>
        private ViewNode CreateViewNodeFast(NodePrepData prepData)
        {
            // 创建最基础的ViewNode，延迟复杂初始化
            var viewNode = new ViewNode(prepData.Node, this);
            
            // 应用预准备的位置信息
            viewNode.SetPosition(new Rect(prepData.Position, new Vector2()));
            
            return viewNode;
        }

        /// <summary>
        /// 完成ViewNode的延迟初始化
        /// </summary>
        private void CompleteViewNodeInitialization(ViewNode viewNode, NodePrepData prepData)
        {
            try
            {
                // 这里可以添加延迟的复杂初始化逻辑
                // 例如：复杂的样式应用、动画等
                viewNode.OnChange();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"完成ViewNode初始化失败: {e.Message}");
            }
        }

        /// <summary>
        /// 计算最优批次大小 - 同步优化版本
        /// </summary>
        private int CalculateOptimalBatchSize(PerformanceTracker tracker, int minSize, int maxSize)
        {
            var avgTime = tracker.GetAverageTime();
            var lastBatchSize = tracker.LastBatchSize;
            var throughput = tracker.GetAverageThroughput();
            var isImproving = tracker.IsPerformanceImproving();
            var stability = tracker.GetTimeStabilityIndex();
            
            // 同步处理的目标：每批次耗时在10-25ms之间
            
            // 如果性能正在改善且稳定，可以适度增加批次大小
            if (isImproving && stability < 3) // 稳定性阈值：3ms
            {
                if (avgTime < 15)
                {
                    return Math.Min(maxSize, lastBatchSize + 2); // 适度增加
                }
            }
            
            // 基于平均时间的标准调整
            if (avgTime < 10) // 太快，增加批次大小
            {
                int increment = stability < 2 ? 3 : 2; // 稳定时增加更多
                return Math.Min(maxSize, lastBatchSize + increment);
            }
            else if (avgTime > 25) // 太慢，减少批次大小
            {
                return Math.Max(minSize, lastBatchSize - 2);
            }
            else if (avgTime > 20) // 稍慢，小幅减少
            {
                return Math.Max(minSize, lastBatchSize - 1);
            }
            
            // 基于吞吐量的微调
            if (throughput > 200) // 高吞吐量，可以尝试增加批次
            {
                return Math.Min(maxSize, lastBatchSize + 1);
            }
            else if (throughput < 50) // 低吞吐量，减少批次
            {
                return Math.Max(minSize, lastBatchSize - 1);
            }
            
            return lastBatchSize; // 保持当前大小
        }

        /// <summary>
        /// 计算自适应延迟 - 同步优化版本
        /// </summary>
        private int CalculateAdaptiveDelay(double batchTime)
        {
            // 同步处理的延迟策略：保持UI响应性
            if (batchTime < 8) return 0;    // 很快，无延迟
            if (batchTime < 15) return 1;   // 快，短延迟
            if (batchTime < 25) return 2;   // 正常，标准延迟
            if (batchTime < 40) return 4;   // 较慢，长延迟
            return 6;                       // 很慢，更长延迟
        }

        public virtual void ApplyChanges(List<ViewChange> changes)
        {
            //Debug.Log($"应用 {changes.Count} 个变化");
            if (changes == null || changes.Count == 0) { return; }
            ViewNode viewNode;
            for (int i = 0; i < changes.Count; i++)
            {
                //Debug.Log(changes[i].ChangeType);
                switch (changes[i].ChangeType)
                {
                    case ViewChangeType.NodeCreate:
                        CreateViewNodeSafe(changes[i].Node);
                        break;
                    case ViewChangeType.NodeDelete:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            RemoveElement(viewNode);
                            ViewNodes.Remove(viewNode);
                            NodeDic.Remove(changes[i].Node);
                        }
                        break;
                    case ViewChangeType.NodeField:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            if (changes[i].Path == PAPath.Position)
                            {
                                Rect rect = viewNode.GetPosition();
                                rect.position = viewNode.Data.Position;
                                viewNode.SetPosition(rect);
                            }
                            else
                            {
                                viewNode.RefreshPropertyElements(changes[i].Path);
                            }
                        }
                        break;
                    case ViewChangeType.EdgeCreate:
                        NodeMetadata metadata =  NodeTree.GetNodeMetadata(changes[i].Node);
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode) && NodeDic.TryGetValue(metadata.Parent.Node, out ViewNode parentNode))
                        {
                            ChildPort childPort = parentNode.GetChildPort(metadata.LocalPath);
                            Edge edge = childPort.ConnectTo(viewNode.ParentPort);
                            childPort.OnAddEdge(edge);
                            AddElement(edge);
                        }
                        break;
                    case ViewChangeType.EdgeDelete:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            if (viewNode.ParentPort.connected)
                            {
                                Edge edge = viewNode.ParentPort.connections.First();
                                viewNode.ParentPort.DisconnectAll();
                                edge.ChildPort().OnRemoveEdge(edge);
                                RemoveElement(edge);
                            }
                        }
                        break;
                    case ViewChangeType.ListItem:
                        if (NodeDic.TryGetValue(changes[i].Node, out viewNode))
                        {
                            viewNode.RefreshList(changes[i]);
                        }
                        break;
                }
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
                    viewNode = new PrefabViewNode(node, this);
                }
                else
                {
                    viewNode = new ViewNode(node, this);
                }

                ViewNodes.Add(viewNode);
                NodeDic.Add(node, viewNode);
                AddElement(viewNode);

                // ✅ 移除异步子节点初始化调用 - 将在批量连接阶段处理
            }
            catch (Exception e)
            {
                Debug.LogWarning($"创建ViewNode失败 {node?.GetType().Name}: {e.Message}");
            }
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
            // 清理现有视图
            ViewNodes.Clear();
            NodeDic.Clear();
            ViewContainer.Query<Layer>().ForEach(p => p.Clear());


            // 重新初始化逻辑层
            InitializeNodeTreeSync();

            // 启动异步重新渲染
             DrawNodesAsync();
            Window.RemoveChangeMark();
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

    /// <summary>
    /// 节点预准备数据结构
    /// </summary>
    public class NodePrepData
    {
        public JsonNode Node { get; set; }
        public Type NodeType { get; set; }
        public bool IsPrefab { get; set; }
        public BaseDrawer Drawer { get; set; }
        public NodeInfoAttribute NodeInfo { get; set; }
        public Vec2 Position { get; set; }
    }

    /// <summary>
    /// 性能跟踪器 - 并行优化版本，支持更精确的性能监控
    /// </summary>
    public class PerformanceTracker
    {
        private readonly Queue<PerformanceSample> _recentSamples = new Queue<PerformanceSample>();
        private const int MaxSamples = 15; // 增加样本数量获得更稳定的统计
        
        public int LastBatchSize { get; private set; } = 5; // 增加初始批次大小

        /// <summary>
        /// 性能样本数据
        /// </summary>
        private struct PerformanceSample
        {
            public double TimeMs { get; set; }
            public int BatchSize { get; set; }
            public double Throughput { get; set; } // 吞吐量：节点数/秒
        }

        public void RecordBatch(double timeMs, int batchSize)
        {
            var throughput = batchSize / (timeMs / 1000.0); // 节点数/秒
            
            var sample = new PerformanceSample
            {
                TimeMs = timeMs,
                BatchSize = batchSize,
                Throughput = throughput
            };
            
            _recentSamples.Enqueue(sample);
            LastBatchSize = batchSize;
            
            if (_recentSamples.Count > MaxSamples)
            {
                _recentSamples.Dequeue();
            }
        }

        public double GetAverageTime()
        {
            return _recentSamples.Count > 0 ? _recentSamples.Average(s => s.TimeMs) : 0;
        }

        /// <summary>
        /// 获取平均吞吐量
        /// </summary>
        public double GetAverageThroughput()
        {
            return _recentSamples.Count > 0 ? _recentSamples.Average(s => s.Throughput) : 0;
        }

        /// <summary>
        /// 获取性能稳定性指标（标准差）
        /// </summary>
        public double GetTimeStabilityIndex()
        {
            if (_recentSamples.Count < 3) return 0;
            
            var times = _recentSamples.Select(s => s.TimeMs).ToArray();
            var mean = times.Average();
            var variance = times.Sum(t => Math.Pow(t - mean, 2)) / times.Length;
            return Math.Sqrt(variance);
        }

        /// <summary>
        /// 获取性能趋势（最近的性能是否在改善）
        /// </summary>
        public bool IsPerformanceImproving()
        {
            if (_recentSamples.Count < 5) return false;
            
            var samples = _recentSamples.ToArray();
            var recentHalf = samples.Skip(samples.Length / 2).Select(s => s.Throughput).Average();
            var earlierHalf = samples.Take(samples.Length / 2).Select(s => s.Throughput).Average();
            
            return recentHalf > earlierHalf; // 吞吐量提升表示性能改善
        }

        /// <summary>
        /// 重置跟踪器
        /// </summary>
        public void Reset()
        {
            _recentSamples.Clear();
            LastBatchSize = 5;
        }
    }
}
