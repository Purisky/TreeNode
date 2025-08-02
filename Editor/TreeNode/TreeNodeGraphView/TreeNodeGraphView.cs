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

        public virtual void ApplyChanges(List<ViewChange> changes)
        {
            if (changes == null || changes.Count == 0) { return; }
            for (int i = 0; i < changes.Count; i++)
            {
                switch (changes[i].ChangeType)
                {
                    case ViewChangeType.NodeCreate:
                        CreateViewNodeSafe(changes[i].Node);
                        break;
                    case ViewChangeType.NodeDelete:
                        if (NodeDic.TryGetValue(changes[i].Node, out var viewNode))
                        {
                            RemoveElement(viewNode);
                            ViewNodes.Remove(viewNode);
                            NodeDic.Remove(changes[i].Node);
                        }
                        break;
                    case ViewChangeType.NodeField:
                        if (NodeDic.TryGetValue(changes[i].Node, out var fieldNode))
                        {
                            //fieldNode.RefreshPropertyElements(changes[i].Path);
                        }
                        break;
                    case ViewChangeType.EdgeCreate:
                        break;
                    case ViewChangeType.EdgeDelete:
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
