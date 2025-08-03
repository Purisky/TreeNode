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
        // 数据源
        public IList ItemsSource { get; set; }
        
        // 配置属性
        public bool HasPort { get; private set; }
        public bool ShowBorder { get; set; } = true;
        public bool ShowAlternatingRowBackgrounds { get; set; } = true;
        public float FixedItemHeight { get; set; } = -1;
        
        // 委托
        public Func<VisualElement> MakeItem { get; set; }
        public Action<VisualElement, int> BindItem { get; set; }
        
        // UI组件
        public VisualElement HeaderContainer { get; private set; }
        public Foldout FoldoutHeader { get; private set; }  // 仅HasPort=false时使用
        public Label SimpleHeader { get; private set; }     // 仅HasPort=true时使用
        public Label SizeLabel { get; private set; }
        public Button AddButton { get; private set; }
        public VisualElement ItemContainer { get; private set; }
        
        public ListElement(MemberMeta memberMeta, ViewNode node, PAPath path, BaseDrawer drawer, bool hasPort)
            : base(memberMeta, node, path, drawer, null)
        {
            HasPort = hasPort;
            InitializeUI();
        }
        
        private void InitializeUI()
        {
            // 设置基础样式
            style.flexGrow = 1;
            AddToClassList("unity-list-view"); // 复用ListView的CSS类名
            
            if (ShowBorder)
            {
                AddToClassList("unity-list-view--with-border");
            }
            
            CreateHeader();
            CreateItemContainer();
        }
        
        private void CreateHeader()
        {
            HeaderContainer = new VisualElement();
            HeaderContainer.name = "unity-list-view__header";
            HeaderContainer.AddToClassList("unity-list-view__header");
            HeaderContainer.style.flexDirection = FlexDirection.Row;
            HeaderContainer.style.alignItems = Align.Center;
            
            if (HasPort)
            {
                // HasPort=true: 简化头部，禁用折叠
                CreateSimpleHeader();
            }
            else
            {
                // HasPort=false: 完整Foldout头部
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
            headerContent.style.flexGrow = 1;
            headerContent.style.flexDirection = FlexDirection.Row;
            headerContent.style.alignItems = Align.Center;
            headerContent.style.height = 22;
            headerContent.style.paddingLeft = 4;
            headerContent.style.paddingTop = 4;
            
            SimpleHeader = BaseDrawer.CreateLabel(MemberMeta.LabelInfo);
            SizeLabel = new Label();
            SizeLabel.style.marginLeft = 8;
            SizeLabel.style.fontSize = 11;
            SizeLabel.style.color = Color.gray;
            
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
            
            // 创建大小显示标签
            SizeLabel = new Label();
            SizeLabel.style.marginLeft = 8;
            SizeLabel.style.fontSize = 11;
            SizeLabel.style.color = Color.gray;
            
            // 将大小标签添加到Foldout的toggle中
            var toggle = FoldoutHeader.Q<Toggle>();
            if (toggle != null)
            {
                toggle.Add(SizeLabel);
            }
            
            // 监听折叠状态变化
            FoldoutHeader.RegisterValueChangedCallback(evt =>
            {
                ItemContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            HeaderContainer.Add(FoldoutHeader);
        }
        
        private void CreateItemContainer()
        {
            ItemContainer = new VisualElement();
            ItemContainer.name = "unity-list-view__item-container";
            ItemContainer.AddToClassList("unity-list-view__item-container");
            ItemContainer.style.flexGrow = 1;
            
            Add(ItemContainer);
        }
        
        private Button CreateAddButton()
        {
            Button addBtn = new Button() { name = "addbtn", text = "+" };
            addBtn.style.height = 17;
            addBtn.style.width = 17;
            addBtn.style.marginBottom = 0;
            addBtn.style.marginTop = 0;
            addBtn.style.paddingBottom = 4;
            addBtn.style.fontSize = 18;
            return addBtn;
        }
        
        /// <summary>
        /// 同步重建所有项目 - 核心方法
        /// </summary>
        public void RefreshItems()
        {
            // 清理现有项目
            foreach (var child in ItemContainer.Children().ToList())
            {
                if (child is ListItem item)
                {
                    item.Cleanup(); // 清理ChildPort连接
                }
                ItemContainer.Remove(child);
            }
            
            // 同步创建新项目
            if (ItemsSource != null)
            {
                for (int i = 0; i < ItemsSource.Count; i++)
                {
                    var listItem = MakeItem?.Invoke();
                    if (listItem != null)
                    {
                        BindItem?.Invoke(listItem, i);
                        ItemContainer.Add(listItem);
                        
                        // 立即处理端口连接
                        if (listItem is ListItem item && item.HasPort)
                        {
                            item.ProcessChildPorts();
                        }
                    }
                }
            }
            
            UpdateSizeDisplay();
            UpdateAlternatingBackgrounds();
        }
        
        private void UpdateSizeDisplay()
        {
            int count = ItemsSource?.Count ?? 0;
            if (SizeLabel != null)
            {
                SizeLabel.text = $"({count})";
            }
        }
        
        private void UpdateAlternatingBackgrounds()
        {
            if (!ShowAlternatingRowBackgrounds) return;
            
            for (int i = 0; i < ItemContainer.childCount; i++)
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
        
        /// <summary>
        /// 删除指定索引的项目
        /// </summary>
        public void RemoveItem(int index)
        {
            if (index < 0 || index >= (ItemsSource?.Count ?? 0)) return;
            
            // 同步操作数据源
            ItemsSource.RemoveAt(index);
            
            // 立即更新UI
            if (index < ItemContainer.childCount)
            {
                var itemToRemove = ItemContainer.ElementAt(index);
                if (itemToRemove is ListItem listItem)
                {
                    listItem.Cleanup();
                }
                ItemContainer.RemoveAt(index);
                
                // 更新后续项目的索引
                for (int i = index; i < ItemContainer.childCount; i++)
                {
                    if (ItemContainer.ElementAt(i) is ListItem item)
                    {
                        item.UpdateIndex(i);
                    }
                }
            }
            
            UpdateSizeDisplay();
            UpdateAlternatingBackgrounds();
        }
        
        /// <summary>
        /// 移动项目
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
            
            // 立即更新UI
            if (fromIndex < ItemContainer.childCount && toIndex < ItemContainer.childCount)
            {
                var visualItem = ItemContainer.ElementAt(fromIndex);
                ItemContainer.RemoveAt(fromIndex);
                ItemContainer.Insert(toIndex, visualItem);
                
                // 更新索引
                int start = Math.Min(fromIndex, toIndex);
                int end = Math.Max(fromIndex, toIndex);
                for (int i = start; i <= end && i < ItemContainer.childCount; i++)
                {
                    if (ItemContainer.ElementAt(i) is ListItem listItem)
                    {
                        listItem.UpdateIndex(i);
                    }
                }
            }
            
            UpdateAlternatingBackgrounds();
        }
        
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
        #endregion

        #region 公共属性
        public int Index => IndexField.value;
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
            
            // 初始化UI
            InitializeUI();
        }
        #endregion

        #region UI初始化
        private void InitializeUI()
        {
            // 应用样式
            styleSheets.Add(StyleSheet);
            style.height = Length.Auto();
            
            // 创建索引显示区域
            CreateIndexElement();
            
            // 注册事件
            RegisterEvents();
            
            _isInitialized = true;
        }
        
        private void CreateIndexElement()
        {
            IndexElement = new VisualElement { name = "listitemindex" };
            IndexElement.style.position = Position.Absolute;
            IndexElement.style.left = 0;
            IndexElement.style.height = Length.Percent(100);
            IndexElement.pickingMode = PickingMode.Ignore;
            
            IndexField = new IntegerField { name = "listitemindexfield" };
            IndexLabel = new Label { name = "listitemindexlabel", pickingMode = PickingMode.Ignore };
            
            IndexElement.Add(IndexLabel);
            IndexElement.Add(IndexField);
            Add(IndexElement);
        }
        
        private void RegisterEvents()
        {
            // 注册右键菜单
            RegisterCallback<MouseDownEvent>((evt) =>
            {
                if (evt.button == 1)
                {
                    GenericMenu menu = new GenericMenu();
                    AddItemsToMenu(menu);
                    menu.ShowAsContext();
                }
            });
            
            // 注册索引变更事件
            IndexField.RegisterValueChangedCallback(OnIndexChanged);
            IndexField.RegisterCallback<BlurEvent>((evt) => CommitEdit());
        }
        #endregion

        #region 核心方法
        /// <summary>
        /// 初始化值 - 同步版本，立即可用
        /// </summary>
        public void InitValue(int index)
        {
            if (!_isInitialized) return;
            
            _currentIndex = index;
            IndexLabel.text = index.ToString();
            IndexField.value = index;

            string propertyPath = $"{Path}[{index}]";

            // 清理现有内容
            if (_contentElement != null)
            {
                RemoveEdges();
                Remove(_contentElement);
                _contentElement = null;
            }

            // 同步创建新内容
            _contentElement = Drawer.Create(Meta, ViewNode, propertyPath, Action);
            _contentElement.style.flexGrow = 1;
            _contentElement.style.paddingLeft = 40;
            Insert(0, _contentElement);

            // 立即处理ChildPort连接
            if (HasPort)
            {
                ProcessChildPorts();
            }
        }
        
        /// <summary>
        /// 立即处理ChildPort连接 - 同步版本，无延迟
        /// </summary>
        public void ProcessChildPorts()
        {
            if (!HasPort) return;
            
            // 立即查询ChildPort，无需schedule
            ChildPorts = this.Query<ChildPort>().ToList();
            
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
            
            if (_contentElement != null)
            {
                Remove(_contentElement);
                _contentElement = null;
            }
        }
        
        /// <summary>
        /// 更新索引显示
        /// </summary>
        public void UpdateIndex(int newIndex)
        {
            _currentIndex = newIndex;
            IndexLabel.text = newIndex.ToString();
            IndexField.value = newIndex;
        }
        #endregion

        #region 端口管理
        private void RemoveEdges()
        {
            if (!ChildPorts.Any()) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (childPort.connected)
                {
                    List<Edge> edges = childPort.connections.ToList();
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

        private void AddEdges()
        {
            if (!ChildPorts.Any()) return;
            
            foreach (var childPort in ChildPorts)
            {
                if (!ViewNode.ChildPorts.Contains(childPort))
                {
                    ViewNode.ChildPorts.Add(childPort);
                }
            }
        }
        #endregion

        #region 事件处理
        private void OnIndexChanged(ChangeEvent<int> evt)
        {
            int index = evt.newValue;
            if (index < 0)
            {
                IndexField.value = 0;
                return;
            }
            
            if (_parentList != null && index >= (_parentList.ItemsSource?.Count ?? 0))
            {
                IndexField.value = (_parentList.ItemsSource?.Count ?? 1) - 1;
                return;
            }
        }
        
        private void CommitEdit()
        {
            int index = IndexField.value;
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
                IndexField.schedule.Execute(() =>
                {
                    IndexField.Focus();
                    IndexField.SelectAll();
                }).ExecuteLater(50);
            }
            else
            {
                RemoveFromClassList("editmode");
            }
        }
        #endregion

        #region 操作方法
        public void RemoveSelf()
        {
            int index = parent.IndexOf(this);
            
            // 清理所有Edge连接
            foreach (var child in parent.Children())
            {
                if (child is ListItem listItem)
                {
                    listItem.RemoveEdges();
                }
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
