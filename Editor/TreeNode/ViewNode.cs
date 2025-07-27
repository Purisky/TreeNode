using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// 优化后的ViewNode - 支持异步渲染和高性能初始化
    /// </summary>
    public class ViewNode : Node
    {
        public TreeNodeGraphView View;
        public ParentPort ParentPort;
        public List<ChildPort> ChildPorts;
        public VisualElement Content;

        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ViewNode");

        public JsonNode Data;

        // 异步初始化相关
        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private TaskCompletionSource<bool> _initializationTask;

        public ViewNode(JsonNode data, TreeNodeGraphView view, ChildPort childPort = null)
        {
            Data = data;
            View = view;
            
            // 快速初始化基础UI结构
            InitializeUIStructure();
            
            // 异步或同步绘制内容
            Draw(childPort);
            OnChange();
        }

        /// <summary>
        /// 快速初始化UI结构 - 同步执行关键UI操作
        /// </summary>
        private void InitializeUIStructure()
        {
            styleSheets.Add(StyleSheet);
            AddToClassList("view-node");
            
            Type typeInfo = Data.GetType();
            NodeInfoAttribute nodeInfo = typeInfo.GetCustomAttribute<NodeInfoAttribute>();
            
            if (nodeInfo != null)
            {
                title = nodeInfo.Title;
                style.width = nodeInfo.Width;
                titleContainer.style.backgroundColor = nodeInfo.Color;
                titleContainer.Q<Label>().style.marginRight = 6;
            }
            
            titleContainer.Q<Label>().style.unityFontStyleAndWeight = FontStyle.Bold;
            this.name = typeInfo.Name;
            
            Content = this.Q<VisualElement>("contents");
            Content.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            Content.RemoveAt(1);
            Content.Q("divider").AddToClassList("hidden");
            this.Q("title-button-container").style.display = DisplayStyle.None;
            this.Q("title-label").style.unityTextAlign = TextAnchor.MiddleCenter;
            this.Q("title-label").style.flexGrow = 1;
        }

        public virtual void Draw(ChildPort childPort = null)
        {
            NodeInfoAttribute nodeInfo = Data.GetType().GetCustomAttribute<NodeInfoAttribute>();
            DrawParentPort(nodeInfo?.Type, childPort);
            DrawPropertiesAndPorts();
        }

        public List<Edge> GetAllEdges()
        {
            List<Edge> edges = new();
            if (ChildPorts != null)
            {
                for (int i = 0; i < ChildPorts.Count; i++)
                {
                    ChildPort childPort = ChildPorts[i];
                    edges.AddRange(childPort.connections);
                }
            }
            
            // 修复：获取ParentPort的所有连接，而不仅仅是第一个
            if (ParentPort != null && ParentPort.connected)
            {
                edges.AddRange(ParentPort.connections);
            }
            return edges;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Data.Position = newPos.position;
        }

        public void DrawParentPort(Type parentType, ChildPort childPort = null)
        {
            if (parentType == null) { return; }
            
            ParentPort = ParentPort.Create(parentType);
            ParentPort.OnChange = OnChange;
            titleContainer.Insert(1, ParentPort);
            
            if (childPort != null)
            {
                Edge edge = childPort.ConnectTo(ParentPort);
                View.AddElement(edge);
            }
        }

        public ViewNode GetRoot()
        {
            if (ParentPort == null || !ParentPort.connected) { return this; }
            return (ParentPort.connections.First().ChildPort().node).GetRoot();
        }
        
        public ViewNode GetParent()
        {
            if (ParentPort == null || !ParentPort.connected) { return null; }
            return ParentPort.connections.First().ChildPort().node;
        }

        public int GetDepth()
        {
            if (ParentPort == null || !ParentPort.connected) { return 0; }
            return (ParentPort.connections.First().ChildPort().node).GetDepth() + 1;
        }
        
        public int GetChildMaxDepth()
        {
            int maxDepth = GetDepth();
            if (ChildPorts != null)
            {
                foreach (var item in ChildPorts)
                {
                    foreach (var child in item.GetChildValues())
                    {
                        if (View.NodeDic.TryGetValue(child, out ViewNode viewNode))
                        {
                            maxDepth = Math.Max(maxDepth, viewNode.GetChildMaxDepth());
                        }
                    }
                }
            }
            return maxDepth;
        }
        
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }

        public void DrawPropertiesAndPorts()
        {
            ChildPorts = new();
            BaseDrawer baseDrawer = DrawerManager.Get(Data.GetType());
            if (baseDrawer != null)
            {
                MemberMeta meta = new()
                {
                    Type = Data.GetType(),
                    LabelInfo = new() { Text = Data.GetType().Name, Hide = true },
                };
                VisualElement visualElement = baseDrawer.Create(meta, this, null, OnChange);
                Content.Add(visualElement);
            }
        }

        private List<ListView> listViews;
        private HashSet<ChildPort> visitedChildPorts = new();

        public bool CheckListInited()
        {
            if (listViews == null) return true;
            
            for (int i = 0; i < listViews.Count; i++)
            {
                ListView listView = listViews[i];
                if (listView.userData is not bool)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 高性能同步方式添加所有子节点 - 绕过ListView异步初始化问题
        /// </summary>
        public void AddChildNodesSynchronously()
        {
            if (_isInitialized || _isInitializing)
                return;

            _isInitializing = true;
            
            try
            {
                // 获取所有ListView元素
                listViews = Content.Query<ListView>().ToList();
                
                // 强制同步初始化所有子端口
                if (ChildPorts != null)
                {
                    for (int i = 0; i < ChildPorts.Count; i++)
                    {
                        ChildPort childPort = ChildPorts[i];
                        if (!visitedChildPorts.Contains(childPort))
                        {
                            visitedChildPorts.Add(childPort);
                            InitChildPortSynchronously(childPort);
                        }
                    }
                }
                
                // 标记所有ListView为已初始化
                if (listViews != null)
                {
                    foreach (ListView listView in listViews)
                    {
                        if (listView.userData is not bool)
                        {
                            listView.userData = true;
                        }
                    }
                }
                
                _isInitialized = true;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// 异步初始化版本 - 提供更好的性能和用户体验（Unity编辑器优化）
        /// </summary>
        public async Task AddChildNodesAsync()
        {
            if (_isInitialized)
                return;

            if (_initializationTask != null)
            {
                await _initializationTask.Task;
                return;
            }

            _initializationTask = new TaskCompletionSource<bool>();
            _isInitializing = true;

            try
            {
                // 在主线程中执行初始化，避免Unity API线程问题
                await ExecuteOnMainThread(() =>
                {
                    if (ChildPorts != null)
                    {
                        foreach (var childPort in ChildPorts)
                        {
                            if (!visitedChildPorts.Contains(childPort))
                            {
                                visitedChildPorts.Add(childPort);
                                InitChildPortSynchronously(childPort);
                            }
                        }
                    }

                    // 更新ListView状态
                    listViews = Content.Query<ListView>().ToList();
                    if (listViews != null)
                    {
                        foreach (ListView listView in listViews)
                        {
                            if (listView.userData is not bool)
                            {
                                listView.userData = true;
                            }
                        }
                    }
                });

                _isInitialized = true;
                _initializationTask.SetResult(true);
            }
            catch (Exception e)
            {
                _initializationTask.SetException(e);
                throw;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// 增强版本 - 优先使用同步初始化，必要时回退到异步
        /// </summary>
        public void AddChildNodesUntilListInited()
        {
            if (_isInitialized)
                return;

            listViews = Content.Query<ListView>().ToList();
            
            // 总是优先尝试同步初始化 - 这可以绕过大部分异步问题
            AddChildNodesSynchronously();
            
            // 检查同步初始化是否成功
            if (CheckListInited())
            {
                // 成功！无需异步回退
                return;
            }
            
            // 如果同步初始化没有完全成功，异步处理剩余项目
            // 注释掉异步回退，因为在新的架构中我们使用完全同步的方式
            //IVisualElementScheduledItem visualElementScheduledItem = schedule.Execute(AddChildNodes).Until(CheckListInited);
        }

        public void AddChildNodes()
        {
            if (ChildPorts == null) return;
            
            //Debug.Log("AddChildNodes"+ CheckListInited());
            for (int i = 0; i < ChildPorts.Count; i++)
            {
                ChildPort childPort = ChildPorts[i];
                if (visitedChildPorts.Contains(childPort))
                {
                    continue;
                }
                visitedChildPorts.Add(childPort);
                InitChildPortSynchronously(childPort);
            }
        }

        /// <summary>
        /// 同步初始化子端口 - 优化版本
        /// </summary>
        private void InitChildPortSynchronously(ChildPort childPort)
        {
            if (childPort == null) return;
            
            List<JsonNode> children = childPort.GetChildValues();
            for (int j = 0; j < children.Count; j++)
            {
                ViewNode childNode = View.AddViewNode(children[j], childPort);
                if (childPort is MultiPort && childNode?.ParentPort != null) 
                { 
                    childNode.ParentPort.SetIndex(j); 
                }
            }
        }

        /// <summary>
        /// 公共方法，保持向后兼容性
        /// </summary>
        public void InitChildPort(ChildPort childPort)
        {
            InitChildPortSynchronously(childPort);
        }

        /// <summary>
        /// 在主线程执行操作 - ViewNode特化版本，简化主线程检测
        /// </summary>
        private async Task ExecuteOnMainThread(Action action)
        {
            // 简化主线程检测，避免复杂的线程ID和SynchronizationContext判断
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

        public string GetNodePath()
        {
            ViewNode parentNode = GetParent();
            if (parentNode == null)
            {
                int index = View.Asset.Data.Nodes.IndexOf(Data);
                return $"[{index}]";
            }
            
            ChildPort childPort = ParentPort.connections.First().ChildPort();
            PropertyElement element = childPort.GetFirstAncestorOfType<PropertyElement>();
            string path = element.LocalPath;
            
            if (childPort is NumPort)
            {
                path = $"{path}.Node";
            }
            else if (childPort is MultiPort)
            {
                path = $"{path}[{ParentPort.Index}]";
            }
            
            return $"{parentNode.GetNodePath()}.{path}";
        }

        public int GetIndex()
        {
            return View.Asset.Data.Nodes.IndexOf(Data);
        }

        public void MakeDirty()
        {
            View.MakeDirty();
        }

        public List<ViewNode> GetChildNodes()
        {
            List<ViewNode> nodes = new();
            if (ChildPorts == null) return nodes;
            
            List<ChildPort> childPorts = ChildPorts.Where(n => n.connected)
                                                   .OrderBy(n => n.worldBound.position.y)
                                                   .ToList();
            
            for (int i = 0; i < childPorts.Count; i++)
            {
                foreach (var item in childPorts[i].connections)
                {
                    nodes.Add(item.ParentPort().node);
                }
            }

            return nodes;
        }

        public HashSet<ShowIfElement> ShowIfElements = new();

        public void OnChange()
        {
            foreach (var item in ShowIfElements)
            {
                item.Refresh();
            }
        }
        
        public PropertyElement FindByLocalPath(string path)
        {
            return this.Q<PropertyElement>(path);
        }

        public bool Validate(out string msg)
        {
            msg = $"{GetNodePath()}:{Data.GetType().Name}";
            List<VisualElement> list = this.Query<VisualElement>().Where(n => n is IValidator).ToList();
            bool success = true;
            
            foreach (VisualElement item in list)
            {
                if (item is IValidator validator && !validator.Validate(out string errorMsg))
                {
                    success = false;
                    msg += $"\n  {errorMsg}";
                }
            }
            return success;
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _initializationTask?.TrySetCanceled();
            visitedChildPorts?.Clear();
            listViews?.Clear();
        }
    }
}
