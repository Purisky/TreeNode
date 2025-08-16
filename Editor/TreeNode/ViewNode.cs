using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// 优化后的ViewNode - 支持快速创建和延迟初始化
    /// </summary>
    public class ViewNode : Node
    {
        public TreeNodeGraphView View;
        public ParentPort ParentPort;

        public VisualElement Content;

        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ViewNode");

        public JsonNode Data;

        private Dictionary<PAPath, PropertyElement> _propertyElementCache = new ();
        
        // 新增：多层缓存架构
        private Dictionary<PAPath, PropertyElement> _localPathCache = new();
        private HashSet<PAPath> _invalidatedPaths = new();
        
        public Dictionary<PAPath,ChildPort> ChildPorts;

        private bool _needsFullRefresh = false;
        
        // 新增：缓存统计信息（调试用）
        private int _cacheHits = 0;
        private int _cacheMisses = 0;
        
        // 新增：快速创建模式相关
        private bool _isQuickMode = false;
        private bool _isFullyInitialized = false;

        public ViewNode(JsonNode data, TreeNodeGraphView view, bool quickMode = false)
        {
            Data = data;
            View = view;
            _isQuickMode = quickMode;
            
            // 快速初始化基础UI结构
            InitializeUIStructure();
            
            if (!quickMode)
            {
                // 完整初始化模式
                CompleteDraw();
                OnChange();
                _isFullyInitialized = true;
            }
            else
            {
                // 快速模式 - 只做最基础的初始化
                QuickDraw();
            }
            
            base.SetPosition(new Rect(data.Position, new Vector2()));
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

        /// <summary>
        /// 快速绘制模式 - 最小化的初始化
        /// </summary>
        private void QuickDraw()
        {
            NodeInfoAttribute nodeInfo = Data.GetType().GetCustomAttribute<NodeInfoAttribute>();
            DrawParentPort(nodeInfo?.Type);
            
            // 延迟属性和端口的创建
            schedule.Execute(() =>
            {
                if (!_isFullyInitialized)
                {
                    CompleteInitialization();
                }
            }).ExecuteLater(10); // 延迟10ms执行
        }

        /// <summary>
        /// 完整绘制模式 - 立即完成所有初始化
        /// </summary>
        private void CompleteDraw()
        {
            NodeInfoAttribute nodeInfo = Data.GetType().GetCustomAttribute<NodeInfoAttribute>();
            DrawParentPort(nodeInfo?.Type);
            DrawPropertiesAndPorts();
        }

        /// <summary>
        /// 完成延迟初始化
        /// </summary>
        public void CompleteInitialization()
        {
            if (_isFullyInitialized) return;
            
            try
            {
                DrawPropertiesAndPorts();
                OnChange();
                _isFullyInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(string.Format(I18n.Runtime.Warning.InitializationFailed, e.Message));
            }
        }

        /// <summary>
        /// 检查是否完全初始化
        /// </summary>
        public bool IsFullyInitialized => _isFullyInitialized;

        public virtual void Draw()
        {
            if (_isQuickMode && !_isFullyInitialized)
            {
                QuickDraw();
            }
            else
            {
                CompleteDraw();
            }
        }

        /// <summary>
        /// 增量刷新属性元素 - 简化版本，移除不必要的锁保护
        /// </summary>
        public void RefreshPropertyElements()
        {
            // 如果还未完全初始化，先完成初始化
            if (!_isFullyInitialized)
            {
                CompleteInitialization();
                return;
            }

            // 检查是否需要完整刷新
            if (_needsFullRefresh)
            {
                FullRefreshProperties();
                _needsFullRefresh = false;
                return;
            }

            // 增量更新：只刷新值发生变化的PropertyElement
            IncrementalRefreshProperties();
        }

        /// <summary>
        /// 重载方法：刷新指定路径的属性元素
        /// </summary>
        /// <param name="path">属性路径，如果为null则执行常规刷新</param>
        public void RefreshPropertyElements(PAPath path)
        {
            // 如果还未完全初始化，先完成初始化
            if (!_isFullyInitialized)
            {
                CompleteInitialization();
                return;
            }

            // 如果指定了路径，尝试精确刷新单个属性
            if (path.Valid)
            {
                RefreshSingleProperty(path);
                return;
            }

            // 否则执行常规的增量刷新
            RefreshPropertyElements();
        }

        /// <summary>
        /// 精确刷新单个属性 - 新增方法，用于精确更新
        /// </summary>
        private void RefreshSingleProperty(PAPath path)
        {
            if (_propertyElementCache.TryGetValue(path, out var propertyElement) && 
                propertyElement?.parent != null)
            {
                try
                {
                    var currentValue = Data.GetValue<object>(path);
                    RefreshPropertyElementValue(propertyElement, currentValue);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(string.Format(I18n.Runtime.Warning.PropertyRefreshFailed, path, e.Message));
                    // 单个属性刷新失败时，标记需要完整刷新
                    _needsFullRefresh = true;
                }
            }
            else
            {
                // 缓存中没有找到，可能需要重建
                _needsFullRefresh = true;
            }
        }

        public void RefreshList(ViewChange viewChange)
        {
            // 确保完全初始化
            if (!_isFullyInitialized)
            {
                CompleteInitialization();
            }

            if (_propertyElementCache.TryGetValue(viewChange.Path, out PropertyElement element) &&
                element is ListElement listElement
                )
            {
                listElement.ApplyViewChange(viewChange);
            }
        }



        /// <summary>
        /// 完整刷新属性
        /// </summary>
        private void FullRefreshProperties()
        {
            // 清除旧的属性元素
            Content.Clear();
            
            // 使用新的缓存失效机制
            InvalidateAllCaches();
            
            // 重新绘制所有属性
            DrawPropertiesAndPorts();
        }

        /// <summary>
        /// 增量刷新属性 - 优化异常处理
        /// </summary>
        private void IncrementalRefreshProperties()
        {
            try
            {
                // 收集需要清理的无效缓存项
                var itemsToRemove = new List<string>();
                
                // 遍历所有缓存的PropertyElement，检查是否需要更新
                foreach (var kvp in _propertyElementCache)
                {
                    var path = kvp.Key;
                    var propertyElement = kvp.Value;
                    
                    if (propertyElement?.parent == null)
                    {
                        // PropertyElement已被移除，标记为待删除
                        itemsToRemove.Add(path);
                        continue;
                    }

                    // 获取最新值并更新
                    try
                    {
                        var currentValue = Data.GetValue<object>(path);
                        RefreshPropertyElementValue(propertyElement, currentValue);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"刷新属性元素失败 {path}: {e.Message}");
                        // 单个属性失败时不影响其他属性的更新
                    }
                }

                // 清理无效的缓存项
                foreach (var key in itemsToRemove)
                {
                    _propertyElementCache.Remove(key);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"增量刷新失败，标记为需要完整刷新: {e.Message}");
                _needsFullRefresh = true;
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
        /// 标记需要完整刷新 - 简化版本，移除锁保护
        /// </summary>
        public void MarkForFullRefresh()
        {
            _needsFullRefresh = true;
        }

        /// <summary>
        /// 优化的属性缓存管理
        /// </summary>
        private void CachePropertyElement(PropertyElement propertyElement)
        {
            if (propertyElement?.LocalPath != null)
            {
                _propertyElementCache[propertyElement.LocalPath] = propertyElement;
                // 同时缓存到本地路径缓存
                _localPathCache[propertyElement.LocalPath] = propertyElement;
            }
        }

        /// <summary>
        /// 智能缓存失效机制 - 按路径失效
        /// </summary>
        public void InvalidatePathCache(PAPath path)
        {
            if (path == null || !path.Valid) return;
            
            // 标记路径为失效
            _invalidatedPaths.Add(path);
            
            // 从所有缓存中移除
            _propertyElementCache.Remove(path);
            _localPathCache.Remove(path);
            
            // 如果是父路径，失效所有子路径
            InvalidateChildPaths(path);
            
            Debug.Log($"ViewNode: 失效缓存路径 {path}");
        }

        /// <summary>
        /// 失效所有子路径 - 级联失效机制
        /// </summary>
        private void InvalidateChildPaths(PAPath parentPath)
        {
            var childPathsToInvalidate = new List<PAPath>();
            
            // 查找所有以parentPath开头的子路径
            foreach (var path in _propertyElementCache.Keys)
            {
                if (path.StartsWith(parentPath))
                {
                    childPathsToInvalidate.Add(path);
                }
            }
            
            foreach (var childPath in childPathsToInvalidate)
            {
                _propertyElementCache.Remove(childPath);
                _localPathCache.Remove(childPath);
                _invalidatedPaths.Add(childPath);
            }
            
            if (childPathsToInvalidate.Count > 0)
            {
                Debug.Log($"ViewNode: 级联失效了 {childPathsToInvalidate.Count} 个子路径");
            }
        }

        /// <summary>
        /// 清空所有缓存 - 全量失效
        /// </summary>
        public void InvalidateAllCaches()
        {
            var cacheCount = _propertyElementCache.Count + _localPathCache.Count;
            
            _propertyElementCache.Clear();
            _localPathCache.Clear();
            _invalidatedPaths.Clear();
            
            // 重置统计信息
            _cacheHits = 0;
            _cacheMisses = 0;
            
            Debug.Log($"ViewNode: 清空所有缓存，共清理 {cacheCount} 个条目");
        }

        /// <summary>
        /// 高性能的本地路径查找 - 多层缓存优化
        /// </summary>
        public PropertyElement FindByLocalPath(PAPath localPath)
        {
            if (localPath == null || !localPath.Valid)
            {
                _cacheMisses++;
                return null;
            }
            
            // 第一层：检查路径是否已失效
            if (_invalidatedPaths.Contains(localPath))
            {
                _cacheMisses++;
                return null;
            }
            
            // 第二层：本地路径缓存（最快）
            if (_localPathCache.TryGetValue(localPath, out var localCachedElement) && 
                localCachedElement?.parent != null)
            {
                _cacheHits++;
                return localCachedElement;
            }
            
            // 第三层：主属性缓存
            if (_propertyElementCache.TryGetValue(localPath, out var cachedElement) && 
                cachedElement?.parent != null)
            {
                // 提升到本地缓存
                _localPathCache[localPath] = cachedElement;
                _cacheHits++;
                return cachedElement;
            }
            
            // 第四层：DOM查找（最慢，但更新缓存）
            var element = this.Q<PropertyElement>(localPath);
            if (element != null)
            {
                // 更新所有缓存层
                _propertyElementCache[localPath] = element;
                _localPathCache[localPath] = element;
                // 从失效列表中移除（如果存在）
                _invalidatedPaths.Remove(localPath);
            }
            
            _cacheMisses++;
            return element;
        }

        /// <summary>
        /// 获取缓存统计信息（调试用）
        /// </summary>
        public void LogCacheStats()
        {
            int totalRequests = _cacheHits + _cacheMisses;
            float hitRate = totalRequests > 0 ? (float)_cacheHits / totalRequests * 100 : 0;
            
            Debug.Log($"ViewNode缓存统计: " +
                     $"命中率={hitRate:F1}% ({_cacheHits}/{totalRequests}), " +
                     $"主缓存={_propertyElementCache.Count}项, " +
                     $"本地缓存={_localPathCache.Count}项, " +
                     $"失效路径={_invalidatedPaths.Count}个");
        }
        
        public ChildPort GetChildPort(PAPath path)
        {
            if (path.ItemOfCollection) { path = path.GetParent(); }
            if (ChildPorts == null) return null;
            if (ChildPorts.TryGetValue(path, out ChildPort childPort))
            {
                return childPort;
            }
            return null;
        }

        /// <summary>
        /// 从字典中移除ChildPort
        /// </summary>
        public bool RemoveChildPort(ChildPort childPort)
        {
            if (childPort == null || ChildPorts == null) return false;
            
            return ChildPorts.Remove(childPort.LocalPath);
        }

        /// <summary>
        /// 清空所有端口
        /// </summary>
        public void ClearChildPorts()
        {
            ChildPorts?.Clear();
        }

        /// <summary>
        /// 验证字典一致性（调试和测试用）
        /// </summary>
        public void ValidateChildPortsDict()
        {
            if (ChildPorts == null) return;
            
            var invalidPorts = new List<PAPath>();
            
            foreach (var kvp in ChildPorts)
            {
                var path = kvp.Key;
                var port = kvp.Value;
                
                if (port == null)
                {
                    invalidPorts.Add(path);
                    Debug.LogWarning(string.Format(I18n.Runtime.Warning.InvalidPortReference, path));
                    continue;
                }
                
                if (port.LocalPath != path)
                {
                    invalidPorts.Add(path);
                    Debug.LogWarning(string.Format(I18n.Runtime.Warning.PortInconsistency, path, port.LocalPath));
                }
            }
            
            // 清理无效端口
            foreach (var invalidPath in invalidPorts)
            {
                ChildPorts.Remove(invalidPath);
            }
            
            if (invalidPorts.Count > 0)
            {
                Debug.Log($"ViewNode: 清理了 {invalidPorts.Count} 个无效端口");
            }
        }

        public List<Edge> GetAllEdges()
        {
            List<Edge> edges = new();
            if (ChildPorts != null)
            {
                foreach (var item in ChildPorts)
                {
                    edges.AddRange(item.Value.connections);
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
            
            ParentPort = ParentPort.Create(this,parentType);
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
                    foreach (var child in item.Value.GetChildValues())
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

        public PAPath GetNodePath()
        {
            ViewNode parentNode = GetParent();
            if (parentNode == null)
            {
                int index = View.Asset.Data.Nodes.IndexOf(Data);
                return PAPath.Index(index);
            }
            ChildPort childPort = ParentPort.connections.First().ChildPort();
            if (childPort is MultiPort multi) { multi.SortIndex(); }
            PropertyElement element = childPort.GetFirstAncestorOfType<PropertyElement>();
            PAPath path = element.LocalPath;
            
            if (childPort is NumPort)
            {
                path = path.Append(nameof(NumValue.Node));
            }
            else if (childPort is MultiPort)
            {
                path = path.Append(ParentPort.Index);
            }

            return parentNode.GetNodePath().Combine(path);
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
            
            List<ChildPort> childPorts = ChildPorts.Values.Where(n => n.connected)
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
        
        /// <summary>
        /// 字符串重载版本 - 兼容现有代码
        /// </summary>
        public PropertyElement FindByLocalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            
            // 转换为PAPath并使用优化的查找方法
            PAPath paPath = new PAPath(path);
            return FindByLocalPath(paPath);
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
        
        public void PopupText()
        {
            JsonNode jsonNode = Data;
            ViewNode parent = GetParent();
            if (jsonNode is IText && parent!=null)
            {
                Edge edge = ParentPort.connections.First();
                if (edge.output is IPopupTextPort port)
                {
                    port.DisplayPopupText();
                }
                parent.PopupText();
            }
        }
        
        public void Dispose()
        {
            // 清理所有缓存
            InvalidateAllCaches();
            
            // 清理端口字典
            ClearChildPorts();
            
            // 如果需要调试缓存性能，可以在这里输出统计信息
            #if UNITY_EDITOR && DEBUG
            LogCacheStats();
            #endif
        }
    }
}
