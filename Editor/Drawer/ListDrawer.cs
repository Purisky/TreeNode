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
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// ListDrawer - 高性能列表组件绘制器
    /// 优化版本：移除锁机制，提升性能，改善可读性
    /// </summary>
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

            //Debug.Log($"ListDrawer memberMeta.Path:{memberMeta.Path}=>{path}");
            listElement.MakeItem = () => new ListItem(listElement, memberMeta, node, itemDrawer, path, dirty, action);
            listElement.BindItem = (element, index) => ((ListItem)element).InitValue(index);
            
            // 配置添加按钮事件
            ConfigureAddButton(listElement, node, path, gType, dirty, action);
            
            // 设置启用状态
            listElement.SetEnabled(!memberMeta.ShowInNode.ReadOnly);
            
            // 立即同步渲染
            listElement.RefreshItems();
            
            return listElement;
        }

        /// <summary>
        /// 配置添加按钮事件 - 提取方法提升可读性
        /// </summary>
        private static void ConfigureAddButton(ListElement listElement, ViewNode node, PAPath path, Type gType, bool dirty, Action action)
        {
            listElement.AddButton.clicked += () =>
            {
                EnsureListExists(listElement, node, path, gType);
                
                // 记录添加操作前的状态，用于 History
                object newItem = CreateNewItem(gType);
                int insertIndex = listElement.ItemsSource.Count;
                
                AddItemToList(listElement, newItem);
                
                // 记录 History
                listElement.RecordItem(path, newItem, -1, insertIndex);
                
                if (dirty)
                {
                    listElement.SetDirty();
                }
                action?.Invoke();
                listElement.RefreshItems();
                listElement.Focus();
            };
        }

        /// <summary>
        /// 确保列表存在 - 延迟初始化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureListExists(ListElement listElement, ViewNode node, PAPath path, Type gType)
        {
            if (listElement.ItemsSource == null)
            {
                IList newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(gType));
                node.Data.SetValue(path, newList);
                listElement.ItemsSource = newList;
            }
        }

        /// <summary>
        /// 创建新项目实例
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object CreateNewItem(Type gType)
        {
            if (gType.GetConstructor(Type.EmptyTypes) == null)
            {
                return RuntimeHelpers.GetUninitializedObject(gType);
            }
            else
            {
                return Activator.CreateInstance(gType);
            }
        }

        /// <summary>
        /// 添加新项目到列表
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddItemToList(ListElement listElement, object newItem)
        {
            listElement.ItemsSource.Add(newItem);
        }
    }

    /// <summary>
    /// ListElement - 继承自PropertyElement的高性能列表组件
    /// 完全替代ListView，实现同步初始化和智能折叠控制
    /// 优化版本：移除锁机制，提升性能，减少内存分配
    /// </summary>
    public class ListElement : PropertyElement
    {
        #region 常量定义
        private static class ListConstants
        {
            public const int UI_UPDATE_DELAY_MS = 1;
            public const int DEFAULT_HEADER_HEIGHT = 22;
            public const int ADD_BUTTON_SIZE = 17;
            public const int INDEX_ELEMENT_WIDTH = 40;
            public const float BORDER_COLOR_ALPHA = 0.5f;
            public const float SIZE_LABEL_ALPHA = 0.7f;
            public const int SIZE_LABEL_FONT_SIZE = 11;
            public const int ADD_BUTTON_FONT_SIZE = 18;
            public const int BORDER_RADIUS = 2;
        }
        #endregion
        
        #region 私有字段 - 移除锁相关
        private bool _isRefreshing = false; // 简单标志位替代锁
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
            schedule.Execute(ApplyAdvancedStyles).ExecuteLater(ListConstants.UI_UPDATE_DELAY_MS);
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
            }
        }
        #endregion
        
        #region 头部创建
        private void CreateHeader()
        {
            HeaderContainer = CreateHeaderContainer();
            
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VisualElement CreateHeaderContainer()
        {
            var container = new VisualElement
            {
                name = "unity-list-view__header"
            };
            container.AddToClassList("unity-list-view__header");
            
            // 批量设置头部样式，减少重排
            var headerStyle = container.style;
            headerStyle.flexDirection = FlexDirection.Row;
            headerStyle.alignItems = Align.Center;
            headerStyle.minHeight = ListConstants.DEFAULT_HEADER_HEIGHT;
            
            return container;
        }
        
        private void CreateSimpleHeader()
        {
            var headerContent = CreateHeaderContent();
            
            SimpleHeader = BaseDrawer.CreateLabel(MemberMeta.LabelInfo);
            SizeLabel = CreateSizeLabel();
            
            headerContent.Add(SimpleHeader);
            headerContent.Add(SizeLabel);
            HeaderContainer.Add(headerContent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VisualElement CreateHeaderContent()
        {
            var headerContent = new VisualElement();
            var contentStyle = headerContent.style;
            contentStyle.flexGrow = 1;
            contentStyle.flexDirection = FlexDirection.Row;
            contentStyle.alignItems = Align.Center;
            contentStyle.height = ListConstants.DEFAULT_HEADER_HEIGHT;
            contentStyle.paddingLeft = 4;
            contentStyle.paddingTop = 4;
            
            return headerContent;
        }
        
        private void CreateFoldoutHeader()
        {
            FoldoutHeader = new Foldout
            {
                name = "unity-list-view__foldout-header",
                text = MemberMeta.LabelInfo?.Text ?? "List",
                value = true // 默认展开
            };
            FoldoutHeader.AddToClassList("unity-list-view__foldout-header");
            
            SizeLabel = CreateSizeLabel();
            
            // 优化：将大小标签添加到Foldout的toggle中
            AddSizeLabelToFoldout();
            
            // 高效的折叠状态变化监听
            FoldoutHeader.RegisterValueChangedCallback(OnFoldoutChanged);
            
            HeaderContainer.Add(FoldoutHeader);
        }

        private void AddSizeLabelToFoldout()
        {
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
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Label CreateSizeLabel()
        {
            var sizeLabel = new Label();
            var style = sizeLabel.style;
            style.marginLeft = 8;
            style.fontSize = ListConstants.SIZE_LABEL_FONT_SIZE;
            style.color = new Color(ListConstants.SIZE_LABEL_ALPHA, ListConstants.SIZE_LABEL_ALPHA, ListConstants.SIZE_LABEL_ALPHA, 1f);
            style.unityFontStyleAndWeight = FontStyle.Normal;
            return sizeLabel;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnFoldoutChanged(ChangeEvent<bool> evt)
        {
            // 高性能的显示/隐藏切换
            ItemContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
        }
        #endregion
        
        #region 容器创建
        private void CreateItemContainer()
        {
            ItemContainer = new VisualElement
            {
                name = "unity-list-view__item-container"
            };
            ItemContainer.AddToClassList("unity-list-view__item-container");
            
            // 批量设置容器样式
            var containerStyle = ItemContainer.style;
            containerStyle.flexGrow = 1;
            containerStyle.overflow = Overflow.Hidden; // 性能优化
            
            Add(ItemContainer);
        }
        
        private Button CreateAddButton()
        {
            var addBtn = new Button 
            { 
                name = "addbtn", 
                text = "+" 
            };
            
            // 批量设置按钮样式 - 使用常量
            var btnStyle = addBtn.style;
            btnStyle.height = ListConstants.ADD_BUTTON_SIZE;
            btnStyle.width = ListConstants.ADD_BUTTON_SIZE;
            btnStyle.marginBottom = 0;
            btnStyle.marginTop = 0;
            btnStyle.paddingBottom = 4;
            btnStyle.fontSize = ListConstants.ADD_BUTTON_FONT_SIZE;
            btnStyle.borderTopLeftRadius = ListConstants.BORDER_RADIUS;
            btnStyle.borderTopRightRadius = ListConstants.BORDER_RADIUS;
            btnStyle.borderBottomLeftRadius = ListConstants.BORDER_RADIUS;
            btnStyle.borderBottomRightRadius = ListConstants.BORDER_RADIUS;
            
            return addBtn;
        }
        #endregion
        
        #region 核心刷新方法 - 高性能版本（移除锁）
        /// <summary>
        /// 同步重建所有项目 - 高性能版本，移除锁机制，优化DOM操作
        /// </summary>
        public void RefreshItems()
        {
            // 使用简单标志位防止重入，避免锁开销
            if (_isRefreshing) return;
            _isRefreshing = true;
            
            try
            {
                RefreshItemsCore();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        /// <summary>
        /// 核心刷新逻辑 - 分离职责，提升可读性
        /// </summary>
        private void RefreshItemsCore()
        {
            // 步骤1：快速清理现有项目
            ClearExistingItems();
            
            // 步骤2：批量创建新项目
            if (ItemsSource?.Count > 0)
            {
                CreateNewItems();
            }
            
            // 步骤3：更新UI状态
            UpdateUIState();
        }
        
        /// <summary>
        /// 清理现有项目 - 优化版本，避免ToList()内存分配
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearExistingItems()
        {
            var childCount = ItemContainer.childCount;
            
            // 反向遍历避免索引问题，无需ToList()
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = ItemContainer.ElementAt(i);
                if (child is ListItem item)
                {
                    item.Cleanup();
                }
            }
            
            // 一次性清空容器
            ItemContainer.Clear();
        }
        
        /// <summary>
        /// 批量创建新项目 - 优化版本，减少DOM操作
        /// </summary>
        private void CreateNewItems()
        {
            var itemCount = ItemsSource.Count;
            
            // 预分配集合避免动态扩容
            var newItems = new List<VisualElement>(itemCount);
            
            // 批量创建项目，延迟DOM操作
            for (int i = 0; i < itemCount; i++)
            {
                var listItem = MakeItem?.Invoke();
                if (listItem != null)
                {
                    BindItem?.Invoke(listItem, i);
                    newItems.Add(listItem);
                    
                    // 立即处理端口连接，无延迟
                    ProcessItemPorts(listItem);
                }
            }
            
            // 批量添加到容器，减少重排次数
            AddItemsToContainer(newItems);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessItemPorts(VisualElement listItem)
        {
            if (listItem is ListItem item && item.HasPort)
            {
                item.ProcessChildPorts();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddItemsToContainer(List<VisualElement> newItems)
        {
            foreach (var item in newItems)
            {
                ItemContainer.Add(item);
            }
        }
        
        /// <summary>
        /// 更新UI状态 - 分离的方法，提升可读性
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateUIState()
        {
            UpdateSizeDisplay();
            
            // 延迟更新交替背景，避免阻塞
            if (ShowAlternatingRowBackgrounds)
            {
                schedule.Execute(UpdateAlternatingBackgrounds).ExecuteLater(ListConstants.UI_UPDATE_DELAY_MS);
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
            if (!IsValidIndex(index)) return;
            
            // 记录删除前的状态，用于 History
            object removedValue = ItemsSource[index];
            
            // 同步操作数据源
            ItemsSource.RemoveAt(index);
            
            // 记录 History
            this.RecordItem(LocalPath, removedValue, index, -1);
            
            // 高效的UI更新
            RemoveItemFromUI(index);
            
            UpdateUIState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIndex(int index)
        {
            return index >= 0 && index < (ItemsSource?.Count ?? 0);
        }

        private void RemoveItemFromUI(int index)
        {
            if (index < ItemContainer.childCount)
            {
                var itemToRemove = ItemContainer.ElementAt(index);
                if (itemToRemove is ListItem listItem)
                {
                    listItem.Cleanup();
                }
                ItemContainer.RemoveAt(index);
                
                // 批量更新后续项目的索引
                UpdateIndicesFrom(index);
            }
        }
        
        /// <summary>
        /// 移动项目 - 高性能版本，添加 History 记录
        /// </summary>
        public void MoveItem(int fromIndex, int toIndex)
        {
            if (!IsValidMoveOperation(fromIndex, toIndex)) return;
                
            // 记录移动前的状态，用于 History
            var item = ItemsSource[fromIndex];
            
            // 同步操作数据源
            ItemsSource.RemoveAt(fromIndex);
            ItemsSource.Insert(toIndex, item);
            
            // 记录 History
            this.RecordItem(LocalPath, item, fromIndex, toIndex);
            
            // 高效的UI更新
            MoveItemInUI(fromIndex, toIndex);
            
            // 延迟更新交替背景
            schedule.Execute(UpdateAlternatingBackgrounds).ExecuteLater(ListConstants.UI_UPDATE_DELAY_MS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidMoveOperation(int fromIndex, int toIndex)
        {
            return fromIndex >= 0 && toIndex >= 0 && 
                   fromIndex < (ItemsSource?.Count ?? 0) && 
                   toIndex < (ItemsSource?.Count ?? 0) &&
                   fromIndex != toIndex;
        }

        private void MoveItemInUI(int fromIndex, int toIndex)
        {
            if (fromIndex < ItemContainer.childCount && toIndex < ItemContainer.childCount)
            {
                var visualItem = ItemContainer.ElementAt(fromIndex);
                ItemContainer.RemoveAt(fromIndex);
                ItemContainer.Insert(toIndex, visualItem);
                
                // 批量更新索引范围
                UpdateIndicesInRange(fromIndex, toIndex);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateIndicesFrom(int startIndex)
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
        private void UpdateIndicesInRange(int fromIndex, int toIndex)
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
        
        #region ViewChange应用 - 新增功能
        /// <summary>
        /// 应用ViewChange - 用于恢复显示层状态
        /// 支持处理ListItem类型的变更，实现Undo/Redo操作的UI同步
        /// </summary>
        /// <param name="viewChange">视图变更信息</param>
        public void ApplyViewChange(ViewChange viewChange)
        {
            int fromIndex = viewChange.ExtraInfo[0];
            int toIndex = viewChange.ExtraInfo[1];
            // 重新同步数据源 - 确保与数据层一致
            SyncItemsSourceFromData();

            // 应用UI变更
            ApplyListItemChange(fromIndex, toIndex);
        }

        /// <summary>
        /// 同步数据源 - 确保ItemsSource与底层数据一致
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SyncItemsSourceFromData()
        {
            try
            {
                var currentData = ViewNode.Data.GetValue<IList>(LocalPath);
                if (currentData != ItemsSource)
                {
                    ItemsSource = currentData;
                    Debug.Log($"ListElement.SyncItemsSourceFromData: 已同步数据源，新长度={ItemsSource?.Count ?? 0}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ListElement.SyncItemsSourceFromData: 同步失败 - {ex.Message}");
            }
        }

        /// <summary>
        /// 应用列表项变更 - 优化版本，避免完全重建
        /// </summary>
        /// <param name="fromIndex">原始索引（-1表示新增）</param>
        /// <param name="toIndex">目标索引（-1表示删除）</param>
        private void ApplyListItemChange(int fromIndex, int toIndex)
        {
            if (fromIndex == -1 && toIndex >= 0)
            {
                // 新增项目
                ApplyItemAddition(toIndex);
            }
            else if (fromIndex >= 0 && toIndex == -1)
            {
                // 删除项目
                ApplyItemDeletion(fromIndex);
            }
            else if (fromIndex >= 0 && toIndex >= 0)
            {
                // 移动项目
                ApplyItemMove(fromIndex, toIndex);
            }
            else
            {
                // 无效操作，执行完全刷新
                Debug.LogWarning($"ListElement.ApplyListItemChange: 无效的索引组合 fromIndex={fromIndex}, toIndex={toIndex}，执行完全刷新");
                RefreshItems();
            }
        }

        /// <summary>
        /// 应用项目新增 - 高性能版本，避免完全重建
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyItemAddition(int index)
        {
            if (ItemsSource == null || index >= ItemsSource.Count)
            {
                // 数据不一致，执行完全刷新
                RefreshItems();
                return;
            }

            // 创建新的ListItem
            var newItem = MakeItem?.Invoke();
            if (newItem != null)
            {
                BindItem?.Invoke(newItem, index);
                
                // 插入到正确位置
                if (index < ItemContainer.childCount)
                {
                    ItemContainer.Insert(index, newItem);
                }
                else
                {
                    ItemContainer.Add(newItem);
                }

                // 处理端口
                ProcessItemPorts(newItem);

                // 更新后续项目的索引
                UpdateIndicesFrom(index + 1);
                
                // 更新UI状态
                UpdateUIState();
            }
        }

        /// <summary>
        /// 应用项目删除 - 高性能版本，避免完全重建
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyItemDeletion(int index)
        {
            if (index >= 0 && index < ItemContainer.childCount)
            {
                var itemToRemove = ItemContainer.ElementAt(index);
                if (itemToRemove is ListItem listItem)
                {
                    listItem.Cleanup();
                }
                ItemContainer.RemoveAt(index);

                // 更新后续项目的索引
                UpdateIndicesFrom(index);
                
                // 更新UI状态
                UpdateUIState();
            }
        }

        /// <summary>
        /// 应用项目移动 - 高性能版本，避免完全重建
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyItemMove(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex < 0 || 
                fromIndex >= ItemContainer.childCount || 
                toIndex >= ItemContainer.childCount ||
                fromIndex == toIndex)
            {
                // 索引无效，执行完全刷新
                RefreshItems();
                return;
            }

            // 移动视觉元素
            var visualItem = ItemContainer.ElementAt(fromIndex);
            ItemContainer.RemoveAt(fromIndex);
            ItemContainer.Insert(toIndex, visualItem);

            // 批量更新索引范围
            UpdateIndicesInRange(fromIndex, toIndex);
            
            // 延迟更新交替背景
            schedule.Execute(UpdateAlternatingBackgrounds).ExecuteLater(ListConstants.UI_UPDATE_DELAY_MS);
        }

        /// <summary>
        /// 检查是否支持ViewChange应用
        /// </summary>
        /// <param name="viewChange">视图变更</param>
        /// <returns>是否支持</returns>
        public bool CanApplyViewChange(ViewChange viewChange)
        {
            return viewChange.ChangeType == ViewChangeType.ListItem &&
                   viewChange.Path.Equals(LocalPath);
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
    /// 优化版本：提取常量，改善可读性，优化性能
    /// </summary>
    public class ListItem : VisualElement
    {
        #region 静态资源和常量
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ListItem");
        
        private static class ItemConstants
        {
            public const int INDEX_ELEMENT_WIDTH = 40;
            public const float BORDER_COLOR_ALPHA = 0.5f;
            public const int EDIT_MODE_DELAY_MS = 25;
        }
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
        public Dictionary<PAPath,ChildPort> ChildPorts { get; private set; }
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
            InitializeProperties(meta, node, baseDrawer, path, action);
            
            // 初始化OnChange委托
            InitializeOnChange(dirty);
            
            // 检查是否有Port
            CheckPortAvailability();
            
            // 高性能UI初始化
            InitializeUIOptimized();
        }

        private void InitializeProperties(MemberMeta meta, ViewNode node, BaseDrawer baseDrawer, string path, Action action)
        {
            Action = action;
            Meta = meta;
            ViewNode = node;
            Path = path;
            Drawer = baseDrawer;
            ChildPorts = new ();
        }

        private void InitializeOnChange(bool dirty)
        {
            OnChange = () =>
            {
                if (dirty)
                {
                    this.SetDirty();
                }
                Action?.Invoke();
                ViewNode.PopupText();
            };
        }

        private void CheckPortAvailability()
        {
            if (Drawer is ComplexDrawer complex)
            {
                HasPort = complex.HasPort;
            }
        }
        #endregion

        #region 高性能UI初始化
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeUIOptimized()
        {
            // 应用样式
            styleSheets.Add(StyleSheet);
            
            // 批量设置基础样式
            ApplyBaseStyles();
            
            // 添加ListView兼容的CSS类
            AddToClassList("unity-list-view__item");
            
            // 创建索引显示区域
            CreateIndexElementOptimized();
            
            // 注册事件
            RegisterEvents();
            
            _isInitialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyBaseStyles()
        {
            var style = this.style;
            style.height = Length.Auto();
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateIndexElementOptimized()
        {
            IndexElement = CreateIndexContainer();
            
            IndexField = new IntegerField { name = "listitemindexfield" };
            IndexLabel = new Label { name = "listitemindexlabel", pickingMode = PickingMode.Ignore };
            
            // 优化标签和输入框样式
            ApplyIndexElementStyles();
            
            IndexElement.Add(IndexLabel);
            IndexElement.Add(IndexField);
            Add(IndexElement);
        }

        private VisualElement CreateIndexContainer()
        {
            var container = new VisualElement { name = "listitemindex" };
            
            // 批量设置索引元素样式
            var indexStyle = container.style;
            indexStyle.position = Position.Absolute;
            indexStyle.left = 0;
            indexStyle.height = Length.Percent(100);
            indexStyle.width = ItemConstants.INDEX_ELEMENT_WIDTH;
            indexStyle.borderRightWidth = 1;
            indexStyle.borderRightColor = new Color(ItemConstants.BORDER_COLOR_ALPHA, ItemConstants.BORDER_COLOR_ALPHA, ItemConstants.BORDER_COLOR_ALPHA, 1f);
            indexStyle.flexDirection = FlexDirection.Row;
            container.pickingMode = PickingMode.Ignore;
            
            return container;
        }

        private void ApplyIndexElementStyles()
        {
            var labelStyle = IndexLabel.style;
            labelStyle.flexGrow = 1;
            labelStyle.unityTextAlign = TextAnchor.MiddleCenter;
            labelStyle.display = DisplayStyle.Flex;
            
            var fieldStyle = IndexField.style;
            fieldStyle.flexGrow = 1;
            fieldStyle.display = DisplayStyle.None;
            fieldStyle.unityTextAlign = TextAnchor.MiddleCenter;
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
                var menu = new GenericMenu();
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
            UpdateIndexDisplay(index);

            string propertyPath = $"{Path}[{index}]";

            // 高效的内容清理和重建
            CleanupContent();
            CreateContent(propertyPath);

            // 立即处理ChildPort连接
            ProcessPortsIfNeeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateIndexDisplay(int index)
        {
            if (IndexLabel != null) IndexLabel.text = index.ToString();
            if (IndexField != null) IndexField.value = index;
        }

        private void ProcessPortsIfNeeded()
        {
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
            Meta.LabelInfo.Hide = true;
            _contentElement = Drawer.Create(Meta, ViewNode, propertyPath, Action);
            
            // 批量设置内容样式
            var contentStyle = _contentElement.style;
            contentStyle.flexGrow = 1;
            contentStyle.paddingLeft = ItemConstants.INDEX_ELEMENT_WIDTH;
            
            Insert(0, _contentElement);
        }
        
        /// <summary>
        /// 立即处理ChildPort连接 - 同步版本，无延迟
        /// 优化版本：避免ToList()内存分配
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessChildPorts()
        {
            if (!HasPort) return;
            
            // 清空现有端口
            ChildPorts.Clear();
            
            // 直接遍历查询结果，避免ToList()
            var portQuery = this.Query<ChildPort>();



            portQuery.ForEach(port =>
            {
                ChildPorts.Add(port.LocalPath, port);
            } );
            
            // 立即添加边连接
            AddPorts();
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
            UpdateIndexDisplay(newIndex);
        }
        #endregion

        #region 端口管理 - 优化版本
        private void RemoveEdges()
        {
            if (ChildPorts.Count == 0) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (childPort.Value.connected)
                {
                    // 优化：避免ToList()，直接遍历
                    var connections = childPort.Value.connections;
                    var edgesToRemove = new List<Edge>(connections);
                    
                    foreach (var edge in edgesToRemove)
                    {
                        edge.ParentPort().DisconnectAll();
                        ViewNode.View.RemoveElement(edge);
                    }
                    childPort.Value.DisconnectAll();
                }
                ViewNode.ChildPorts.Remove(childPort.Key);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddPorts()
        {
            if (ChildPorts.Count == 0) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (!ViewNode.ChildPorts.Contains(childPort))
                {
                    ViewNode.ChildPorts.Add(childPort.Value.LocalPath,childPort.Value);
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
                IndexLabel.style.display = DisplayStyle.None;
                IndexField.style.display = DisplayStyle.Flex;
                // 使用常量替代魔法数字
                IndexField?.schedule.Execute(() =>
                {
                    IndexField.Focus();
                    IndexField.SelectAll();
                }).ExecuteLater(ItemConstants.EDIT_MODE_DELAY_MS);
            }
            else
            {
                IndexLabel.style.display = DisplayStyle.Flex;
                IndexField.style.display = DisplayStyle.None;
                RemoveFromClassList("editmode");
            }
        }
        #endregion

        #region 操作方法 - 优化版本
        public void RemoveSelf()
        {
            int index = parent.IndexOf(this);
            
            // 记录删除前的状态，用于 History
            object removedValue = _parentList?.ItemsSource[index];
            
            // 优化：避免ToList()内存分配
            var listItems = new List<ListItem>();
            foreach (var child in parent.Children())
            {
                if (child is ListItem item)
                {
                    listItems.Add(item);
                }
            }
            
            // 高效的批量Edge清理
            foreach (var listItem in listItems)
            {
                listItem.RemoveEdges();
            }
            
            // 将ChildPort的子值添加回节点数据
            var childValues = new List<JsonNode>();
            foreach (var port in ChildPorts)
            {
                childValues.AddRange(port.Value.GetChildValues());
            }
            ViewNode.View.Asset.Data.Nodes.AddRange(childValues);
            
            // 记录 History - 通过 ListElement 的扩展方法
            if (removedValue != null)
            {
                _parentList?.RecordItem(_parentList.LocalPath, removedValue, index, -1);
            }
            
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
