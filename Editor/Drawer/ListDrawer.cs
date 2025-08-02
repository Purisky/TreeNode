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
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            ListView listView = NewListView(labelInfo);
            listView.dataSourcePath =new( path);
            Button addBtn = NewAddBtn();
            listView.Q<TextField>("unity-list-view__size-field").Add(addBtn);
            IList list = node.Data.GetValue<IList>(path);
            listView.viewController.itemsSource = list;
            listView.bindItem = (element, i) =>
            {
                //Debug.Log("bindItem");
                ListItem listItem = (ListItem)element;
                listItem.InitValue(i);
            };
            Type gType = memberMeta.Type.GetGenericArguments()[0];
            bool dirty = memberMeta.Json;
            object parent = node.Data.GetParent( path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            addBtn.clicked += () =>
            {
                if (listView.viewController.itemsSource is null)
                {
                    IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(gType));
                    node.Data.SetValue(path, list);
                    listView.viewController.itemsSource = list;
                }
                if (gType.GetConstructor(Type.EmptyTypes) == null)
                {
                    listView.itemsSource.Add(RuntimeHelpers.GetUninitializedObject(gType));
                }
                else
                {
                    listView.itemsSource.Add(Activator.CreateInstance(gType));
                }
                if (dirty)
                {
                    listView.SetDirty();
                }
                action?.Invoke();
                listView.RefreshItems();
                listView.Focus();
            };
            DropdownAttribute dropdownAttribute = memberMeta.Dropdown;
            BaseDrawer baseDrawer = dropdownAttribute != null ? DrawerManager.GetDropdownDrawer(gType) : DrawerManager.Get(gType);
            listView.userData = 0;
            if (baseDrawer != null)
            {
                if (baseDrawer is ComplexDrawer complex)
                {
                    listView.fixedItemHeight = complex.Height;
                    if (complex.HasPort)
                    {
                        listView.makeHeader = () =>
                        {
                            VisualElement visualElement = new();
                            visualElement.style.height = 22;
                            visualElement.style.paddingLeft = 4;
                            visualElement.style.paddingTop = 4;
                            visualElement.style.alignSelf = Align.FlexStart;
                            visualElement.Add(CreateLabel(labelInfo));
                            return visualElement;
                        };
                    }
                }
                
                // 注册ListView到TreeNodeGraphView的初始化跟踪器
                if (node.View is TreeNodeGraphView graphView)
                {
                    graphView.RegisterListViewForTracking(listView);
                }
                
                listView.makeItem = () =>
                {
                    ListItem listItem = new(memberMeta, node, baseDrawer, path, dirty, action);
                    
                    // 检查是否所有Item都已创建 - 智能初始化检测
                    var currentItemCount = listView.Query<ListItem>().ToList().Count + 1;
                    var totalItemCount = listView.viewController.itemsSource?.Count ?? 0;
                    
                    if (currentItemCount >= totalItemCount && totalItemCount > 0)
                    {
                        listView.userData = true; // 标记ListView已完全初始化
                        
                        // 通知TreeNodeGraphView ListView已准备就绪
                        if (node.View is TreeNodeGraphView treeGraphView)
                        {
                            treeGraphView.MarkListViewAsReady(listView);
                        }
                        
                        UnityEngine.Debug.Log($"ListView完全初始化: {totalItemCount}个项目创建完成");
                    }
                    
                    return listItem;
                };
            }
            listView.SetEnabled(!showInNode.ReadOnly);
            listView.RefreshItems();
            return new PropertyElement(memberMeta, node, path, this, listView);
        }


        ListView NewListView(LabelInfoAttribute labelInfo)
        {
            ListView listView = new();
            listView.style.flexGrow = 1;
            listView.showBorder = true;
            listView.showAlternatingRowBackgrounds = AlternatingRowBackground.All;
            listView.showFoldoutHeader = true;
            listView.showBoundCollectionSize = true;
            Foldout foldout = listView.Q<Foldout>("unity-list-view__foldout-header");
            foldout.Q<Toggle>().ElementAt(0).Add(CreateLabel(labelInfo));
            if (listView.viewController == null)
            {
                listView.SetViewController(new ListViewController());
            }
            return listView;
        }
        Button NewAddBtn()
        {
            Button addBtn = new() { name = "addbtn", text = "+" };
            addBtn.style.height = 17;
            addBtn.style.width = 17;
            addBtn.style.marginBottom = 0;
            addBtn.style.marginTop = 0;
            addBtn.style.paddingBottom = 4;
            addBtn.style.fontSize = 18;
            return addBtn;
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
                ListView listView = GetFirstAncestorOfType<ListView>();
                if (index >= listView.itemsSource.Count)
                {
                    IndexField.value = listView.itemsSource.Count - 1;
                    return;
                }
            });
            IndexField.RegisterCallback<BlurEvent>((evt) => CommitEdit());
            ChildPorts = new();
        }


        void CommitEdit()
        {
            int index = IndexField.value;
            ListView listView = GetFirstAncestorOfType<ListView>();
            int oldIndex = parent.IndexOf(this);
            if (index == oldIndex)
            {
                EditMode(false);
                return;
            }
            Move(listView, oldIndex, index);
        }



        public void EditMode(bool edit)
        {
            if (edit)
            {
                AddToClassList("editmode");
                //IndexField.SetEnabled(true);
                IndexField.schedule.Execute(() =>
                {
                    IndexField.Focus(); ;
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

            string propertyPath =$"{Path}[{index}]";


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
                Insert(0,Value);
                if (HasPort)
                {
                    ChildPorts = this.Query<ChildPort>().ToList();
                }
                schedule.Execute(AddEdges);
            }

            Value.style.paddingLeft = 40;




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
            ListView listView = GetFirstAncestorOfType<ListView>();
            for (int i = 0; i < parent.childCount; i++)
            {
                ListItem listItem = parent.ElementAt(i) as ListItem;
                listItem.RemoveEdges();
            }
            ViewNode.View.Asset.Data.Nodes.AddRange(ChildPorts.SelectMany(n => n.GetChildValues()));
            int index = parent.IndexOf(this);
            listView.viewController.RemoveItem(index);
            listView.Focus();
            OnChange?.Invoke();

        }







        public void AddItemsToMenu(GenericMenu menu)
        {
            int index = parent.IndexOf(this);
            bool showMoveUp = index > 0;
            bool showMoveDown = index < parent.childCount - 1;
            ListView listView = GetFirstAncestorOfType<ListView>();

            if (showMoveUp)
            {
                menu.AddItem(new($"▲{I18n.MoveUp}"), false, () =>
                {
                    Move(listView, index, index-1);
                });
            }
            if (showMoveDown)
            {
                menu.AddItem(new($"▼{I18n.MoveDown}"), false, () =>
                {
                    Move(listView, index, index + 1);
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

        void Move(ListView listView, int index,int newIndex)
        {
            listView.viewController.Move(index, newIndex);
            listView.Focus();
            OnChange?.Invoke();
        }





    }


}
