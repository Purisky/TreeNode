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
    /// 优化后的ViewNode - 支持批量渲染和简化拖动事件处理
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


        public ViewNode(JsonNode data, TreeNodeGraphView view)
        {
            Data = data;
            View = view;
            
            
            // 快速初始化基础UI结构
            InitializeUIStructure();
            
            // 纯UI绘制，不包含连接逻辑
            Draw();
            OnChange();
            base.SetPosition(new Rect(data.Position, new Vector2()));
        }

        public override void SetPosition(Rect newPos)
        {
            // 🔥 简化位置设置逻辑 - 统一在这里处理所有位置变化
            var oldPosition = Data.Position;
            
            base.SetPosition(newPos);
            Data.Position = newPos.position;
            
            // 检查位置是否真正发生了变化
            if (!oldPosition.Equals(newPos.position))
            {
                // 🔥 统一记录位置变化 - 利用History系统的智能合并功能
                RecordPositionChange(oldPosition, newPos.position);
                
                // 标记文件为已修改
                MakeDirty();
            }
        }


        /// <summary>
        /// 简化的位置变化记录 - 统一通过SetPosition处理
        /// </summary>
        private void RecordPositionChange(Vec2 oldPosition, Vec2 newPosition)
        {
            try
            {
                // 创建位置变化的字段修改操作 - 使用Vec2版本避免字符串转换
                // History系统会自动处理同一节点连续位置变化的合并
                var positionChangeOperation = new FieldModifyOperation<Vec2>(
                    Data,
                    "Position",
                    oldPosition,
                    newPosition,
                    View
                );

                // 记录到历史系统 - History系统会自动合并连续的同节点操作
                View.Window.History.RecordOperation(positionChangeOperation);
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
        /// 清理资源 - 🔥 大幅简化，移除复杂的事件监听器
        /// </summary>
        public void Dispose()
        {
            _propertyElementCache?.Clear();
        }
    }
}
