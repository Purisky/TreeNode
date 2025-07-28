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

        // 增量渲染优化
        private Dictionary<string, PropertyElement> _propertyElementCache = new Dictionary<string, PropertyElement>();
        private bool _needsFullRefresh = false;
        private readonly object _refreshLock = new object();

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

        /// <summary>
        /// 增量刷新属性元素 - 只更新变化的部分
        /// </summary>
        public void RefreshPropertyElements()
        {
            lock (_refreshLock)
            {
                if (_needsFullRefresh)
                {
                    FullRefreshProperties();
                    _needsFullRefresh = false;
                    return;
                }

                // 增量更新：只刷新值发生变化的PropertyElement
                IncrementalRefreshProperties();
            }
        }

        /// <summary>
        /// 完整刷新属性
        /// </summary>
        private void FullRefreshProperties()
        {
            // 清除旧的属性元素
            Content.Clear();
            _propertyElementCache.Clear();
            
            // 重新绘制所有属性
            DrawPropertiesAndPorts();
        }

        /// <summary>
        /// 增量刷新属性
        /// </summary>
        private void IncrementalRefreshProperties()
        {
            try
            {
                // 遍历所有缓存的PropertyElement，检查是否需要更新
                foreach (var kvp in _propertyElementCache.ToList())
                {
                    var path = kvp.Key;
                    var propertyElement = kvp.Value;
                    
                    if (propertyElement?.parent == null)
                    {
                        // PropertyElement已被移除，从缓存中删除
                        _propertyElementCache.Remove(path);
                        continue;
                    }

                    // 获取最新值
                    try
                    {
                        var currentValue = Data.GetValue<object>(path);
                        RefreshPropertyElementValue(propertyElement, currentValue);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"刷新属性元素失败 {path}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"增量刷新失败，回退到完整刷新: {e.Message}");
                FullRefreshProperties();
            }
        }

        /// <summary>
        /// 刷新单个PropertyElement的值
        /// </summary>
        private void RefreshPropertyElementValue(PropertyElement propertyElement, object newValue)
        {
            if (propertyElement?.Drawer == null) return;

            try
            {
                // 根据不同的Drawer类型，使用不同的刷新策略
                var drawerType = propertyElement.Drawer.GetType();
                
                if (drawerType.Name.Contains("StringDrawer"))
                {
                    RefreshTextFieldValue(propertyElement, newValue?.ToString());
                }
                else if (drawerType.Name.Contains("FloatDrawer"))
                {
                    RefreshFloatFieldValue(propertyElement, Convert.ToSingle(newValue));
                }
                else if (drawerType.Name.Contains("IntDrawer"))
                {
                    RefreshIntFieldValue(propertyElement, Convert.ToInt32(newValue));
                }
                else if (drawerType.Name.Contains("BoolDrawer"))
                {
                    RefreshToggleValue(propertyElement, Convert.ToBoolean(newValue));
                }
                else if (drawerType.Name.Contains("EnumDrawer"))
                {
                    RefreshEnumFieldValue(propertyElement, newValue as Enum);
                }
                else
                {
                    // 对于复杂类型，标记需要完整刷新
                    _needsFullRefresh = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"刷新PropertyElement值失败: {e.Message}");
                _needsFullRefresh = true;
            }
        }

        private void RefreshTextFieldValue(PropertyElement propertyElement, string newValue)
        {
            var textField = propertyElement.Q<TextField>();
            if (textField != null && textField.value != newValue)
            {
                textField.SetValueWithoutNotify(newValue ?? string.Empty);
            }
        }

        private void RefreshFloatFieldValue(PropertyElement propertyElement, float newValue)
        {
            var floatField = propertyElement.Q<FloatField>();
            if (floatField != null && Math.Abs(floatField.value - newValue) > float.Epsilon)
            {
                floatField.SetValueWithoutNotify(newValue);
            }
        }

        private void RefreshIntFieldValue(PropertyElement propertyElement, int newValue)
        {
            var intField = propertyElement.Q<IntegerField>();
            if (intField != null && intField.value != newValue)
            {
                intField.SetValueWithoutNotify(newValue);
            }
        }

        private void RefreshToggleValue(PropertyElement propertyElement, bool newValue)
        {
            var toggle = propertyElement.Q<Toggle>();
            if (toggle != null && toggle.value != newValue)
            {
                toggle.SetValueWithoutNotify(newValue);
            }
        }

        private void RefreshEnumFieldValue(PropertyElement propertyElement, Enum newValue)
        {
            var enumField = propertyElement.Q<EnumField>();
            if (enumField != null && !Equals(enumField.value, newValue))
            {
                enumField.SetValueWithoutNotify(newValue);
            }
        }

        /// <summary>
        /// 标记需要完整刷新
        /// </summary>
        public void MarkForFullRefresh()
        {
            lock (_refreshLock)
            {
                _needsFullRefresh = true;
            }
        }

        /// <summary>
        /// 优化的属性缓存管理
        /// </summary>
        private void CachePropertyElement(PropertyElement propertyElement)
        {
            if (propertyElement?.LocalPath != null)
            {
                _propertyElementCache[propertyElement.LocalPath] = propertyElement;
            }
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
            // 在设置新位置之前，通知GraphView缓存当前位置
            if (View != null && !View._isRecordingMove)
            {
                View.CacheNodePositionBeforeMove(this);
            }
            
            base.SetPosition(newPos);
            
            // 只有在非录制状态下才更新Data.Position，避免重复更新
            if (!View?._isRecordingMove ?? true)
            {
                Data.Position = newPos.position;
            }
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

                // 缓存新创建的PropertyElement
                CacheNewPropertyElements(visualElement);
            }
        }

        /// <summary>
        /// 缓存新创建的PropertyElement
        /// </summary>
        private void CacheNewPropertyElements(VisualElement root)
        {
            var propertyElements = root.Query<PropertyElement>().ToList();
            foreach (var propertyElement in propertyElements)
            {
                CachePropertyElement(propertyElement);
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
            
            // 总是优先尝试同步初始化 - 这可以绕过大部分异Async问题
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
            // 优先从缓存中查找
            if (_propertyElementCache.TryGetValue(path, out var cachedElement) && 
                cachedElement?.parent != null)
            {
                return cachedElement;
            }

            // 从DOM中查找并更新缓存
            var element = this.Q<PropertyElement>(path);
            if (element != null)
            {
                _propertyElementCache[path] = element;
            }

            return element;
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
            _propertyElementCache?.Clear();
        }
    }
}
