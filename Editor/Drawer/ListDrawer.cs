using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class ListDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(List<>);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            Type gType = memberMeta.Type.GetGenericArguments()[0];
            DropdownAttribute dropdownAttribute = memberMeta.Dropdown;
            BaseDrawer itemDrawer = dropdownAttribute != null ? DrawerManager.GetDropdownDrawer(gType) : DrawerManager.Get(gType);
            bool hasPort = itemDrawer is ComplexDrawer complex && complex.HasPort;

            // 创建ListElement而非ListView
            ListElement listElement = new ListElement(memberMeta, node, path, this, hasPort);
            
            // 配置数据绑定
            IList list = node.Data.GetValue<IList>(path);
            listElement.ItemsSource = list;
            
            // 配置项目创建委托
            bool dirty = memberMeta.Json;
            object parent = node.Data.GetParent(path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            
            listElement.MakeItem = () => new ListItem(listElement, memberMeta, node, itemDrawer, path, dirty, action);
            listElement.BindItem = (element, index) => ((ListItem)element).InitValue(index);
            
            // 配置添加按钮事件
            listElement.AddButton.clicked += () =>
            {
                if (listElement.ItemsSource == null)
                {
                    IList newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(gType));
                    node.Data.SetValue(path, newList);
                    listElement.ItemsSource = newList;
                }
                
                if (gType.GetConstructor(Type.EmptyTypes) == null)
                {
                    listElement.ItemsSource.Add(RuntimeHelpers.GetUninitializedObject(gType));
                }
                else
                {
                    listElement.ItemsSource.Add(Activator.CreateInstance(gType));
                }
                
                if (dirty)
                {
                    listElement.SetDirty();
                }
                action?.Invoke();
                listElement.RefreshItems();
                listElement.Focus();
            };
            
            // 设置启用状态
            listElement.SetEnabled(!memberMeta.ShowInNode.ReadOnly);
            
            // 立即同步渲染
            listElement.RefreshItems();
            
            return listElement;
        }
    }

    /// <summary>
    /// ListElement - 继承自PropertyElement的高性能列表组件
    /// 完全替代ListView，实现同步初始化和智能折叠控制
    /// </summary>
    public class ListElement : PropertyElement
    {
        #region 私有字段
        private bool _isRefreshing = false;
        private readonly object _refreshLock = new object();
        #endregion
        
        #region 数据源和配置
        public IList ItemsSource { get; set; }
        public bool HasPort { get; private set; }
        public bool ShowBorder { get; set; } = true;
        public bool ShowAlternatingRowBackgrounds { get; set; } = true;
        public float FixedItemHeight { get; set; } = -1;
        #endregion
        
        #region 委托
        public Func<VisualElement> MakeItem { get; set; }
        public Action<VisualElement, int> BindItem { get; set; }
        #endregion
        
        #region UI组件
        public VisualElement HeaderContainer { get; private set; }
        public Foldout FoldoutHeader { get; private set; }  // 仅HasPort=false时使用
        public Label SimpleHeader { get; private set; }     // 仅HasPort=true时使用
        public Label SizeLabel { get; private set; }
        public Button AddButton { get; private set; }
        public VisualElement ItemContainer { get; private set; }
        #endregion
        
        public ListElement(MemberMeta memberMeta, ViewNode node, PAPath path, BaseDrawer drawer, bool hasPort)
            : base(memberMeta, node, path, drawer, null)
        {
            HasPort = hasPort;
            InitializeUI();
        }
        
        #region UI初始化
        private void InitializeUI()
        {
            // 批量设置基础样式
            ApplyBaseStyles();
            
            // 创建UI结构
            CreateHeader();
            CreateItemContainer();
            
            // 延迟应用高级样式，避免初始化时的重排
            schedule.Execute(ApplyAdvancedStyles).ExecuteLater(1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyBaseStyles()
        {
            style.flexGrow = 1;
            AddToClassList("unity-list-view"); // 复用ListView的CSS类名确保兼容性
            
            if (ShowBorder)
            {
                AddToClassList("unity-list-view--with-border");
            }
        }
        
        private void ApplyAdvancedStyles()
        {
            // 应用高级样式和性能优化
            if (ShowAlternatingRowBackgrounds)
            {
                AddToClassList("unity-list-view--show-alternating-row-backgrounds");
            }
            
            // 设置容器样式优化
            if (ItemContainer != null)
            {
                ItemContainer.style.overflow = Overflow.Hidden; // 性能优化
                // 移除了不兼容的 enableViewDataPersistence 属性
            }
        }
        #endregion
        
        #region 头部创建
        private void CreateHeader()
        {
            HeaderContainer = new VisualElement();
            HeaderContainer.name = "unity-list-view__header";
            HeaderContainer.AddToClassList("unity-list-view__header");
            
            // 批量设置头部样式，减少重排
            var headerStyle = HeaderContainer.style;
            headerStyle.flexDirection = FlexDirection.Row;
            headerStyle.alignItems = Align.Center;
            headerStyle.minHeight = 22;
            
            if (HasPort)
            {
                CreateSimpleHeader();
            }
            else
            {
                CreateFoldoutHeader();
            }
            
            // 创建添加按钮
            AddButton = CreateAddButton();
            HeaderContainer.Add(AddButton);
            
            Add(HeaderContainer);
        }
        
        private void CreateSimpleHeader()
        {
            VisualElement headerContent = new VisualElement();
            var contentStyle = headerContent.style;
            contentStyle.flexGrow = 1;
            contentStyle.flexDirection = FlexDirection.Row;
            contentStyle.alignItems = Align.Center;
            contentStyle.height = 22;
            contentStyle.paddingLeft = 4;
            contentStyle.paddingTop = 4;
            
            SimpleHeader = BaseDrawer.CreateLabel(MemberMeta.LabelInfo);
            SizeLabel = CreateSizeLabel();
            
            headerContent.Add(SimpleHeader);
            headerContent.Add(SizeLabel);
            HeaderContainer.Add(headerContent);
        }
        
        private void CreateFoldoutHeader()
        {
            FoldoutHeader = new Foldout();
            FoldoutHeader.name = "unity-list-view__foldout-header";
            FoldoutHeader.AddToClassList("unity-list-view__foldout-header");
            FoldoutHeader.text = MemberMeta.LabelInfo?.Text ?? "List";
            FoldoutHeader.value = true; // 默认展开
            
            SizeLabel = CreateSizeLabel();
            
            // 优化：将大小标签添加到Foldout的toggle中
            var toggle = FoldoutHeader.Q<Toggle>();
            if (toggle != null)
            {
                var labelContainer = toggle.Q(className: "unity-toggle__text");
                if (labelContainer != null)
                {
                    labelContainer.Add(SizeLabel);
                }
                else
                {
                    toggle.Add(SizeLabel);
                }
            }
            
            // 高效的折叠状态变化监听
            FoldoutHeader.RegisterValueChangedCallback(OnFoldoutChanged);
            
            HeaderContainer.Add(FoldoutHeader);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Label CreateSizeLabel()
        {
            var sizeLabel = new Label();
            var style = sizeLabel.style;
            style.marginLeft = 8;
            style.fontSize = 11;
            style.color = new Color(0.7f, 0.7f, 0.7f, 1f); // 优化颜色值
            style.unityFontStyleAndWeight = FontStyle.Normal;
            return sizeLabel;
        }
        
        private void OnFoldoutChanged(ChangeEvent<bool> evt)
        {
            // 高性能的显示/隐藏切换
            ItemContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
        }
        #endregion
        
        #region 容器创建
        private void CreateItemContainer()
        {
            ItemContainer = new VisualElement();
            ItemContainer.name = "unity-list-view__item-container";
            ItemContainer.AddToClassList("unity-list-view__item-container");
            
            // 批量设置容器样式
            var containerStyle = ItemContainer.style;
            containerStyle.flexGrow = 1;
            containerStyle.overflow = Overflow.Hidden; // 性能优化
            
            Add(ItemContainer);
        }
        
        private Button CreateAddButton()
        {
            Button addBtn = new Button() { name = "addbtn", text = "+" };
            
            // 批量设置按钮样式
            var btnStyle = addBtn.style;
            btnStyle.height = 17;
            btnStyle.width = 17;
            btnStyle.marginBottom = 0;
            btnStyle.marginTop = 0;
            btnStyle.paddingBottom = 4;
            btnStyle.fontSize = 18;
            btnStyle.borderTopLeftRadius = 2;
            btnStyle.borderTopRightRadius = 2;
            btnStyle.borderBottomLeftRadius = 2;
            btnStyle.borderBottomRightRadius = 2;
            
            return addBtn;
        }
        #endregion
        
        #region 核心刷新方法 - 高性能版本
        /// <summary>
        /// 同步重建所有项目 - 高性能版本，批量DOM操作
        /// </summary>
        public void RefreshItems()
        {
            // 防止并发刷新
            lock (_refreshLock)
            {
                if (_isRefreshing) return;
                _isRefreshing = true;
            }
            
            try
            {
                // 批量清理现有项目
                BatchCleanupItems();
                
                // 批量创建新项目
                if (ItemsSource != null && ItemsSource.Count > 0)
                {
                    BatchCreateItems();
                }
                
                // 批量更新UI状态
                BatchUpdateUI();
            }
            finally
            {
                _isRefreshing = false;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BatchCleanupItems()
        {
            var children = ItemContainer.Children().ToList();
            
            // 批量清理ChildPort连接
            foreach (var child in children)
            {
                if (child is ListItem item)
                {
                    item.Cleanup();
                }
            }
            
            // 一次性清空容器
            ItemContainer.Clear();
        }
        
        private void BatchCreateItems()
        {
            var itemCount = ItemsSource.Count;
            var newItems = new List<VisualElement>(itemCount);
            
            // 批量创建项目，减少DOM操作次数
            for (int i = 0; i < itemCount; i++)
            {
                var listItem = MakeItem?.Invoke();
                if (listItem != null)
                {
                    BindItem?.Invoke(listItem, i);
                    newItems.Add(listItem);
                    
                    // 立即处理端口连接，无延迟
                    if (listItem is ListItem item && item.HasPort)
                    {
                        item.ProcessChildPorts();
                    }
                }
            }
            
            // 批量添加到容器，减少重排次数
            foreach (var item in newItems)
            {
                ItemContainer.Add(item);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BatchUpdateUI()
        {
            UpdateSizeDisplay();
            
            // 延迟更新交替背景，避免阻塞
            if (ShowAlternatingRowBackgrounds)
            {
                schedule.Execute(UpdateAlternatingBackgrounds).ExecuteLater(1);
            }
        }
        #endregion
        
        #region UI更新方法
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateSizeDisplay()
        {
            if (SizeLabel != null)
            {
                int count = ItemsSource?.Count ?? 0;
                SizeLabel.text = $"({count})";
            }
        }
        
        private void UpdateAlternatingBackgrounds()
        {
            if (!ShowAlternatingRowBackgrounds) return;
            
            var childCount = ItemContainer.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = ItemContainer.ElementAt(i);
                if (i % 2 == 0)
                {
                    child.RemoveFromClassList("unity-list-view__item--alternative-background");
                }
                else
                {
                    child.AddToClassList("unity-list-view__item--alternative-background");
                }
            }
        }
        #endregion
        
        #region 项目操作方法 - 优化版本
        /// <summary>
        /// 删除指定索引的项目 - 高性能版本
        /// </summary>
        public void RemoveItem(int index)
        {
            if (index < 0 || index >= (ItemsSource?.Count ?? 0)) return;
            
            // 同步操作数据源
            ItemsSource.RemoveAt(index);
            
            // 高效的UI更新
            if (index < ItemContainer.childCount)
            {
                var itemToRemove = ItemContainer.ElementAt(index);
                if (itemToRemove is ListItem listItem)
                {
                    listItem.Cleanup();
                }
                ItemContainer.RemoveAt(index);
                
                // 批量更新后续项目的索引
                BatchUpdateIndicesFrom(index);
            }
            
            BatchUpdateUI();
        }
        
        /// <summary>
        /// 移动项目 - 高性能版本
        /// </summary>
        public void MoveItem(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || 
                fromIndex >= (ItemsSource?.Count ?? 0) || 
                toIndex >= (ItemsSource?.Count ?? 0) ||
                fromIndex == toIndex) return;
                
            // 同步操作数据源
            var item = ItemsSource[fromIndex];
            ItemsSource.RemoveAt(fromIndex);
            ItemsSource.Insert(toIndex, item);
            
            // 高效的UI更新
            if (fromIndex < ItemContainer.childCount && toIndex < ItemContainer.childCount)
            {
                var visualItem = ItemContainer.ElementAt(fromIndex);
                ItemContainer.RemoveAt(fromIndex);
                ItemContainer.Insert(toIndex, visualItem);
                
                // 批量更新索引范围
                BatchUpdateIndicesInRange(fromIndex, toIndex);
            }
            
            // 延迟更新交替背景
            schedule.Execute(UpdateAlternatingBackgrounds).ExecuteLater(1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BatchUpdateIndicesFrom(int startIndex)
        {
            var childCount = ItemContainer.childCount;
            for (int i = startIndex; i < childCount; i++)
            {
                if (ItemContainer.ElementAt(i) is ListItem item)
                {
                    item.UpdateIndex(i);
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BatchUpdateIndicesInRange(int fromIndex, int toIndex)
        {
            int start = Math.Min(fromIndex, toIndex);
            int end = Math.Max(fromIndex, toIndex);
            var childCount = ItemContainer.childCount;
            
            for (int i = start; i <= end && i < childCount; i++)
            {
                if (ItemContainer.ElementAt(i) is ListItem listItem)
                {
                    listItem.UpdateIndex(i);
                }
            }
        }
        #endregion
        
        #region 公共接口
        /// <summary>
        /// 设置启用状态
        /// </summary>
        public new void SetEnabled(bool enabled)
        {
            base.SetEnabled(enabled);
            if (AddButton != null)
            {
                AddButton.SetEnabled(enabled);
            }
        }
        
        /// <summary>
        /// 设置焦点
        /// </summary>
        public new void Focus()
        {
            base.Focus();
        }
        #endregion
    }

    /// <summary>
    /// ListItem - 完全重写的高性能列表项组件
    /// 专为ListElement优化，实现同步初始化和立即可用的ChildPort
    /// </summary>
    public class ListItem : VisualElement
    {
        #region 静态资源
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ListItem");
        #endregion

        #region 私有字段
        private ListElement _parentList;
        private int _currentIndex;
        private PropertyElement _contentElement;
        private bool _isInitialized;
        private bool _hasProcessedPorts;
        #endregion

        #region 公共属性
        public int Index => IndexField?.value ?? _currentIndex;
        public Label IndexLabel { get; private set; }
        public VisualElement IndexElement { get; private set; }
        public IntegerField IndexField { get; private set; }
        public PropertyElement Value => _contentElement;
        public ViewNode ViewNode { get; private set; }
        public string Path { get; private set; }
        public MemberMeta Meta { get; private set; }
        public BaseDrawer Drawer { get; private set; }
        public bool HasPort { get; private set; }
        public List<ChildPort> ChildPorts { get; private set; }
        #endregion

        #region 事件和委托
        private Action OnChange;
        private Action Action;
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数 - 接收ListElement父级引用，实现直接交互
        /// </summary>
        public ListItem(ListElement parentList, MemberMeta meta, ViewNode node, BaseDrawer baseDrawer, string path, bool dirty, Action action)
        {
            // 保存父级引用
            _parentList = parentList;
            
            // 初始化基本属性
            Action = action;
            Meta = meta;
            ViewNode = node;
            Path = path;
            Drawer = baseDrawer;
            ChildPorts = new List<ChildPort>();
            
            // 初始化OnChange委托
            OnChange = () =>
            {
                if (dirty)
                {
                    this.SetDirty();
                }
                Action?.Invoke();
                ViewNode.PopupText();
            };
            
            // 检查是否有Port
            if (Drawer is ComplexDrawer complex)
            {
                HasPort = complex.HasPort;
            }
            
            // 高性能UI初始化
            InitializeUIOptimized();
        }
        #endregion

        #region 高性能UI初始化
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeUIOptimized()
        {
            // 应用样式
            styleSheets.Add(StyleSheet);
            
            // 批量设置基础样式
            var style = this.style;
            style.height = Length.Auto();
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;
            
            // 添加ListView兼容的CSS类
            AddToClassList("unity-list-view__item");
            
            // 创建索引显示区域
            CreateIndexElementOptimized();
            
            // 注册事件
            RegisterEvents();
            
            _isInitialized = true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateIndexElementOptimized()
        {
            IndexElement = new VisualElement { name = "listitemindex" };
            
            // 批量设置索引元素样式
            var indexStyle = IndexElement.style;
            indexStyle.position = Position.Absolute;
            indexStyle.left = 0;
            indexStyle.height = Length.Percent(100);
            indexStyle.width = 40;
            indexStyle.borderRightWidth = 1;
            indexStyle.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            indexStyle.flexDirection = FlexDirection.Row;
            IndexElement.pickingMode = PickingMode.Ignore;
            
            IndexField = new IntegerField { name = "listitemindexfield" };
            IndexLabel = new Label { name = "listitemindexlabel", pickingMode = PickingMode.Ignore };
            
            // 优化标签和输入框样式
            var labelStyle = IndexLabel.style;
            labelStyle.flexGrow = 1;
            labelStyle.unityTextAlign = TextAnchor.MiddleCenter;
            labelStyle.display = DisplayStyle.Flex;
            
            var fieldStyle = IndexField.style;
            fieldStyle.flexGrow = 1;
            fieldStyle.display = DisplayStyle.None;
            fieldStyle.unityTextAlign = TextAnchor.MiddleCenter;
            
            IndexElement.Add(IndexLabel);
            IndexElement.Add(IndexField);
            Add(IndexElement);
        }
        
        private void RegisterEvents()
        {
            // 注册右键菜单
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            
            // 注册索引变更事件
            IndexField?.RegisterValueChangedCallback(OnIndexChanged);
            IndexField?.RegisterCallback<BlurEvent>(OnBlur);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 1) // 右键
            {
                GenericMenu menu = new GenericMenu();
                AddItemsToMenu(menu);
                menu.ShowAsContext();
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnBlur(BlurEvent evt) => CommitEdit();
        #endregion

        #region 核心方法 - 高性能版本
        /// <summary>
        /// 初始化值 - 同步版本，立即可用
        /// </summary>
        public void InitValue(int index)
        {
            if (!_isInitialized) return;
            
            _currentIndex = index;
            
            // 批量更新索引显示
            if (IndexLabel != null) IndexLabel.text = index.ToString();
            if (IndexField != null) IndexField.value = index;

            string propertyPath = $"{Path}[{index}]";

            // 高效的内容清理和重建
            CleanupContent();
            CreateContent(propertyPath);

            // 立即处理ChildPort连接
            if (HasPort && !_hasProcessedPorts)
            {
                ProcessChildPorts();
                _hasProcessedPorts = true;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CleanupContent()
        {
            if (_contentElement != null)
            {
                RemoveEdges();
                Remove(_contentElement);
                _contentElement = null;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateContent(string propertyPath)
        {
            // 同步创建新内容
            _contentElement = Drawer.Create(Meta, ViewNode, propertyPath, Action);
            
            // 批量设置内容样式
            var contentStyle = _contentElement.style;
            contentStyle.flexGrow = 1;
            contentStyle.paddingLeft = 40;
            
            Insert(0, _contentElement);
        }
        
        /// <summary>
        /// 立即处理ChildPort连接 - 同步版本，无延迟
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessChildPorts()
        {
            if (!HasPort) return;
            
            // 立即查询ChildPort，无需schedule
            ChildPorts.Clear();
            ChildPorts.AddRange(this.Query<ChildPort>().ToList());
            
            // 立即添加边连接
            AddEdges();
        }
        
        /// <summary>
        /// 清理资源 - 高效版本
        /// </summary>
        public void Cleanup()
        {
            RemoveEdges();
            ChildPorts.Clear();
            _hasProcessedPorts = false;
            
            CleanupContent();
        }
        
        /// <summary>
        /// 更新索引显示 - 高性能版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateIndex(int newIndex)
        {
            _currentIndex = newIndex;
            if (IndexLabel != null) IndexLabel.text = newIndex.ToString();
            if (IndexField != null) IndexField.value = newIndex;
        }
        #endregion

        #region 端口管理 - 优化版本
        private void RemoveEdges()
        {
            if (ChildPorts.Count == 0) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (childPort.connected)
                {
                    var edges = childPort.connections.ToList();
                    foreach (var edge in edges)
                    {
                        edge.ParentPort().DisconnectAll();
                        ViewNode.View.RemoveElement(edge);
                    }
                    childPort.DisconnectAll();
                }
                ViewNode.ChildPorts.Remove(childPort);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddEdges()
        {
            if (ChildPorts.Count == 0) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (!ViewNode.ChildPorts.Contains(childPort))
                {
                    ViewNode.ChildPorts.Add(childPort);
                }
            }
        }
        #endregion

        #region 事件处理 - 优化版本
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnIndexChanged(ChangeEvent<int> evt)
        {
            int index = evt.newValue;
            if (index < 0)
            {
                IndexField.value = 0;
                return;
            }
            
            var maxIndex = (_parentList?.ItemsSource?.Count ?? 1) - 1;
            if (index > maxIndex)
            {
                IndexField.value = Math.Max(0, maxIndex);
                return;
            }
        }
        
        private void CommitEdit()
        {
            var index = IndexField?.value ?? _currentIndex;
            int oldIndex = parent.IndexOf(this);
            if (index == oldIndex)
            {
                EditMode(false);
                return;
            }
            
            _parentList?.MoveItem(oldIndex, index);
            OnChange?.Invoke();
        }
        
        public void EditMode(bool edit)
        {
            if (edit)
            {
                AddToClassList("editmode");
                // 使用更短的延迟提高响应性
                IndexField?.schedule.Execute(() =>
                {
                    IndexField.Focus();
                    IndexField.SelectAll();
                }).ExecuteLater(25);
            }
            else
            {
                RemoveFromClassList("editmode");
            }
        }
        #endregion

        #region 操作方法 - 优化版本
        public void RemoveSelf()
        {
            int index = parent.IndexOf(this);
            
            // 高效的批量Edge清理
            var allListItems = parent.Children().OfType<ListItem>().ToList();
            foreach (var listItem in allListItems)
            {
                listItem.RemoveEdges();
            }
            
            // 将ChildPort的子值添加回节点数据
            ViewNode.View.Asset.Data.Nodes.AddRange(ChildPorts.SelectMany(n => n.GetChildValues()));
            
            // 使用新的ListElement删除方法
            _parentList?.RemoveItem(index);
            _parentList?.Focus();
            OnChange?.Invoke();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            int index = parent.IndexOf(this);
            bool showMoveUp = index > 0;
            bool showMoveDown = index < parent.childCount - 1;

            if (showMoveUp)
            {
                menu.AddItem(new GUIContent($"▲{I18n.MoveUp}"), false, () =>
                {
                    _parentList?.MoveItem(index, index - 1);
                    OnChange?.Invoke();
                });
            }
            if (showMoveDown)
            {
                menu.AddItem(new GUIContent($"▼{I18n.MoveDown}"), false, () =>
                {
                    _parentList?.MoveItem(index, index + 1);
                    OnChange?.Invoke();
                });
            }
            menu.AddItem(new GUIContent($"✎{I18n.SetIndex}"), false, () =>
            {
                EditMode(true);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"✖{I18n.DeleteItem}"), false, RemoveSelf);
            menu.AddSeparator("");
        }
        #endregion
    }
}
