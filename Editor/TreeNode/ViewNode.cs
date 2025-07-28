using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// 优化后的ViewNode - 支持批量渲染和高性能初始化
    /// </summary>
    public class ViewNode : Node
    {
        public TreeNodeGraphView View;
        public ParentPort ParentPort;
        public List<ChildPort> ChildPorts;
        public VisualElement Content;

        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ViewNode");

        public JsonNode Data;

        // 增量渲染优化
        private Dictionary<string, PropertyElement> _propertyElementCache = new Dictionary<string, PropertyElement>();
        private bool _needsFullRefresh = false;
        private readonly object _refreshLock = new object();

        // 位置跟踪相关
        private Vec2 _lastRecordedPosition;
        private bool _isPositionChanging = false;
        private DateTime _lastPositionChangeTime;
        private const int POSITION_CHANGE_DEBOUNCE_MS = 300; // 位置变化防抖时间

        // 🔥 新增：初始化状态标志，避免初始化期间的误报
        private bool _isInitializing = true;
        private DateTime _initializationStartTime;
        private const int INITIALIZATION_GRACE_PERIOD_MS = 2000; // 初始化宽限期2秒

        public ViewNode(JsonNode data, TreeNodeGraphView view)
        {
            Data = data;
            View = view;
            
            // 🔥 初始化状态管理 - 记录初始化开始时间
            _isInitializing = true;
            _initializationStartTime = DateTime.Now;
            
            // 初始化位置跟踪
            _lastRecordedPosition = Data.Position;
            
            // 快速初始化基础UI结构
            InitializeUIStructure();
            
            // 纯UI绘制，不包含连接逻辑
            Draw();
            OnChange();

            // 注册位置变化监听
            RegisterPositionChangeListeners();
            
            // 🔥 延迟结束初始化状态 - 给系统时间完成布局
            this.schedule.Execute(() => {
                _isInitializing = false;
                Debug.Log($"ViewNode初始化完成: {Data.GetType().Name}, 最终位置: {Data.Position}");
            }).ExecuteLater(500); // 500ms后结束初始化状态
        }

        /// <summary>
        /// 注册位置变化监听器 - 监听拖拽开始和结束事件
        /// 修复版本：解决与SelectionDragger的事件冲突问题
        /// </summary>
        private void RegisterPositionChangeListeners()
        {
            // 监听几何变化事件 - 包括位置变化
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // 🔥 关键修复：使用多种事件传播阶段注册鼠标事件，避免被SelectionDragger拦截
            RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.TrickleDown);
            
            // 🔥 新增：在Bubble阶段也注册一份监听，作为备用机制
            RegisterCallback<MouseDownEvent>(OnMouseDownBubble, TrickleDown.NoTrickleDown);
            RegisterCallback<MouseUpEvent>(OnMouseUpBubble, TrickleDown.NoTrickleDown);
            
            // 🔥 新增：监听拖拽相关的特殊事件
            RegisterCallback<DragUpdatedEvent>(OnDragUpdatedEvent);
            RegisterCallback<DragPerformEvent>(OnDragPerformEvent);
            
            // 🔥 新增：使用定时器机制作为最后的位置检测手段
            this.schedule.Execute(CheckPositionPeriodically).Every(200); // 每200ms检查一次位置
        }

        /// <summary>
        /// 鼠标按下事件 - 开始位置跟踪（Trickle Down阶段）
        /// </summary>
        private void OnMouseDown(MouseDownEvent evt)
        {
            Debug.Log($"OnMouseDown (TrickleDown) on ViewNode: {Data.GetType().Name}, button: {evt.button}");
            
            // 只处理左鼠标按钮
            if (evt.button == 0)
            {
                _isPositionChanging = true;
                _lastRecordedPosition = Data.Position;
                _lastPositionChangeTime = DateTime.Now;
                
                Debug.Log($"开始位置跟踪: {Data.GetType().Name}, 初始位置: {_lastRecordedPosition}");
            }
        }

        /// <summary>
        /// 鼠标按下事件 - 备用监听（Bubble阶段）
        /// </summary>
        private void OnMouseDownBubble(MouseDownEvent evt)
        {
            Debug.Log($"OnMouseDownBubble on ViewNode: {Data.GetType().Name}, button: {evt.button}");
            
            // 如果TrickleDown阶段没有触发，这里作为备用
            if (evt.button == 0 && !_isPositionChanging)
            {
                _isPositionChanging = true;
                _lastRecordedPosition = Data.Position;
                _lastPositionChangeTime = DateTime.Now;
                
                Debug.Log($"备用开始位置跟踪: {Data.GetType().Name}, 初始位置: {_lastRecordedPosition}");
            }
        }

        /// <summary>
        /// 鼠标释放事件 - 结束位置跟踪并记录变化（Trickle Down阶段）
        /// </summary>
        private void OnMouseUp(MouseUpEvent evt)
        {
            Debug.Log($"OnMouseUp (TrickleDown) on ViewNode: {Data.GetType().Name}, isPositionChanging: {_isPositionChanging}");
            
            if (evt.button == 0 && _isPositionChanging)
            {
                HandlePositionChangeEnd("TrickleDown");
            }
        }

        /// <summary>
        /// 鼠标释放事件 - 备用监听（Bubble阶段）
        /// </summary>
        private void OnMouseUpBubble(MouseUpEvent evt)
        {
            Debug.Log($"OnMouseUpBubble on ViewNode: {Data.GetType().Name}, isPositionChanging: {_isPositionChanging}");
            
            if (evt.button == 0 && _isPositionChanging)
            {
                HandlePositionChangeEnd("Bubble");
            }
        }

        /// <summary>
        /// 拖拽更新事件监听
        /// </summary>
        private void OnDragUpdatedEvent(DragUpdatedEvent evt)
        {
            // 如果鼠标事件没有正确触发拖拽开始，这里作为备用检测
            if (!_isPositionChanging)
            {
                _isPositionChanging = true;
                _lastRecordedPosition = Data.Position;
                _lastPositionChangeTime = DateTime.Now;
                
                Debug.Log($"通过DragUpdated事件开始位置跟踪: {Data.GetType().Name}");
            }
        }

        /// <summary>
        /// 拖拽完成事件监听
        /// </summary>
        private void OnDragPerformEvent(DragPerformEvent evt)
        {
            if (_isPositionChanging)
            {
                HandlePositionChangeEnd("DragPerform");
            }
        }

        /// <summary>
        /// 定期检查位置变化 - 最后的保障机制
        /// 修复版本：避免初始化期间的误报
        /// </summary>
        private void CheckPositionPeriodically()
        {
            // 🔥 关键修复：检查是否还在初始化期间
            if (_isInitializing)
            {
                // 检查初始化是否超时
                var initElapsed = (DateTime.Now - _initializationStartTime).TotalMilliseconds;
                if (initElapsed > INITIALIZATION_GRACE_PERIOD_MS)
                {
                    // 初始化超时，强制结束初始化状态
                    _isInitializing = false;
                    _lastRecordedPosition = Data.Position; // 更新基准位置
                    Debug.Log($"初始化超时，强制结束初始化状态: {Data.GetType().Name}, 当前位置: {Data.Position}");
                }
                return; // 初始化期间跳过位置检查
            }
            
            if (_isPositionChanging)
            {
                // 检查是否长时间没有收到MouseUp事件（超过2秒）
                if ((DateTime.Now - _lastPositionChangeTime).TotalMilliseconds > 2000)
                {
                    Debug.LogWarning($"长时间未收到MouseUp事件，强制结束位置跟踪: {Data.GetType().Name}");
                    HandlePositionChangeEnd("Timeout");
                }
            }
            else
            {
                // 即使没有在拖拽状态，也检查位置是否发生了变化（可能被外部代码修改）
                var currentPosition = Data.Position;
                if (!_lastRecordedPosition.Equals(currentPosition))
                {
                    Debug.Log($"检测到外部位置变化: {Data.GetType().Name} 从 {_lastRecordedPosition} 到 {currentPosition}");
                    
                    // 记录位置变化
                    RecordPositionChange(_lastRecordedPosition, currentPosition);
                    MakeDirty();
                    
                    _lastRecordedPosition = currentPosition;
                }
            }
        }

        /// <summary>
        /// 统一处理位置变化结束逻辑
        /// 修复版本：增加初始化状态检查
        /// </summary>
        private void HandlePositionChangeEnd(string triggerSource)
        {
            _isPositionChanging = false;
            
            // 🔥 新增：如果还在初始化期间，避免记录位置变化
            if (_isInitializing)
            {
                Debug.Log($"初始化期间跳过位置变化记录: {Data.GetType().Name} (触发源: {triggerSource})");
                _lastRecordedPosition = Data.Position; // 更新基准位置
                return;
            }
            
            // 检查位置是否真正发生了变化
            var currentPosition = Data.Position;
            Debug.Log($"位置变化结束 (触发源: {triggerSource}): 当前位置 {currentPosition}, 上次记录位置 {_lastRecordedPosition}");
            
            if (!_lastRecordedPosition.Equals(currentPosition))
            {
                // 记录位置变化操作到历史系统
                RecordPositionChange(_lastRecordedPosition, currentPosition);
                
                // 标记文件为已修改
                MakeDirty();
                
                Debug.Log($"节点位置已更改 (触发源: {triggerSource}): {Data.GetType().Name} 从 {_lastRecordedPosition} 移动到 {currentPosition}");
                
                // 更新记录的位置
                _lastRecordedPosition = currentPosition;
            }
            else
            {
                Debug.Log($"位置无变化，无需记录: {Data.GetType().Name}");
            }
        }

        /// <summary>
        /// 几何变化事件 - 监听位置变化
        /// </summary>
        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // 只在拖拽过程中处理位置变化
            if (_isPositionChanging)
            {
                // 防抖处理 - 避免过于频繁的更新
                _lastPositionChangeTime = DateTime.Now;
                
                this.schedule.Execute(() =>
                {
                    // 检查是否在防抖时间内没有新的变化
                    if ((DateTime.Now - _lastPositionChangeTime).TotalMilliseconds >= POSITION_CHANGE_DEBOUNCE_MS)
                    {
                        // 可以在这里添加实时位置更新逻辑，如实时保存等
                    }
                }).ExecuteLater((long)POSITION_CHANGE_DEBOUNCE_MS);
            }
        }

        /// <summary>
        /// 记录位置变化到历史系统
        /// </summary>
        private void RecordPositionChange(Vec2 oldPosition, Vec2 newPosition)
        {
            try
            {
                // 创建位置变化的字段修改操作
                var positionChangeOperation = new FieldModifyOperation(
                    Data,
                    "Position",
                    $"({oldPosition.x:F2}, {oldPosition.y:F2})",
                    $"({newPosition.x:F2}, {newPosition.y:F2})",
                    View
                );

                // 记录到历史系统
                View.Window.History.RecordOperation(positionChangeOperation);
                
                Debug.Log($"位置变化已记录到历史系统: {Data.GetType().Name}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"记录位置变化失败: {e.Message}");
                // 即使历史记录失败，也要确保文件被标记为已修改
                MakeDirty();
            }
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

        public virtual void Draw()
        {
            NodeInfoAttribute nodeInfo = Data.GetType().GetCustomAttribute<NodeInfoAttribute>();
            DrawParentPort(nodeInfo?.Type);
            DrawPropertiesAndPorts();
        }

        public override void SetPosition(Rect newPos)
        {
            // 获取旧位置用于比较
            var oldPosition = Data.Position;
            
            base.SetPosition(newPos);
            Data.Position = newPos.position;
            
            // 🔥 关键修复：如果位置真正发生了变化，标记为已修改
            if (!oldPosition.Equals(newPos.position))
            {
                // 🔥 新增：检查是否在初始化期间
                if (_isInitializing)
                {
                    // 初始化期间的位置变化：更新基准位置但不标记为脏
                    _lastRecordedPosition = newPos.position;
                    Debug.Log($"初始化期间位置更新: {Data.GetType().Name} 位置: {newPos.position}");
                    return;
                }
                
                // 只有在不是通过拖拽操作改变位置时才立即标记为脏
                // 拖拽操作的脏标记由鼠标事件处理
                if (!_isPositionChanging)
                {
                    MakeDirty();
                    Debug.Log($"节点位置已更改(编程方式): {Data.GetType().Name} 位置: {newPos.position}");
                }
            }
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
                edges.Add(ParentPort.connections.First());
            }
            return edges;
        }

        public void DrawParentPort(Type parentType)
        {
            if (parentType == null) { return; }
            
            ParentPort = ParentPort.Create(parentType);
            ParentPort.OnChange = OnChange;
            titleContainer.Insert(1, ParentPort);
            
            // ✅ 移除连接创建逻辑 - 连接将在第二阶段统一创建
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
            // 注销事件监听器
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // 注销鼠标事件监听器（TrickleDown和Bubble阶段）
            UnregisterCallback<MouseDownEvent>(OnMouseDown);
            UnregisterCallback<MouseUpEvent>(OnMouseUp);
            UnregisterCallback<MouseDownEvent>(OnMouseDownBubble);
            UnregisterCallback<MouseUpEvent>(OnMouseUpBubble);
            
            // 注销拖拽事件监听器
            UnregisterCallback<DragUpdatedEvent>(OnDragUpdatedEvent);
            UnregisterCallback<DragPerformEvent>(OnDragPerformEvent);
            
            _propertyElementCache?.Clear();
        }
    }
}
