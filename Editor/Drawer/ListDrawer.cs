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
            
            listElement.MakeItem = () => new ListItem(memberMeta, node, itemDrawer, path, dirty, action);
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

    public class ListItem : VisualElement
    {
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("ListItem");
        public int Index => IndexField.value;
        public Label IndexLabel;
        public VisualElement IndexElement;
        public IntegerField IndexField;
        public PropertyElement Value;
        public ViewNode ViewNode;
        public string Path;
        public MemberMeta Meta;

        public BaseDrawer Drawer;
        Action OnChange;
        Action Action;
        public bool HasPort;
        public List<ChildPort> ChildPorts;
        private ListElement _parentList;

        public ListItem(MemberMeta meta, ViewNode node, BaseDrawer baseDrawer, string path, bool dirty, Action action)
        {
            Action = action;
            Meta = meta;
            ViewNode = node;
            Path = path;
            Drawer = baseDrawer;
            OnChange = () =>
            {
                if (dirty)
                {
                    this.SetDirty();
                }
                Action?.Invoke();
                ViewNode.PopupText();
            };
            if (Drawer is ComplexDrawer complex)
            {
                HasPort = complex.HasPort;
            }
            styleSheets.Add(StyleSheet);
            style.height = Length.Auto();
            IndexElement = new() { name = "listitemindex" };
            IndexElement.style.position = Position.Absolute;
            IndexElement.style.left = 0;
            IndexElement.style.height = Length.Percent(100);
            IndexElement.pickingMode = PickingMode.Ignore;
            IndexField = new() { name = "listitemindexfield" };
            IndexLabel = new() { name = "listitemindexlabel", pickingMode = PickingMode.Ignore };
            IndexElement.Add(IndexLabel);
            IndexElement.Add(IndexField);
            Add(IndexElement);
            RegisterCallback<MouseDownEvent>((evt) =>
            {
                if (evt.button == 1)
                {
                    GenericMenu menu = new();
                    AddItemsToMenu(menu);
                    menu.ShowAsContext();
                }
            });
            IndexField.RegisterValueChangedCallback((evt) =>
            {
                int index = evt.newValue;
                if (index < 0)
                {
                    IndexField.value = 0;
                    return;
                }
                // 尝试获取父级ListElement
                if (_parentList == null)
                {
                    _parentList = this.GetFirstAncestorOfType<ListElement>();
                }
                if (_parentList != null && index >= (_parentList.ItemsSource?.Count ?? 0))
                {
                    IndexField.value = (_parentList.ItemsSource?.Count ?? 1) - 1;
                    return;
                }
            });
            IndexField.RegisterCallback<BlurEvent>((evt) => CommitEdit());
            ChildPorts = new();
        }

        void CommitEdit()
        {
            int index = IndexField.value;
            int oldIndex = parent.IndexOf(this);
            if (index == oldIndex)
            {
                EditMode(false);
                return;
            }
            
            // 尝试获取父级ListElement
            if (_parentList == null)
            {
                _parentList = this.GetFirstAncestorOfType<ListElement>();
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

        public void InitValue(int index)
        {
            IndexLabel.text = index.ToString();
            IndexField.value = index;

            string propertyPath = $"{Path}[{index}]";

            // 尝试获取父级ListElement
            if (_parentList == null)
            {
                _parentList = this.GetFirstAncestorOfType<ListElement>();
            }

            if (Value == null)
            {
                Value = Drawer.Create(Meta, ViewNode, propertyPath, Action);
                Value.style.flexGrow = 1;
                Insert(0, Value);
                if (HasPort)
                {
                    ChildPorts = this.Query<ChildPort>().ToList();
                }
            }
            else
            {
                RemoveEdges();
                Remove(Value);
                Value = Drawer.Create(Meta, ViewNode, propertyPath, Action);
                Value.style.flexGrow = 1;
                Insert(0, Value);
                if (HasPort)
                {
                    ChildPorts = this.Query<ChildPort>().ToList();
                }
                // 立即添加边连接，不再使用schedule
                AddEdges();
            }

            Value.style.paddingLeft = 40;
        }

        /// <summary>
        /// 立即处理ChildPort连接 - 同步版本
        /// </summary>
        public void ProcessChildPorts()
        {
            if (HasPort)
            {
                ChildPorts = this.Query<ChildPort>().ToList();
                AddEdges();
            }
        }

        public void RemoveEdges()
        {
            if (ChildPorts.Any())
            {
                for (int i = 0; i < ChildPorts.Count; i++)
                {
                    ChildPort childPort = ChildPorts[i];

                    if (childPort.connected)
                    {
                        List<Edge> edges = childPort.connections.ToList();
                        for (int j = 0; j < edges.Count; j++)
                        {
                            edges[j].ParentPort().DisconnectAll();
                            ViewNode.View.RemoveElement(edges[j]);
                        }
                        childPort.DisconnectAll();
                    }
                    ViewNode.ChildPorts.Remove(childPort);
                }
            }
        }

        public void AddEdges()
        {
            if (ChildPorts.Any())
            {
                for (int i = 0; i < ChildPorts.Count; i++)
                {
                    ChildPort childPort = ChildPorts[i];
                    if (!ViewNode.ChildPorts.Contains(childPort))
                    {
                        ViewNode.ChildPorts.Add(childPort);
                    }
                }
            }
        }

        public void RemoveSelf()
        {
            int index = parent.IndexOf(this);
            
            // 清理所有Edge连接
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.ElementAt(i) is ListItem listItem)
                {
                    listItem.RemoveEdges();
                }
            }
            
            // 将ChildPort的子值添加回节点数据
            ViewNode.View.Asset.Data.Nodes.AddRange(ChildPorts.SelectMany(n => n.GetChildValues()));
            
            // 尝试获取父级ListElement
            if (_parentList == null)
            {
                _parentList = this.GetFirstAncestorOfType<ListElement>();
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
                menu.AddItem(new($"▲{I18n.MoveUp}"), false, () =>
                {
                    // 尝试获取父级ListElement
                    if (_parentList == null)
                    {
                        _parentList = this.GetFirstAncestorOfType<ListElement>();
                    }
                    _parentList?.MoveItem(index, index - 1);
                    OnChange?.Invoke();
                });
            }
            if (showMoveDown)
            {
                menu.AddItem(new($"▼{I18n.MoveDown}"), false, () =>
                {
                    // 尝试获取父级ListElement
                    if (_parentList == null)
                    {
                        _parentList = this.GetFirstAncestorOfType<ListElement>();
                    }
                    _parentList?.MoveItem(index, index + 1);
                    OnChange?.Invoke();
                });
            }
            menu.AddItem(new($"✎{I18n.SetIndex}"), false, () =>
            {
                EditMode(true);
            });
            menu.AddSeparator("");
            menu.AddItem(new($"✖{I18n.DeleteItem}"), false, RemoveSelf);
            menu.AddSeparator("");
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            RemoveEdges();
            ChildPorts.Clear();
        }

        /// <summary>
        /// 更新索引显示
        /// </summary>
        public void UpdateIndex(int newIndex)
        {
            IndexLabel.text = newIndex.ToString();
            IndexField.value = newIndex;
        }
    }
}
