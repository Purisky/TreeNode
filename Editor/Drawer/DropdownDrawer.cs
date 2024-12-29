using Newtonsoft.Json.Linq;
using Palmmedia.ReportGenerator.Core.Reporting.Builders.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class DropdownDrawer<T> : BaseDrawer
    {
        public override Type DrawType => typeof(DropdownList<T>);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            DropDownElement<T> dropdownElement = new();
            dropdownElement.Init(memberMeta, node, path, action);
            return new PropertyElement(memberMeta, node, path, this, dropdownElement);
        }
    }
    public class DropDownElement<T> : BaseField<T>
    {
        VisualElement visualInput;
        public VisualElement VisualInput => visualInput;



        public TextElement TextElement;
        VisualElement ArrowElement;
        public MemberMeta Meta;
        public object Data;
        bool Dirty;
        Action OnChange;
        public delegate DropdownList<T> DropdownListGetter();
        DropdownListGetter ListGetter;

        public DropdownList<T> GetList() => ListGetter();


        public ViewNode Node;

        public HashSet<string> Expand;
        public bool Flat;
        public bool Flags;



        public DropDownElement() : base(null, null)
        {
            visualInput = new VisualElement();
            this.Q<VisualElement>(null, "unity-base-field__input").style.display = DisplayStyle.None;
            visualInput.AddToClassList("unity-base-field__input");
            visualInput.AddToClassList("unity-enum-field__input");
            TextElement = new TextElement();
            TextElement.AddToClassList("unity-text-element");
            TextElement.AddToClassList(EnumField.textUssClassName);
            TextElement.pickingMode = PickingMode.Ignore;
            ArrowElement = new VisualElement();
            ArrowElement.AddToClassList(EnumField.arrowUssClassName);
            ArrowElement.pickingMode = PickingMode.Ignore;
            visualInput.Add(TextElement);
            visualInput.Add(ArrowElement);
            style.flexGrow = 1;
            Add(visualInput);
        }
        public void Init(MemberMeta meta,ViewNode node, PropertyPath path, Action action)
        {
            Meta = meta;
            dataSourcePath = path;
            Node = node;
            Data = Node.Data.GetParent(in path);
            ShowInNodeAttribute showInNodeAttribute = Meta.ShowInNode;
            LabelInfoAttribute labelInfo = Meta.LabelInfo;
            if (Meta.Type == typeof(List<T>)) { labelInfo.Hide = true; }
            label = labelInfo.Text;
            labelElement.SetInfo(labelInfo);
            if (Meta.Type.IsSubclassOf(typeof(Enum)))
            {
                Flags = Meta.Type.GetCustomAttribute<FlagsAttribute>() != null;
            }
            Flat = Meta.Dropdown.Flat || Flags;
            if (!Flat)
            {
                Expand = DropMenu.Get(Meta.DropdownKey);
            }

            InitListGetter(meta);
            Dirty = meta.Json;




            OnChange = Meta.OnChangeMethod.GetOnChangeAction(Data) + action;
            SetEnabled(!showInNodeAttribute.ReadOnly);
            T TValue = Node.Data.GetValue<T>(in path);
            SetValueWithoutNotify(TValue);
            DropdownList<T> list = GetList();
            TextElement.text = "Null";
            foreach (var item in list)
            {
                if (item.ValueEquals(TValue))
                {
                    TextElement.text = item.FullText;
                    break;
                }
            }

            SetCallbacks();
        }

        private void InitListGetter(MemberMeta meta)
        {
            DropdownAttribute dropdownAttribute = Meta.Dropdown;
            if (Meta.Type.IsSubclassOf(typeof(Enum)))
            {
                if (string.IsNullOrEmpty(dropdownAttribute.ListGetter))
                {
                    //Debug.Log(typeof(T).Name);
                    ListGetter = new (() =>
                    {
                        return EnumList<T>.GetList(Node.View.Asset.Data.GetType());
                    });



                }
            }
            else
            {
                Type type = meta.DeclaringType;
                MemberInfo member = type.GetMember(dropdownAttribute.ListGetter, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)[0];
                if (member == null)
                {
                    labelElement.text = $"{dropdownAttribute.ListGetter} not found";
                    labelElement.style.color = Color.red;
                }
                if (member.GetValueType() != typeof(DropdownList<T>))
                {
                    labelElement.text = $"{dropdownAttribute.ListGetter} is not {typeof(DropdownList<T>).Name}";
                    labelElement.style.color = Color.red;
                }
                switch (member.MemberType)
                {
                    case MemberTypes.Field:
                        FieldInfo fieldInfo = member as FieldInfo;
                        ListGetter = () => (member as FieldInfo).GetValue(fieldInfo.IsStatic ? null : Data) as DropdownList<T>;
                        break;
                    case MemberTypes.Method:
                        MethodInfo methodInfo = member as MethodInfo;
                        if (methodInfo.IsStatic)
                        {
                            ListGetter = methodInfo.CreateDelegate(typeof(DropdownListGetter)) as DropdownListGetter;
                        }
                        else
                        {
                            ListGetter = methodInfo.CreateDelegate(typeof(DropdownListGetter), Data) as DropdownListGetter;
                        }
                        break;
                    case MemberTypes.Property:
                        PropertyInfo propertyInfo = member as PropertyInfo;
                        MethodInfo getMethod = propertyInfo.GetMethod;
                        if (getMethod.IsStatic)
                        {
                            ListGetter = getMethod.CreateDelegate(typeof(DropdownListGetter)) as DropdownListGetter;
                        }
                        else
                        {
                            ListGetter = getMethod.CreateDelegate(typeof(DropdownListGetter), Data) as DropdownListGetter;
                        }
                        break;
                    default:
                        labelElement.text = $"{dropdownAttribute.ListGetter} not found";
                        labelElement.style.color = Color.red;
                        break;
                }

            }








        }

        public void SetCallbacks()
        {
            visualInput.RegisterCallback<MouseDownEvent>(OnMouseDown);
        }
        VisualElement menu;
        public void OnMouseDown(MouseDownEvent evt)
        {
            //Debug.Log(menu?.parent);
            if (menu != null && menu.parent!=null)
            {
                menu.RemoveFromHierarchy();
                menu = null;
                return;
            }
            DropdownList<T> items = GetList();
            DropMenu dropMenu = new(this);
            for (int i = 0; i < items.Count; i++)
            {
                DropdownItem<T> item = items[i];
                dropMenu.Add(item, () =>
                {
                    Node.Data.SetValue(dataSourcePath, item.Value);
                    SetValueWithoutNotify(item.Value);
                    TextElement.text = item.FullText;
                    if (Dirty)
                    {
                        this.SetDirty();
                    }
                    OnChange?.Invoke();
                });
            }
            dropMenu.BuildMenu();
            menu = dropMenu.DropDown();
            evt.StopPropagation();
        }

        public class DropMenu
        {
            static readonly Dictionary<string, HashSet<string>> Expands = new();

            public static HashSet<string> Get(string key)
            {
                if (Expands.TryGetValue(key, out HashSet<string> expand)) { return expand; }
                Expands[key] = expand = new();
                return expand;
            }


            internal DropDownElement<T> DropDownElement;
            internal Dictionary<string, TreeItem> Dic;
            internal VisualElement m_MenuContainer;

            internal VisualElement m_OuterContainer;

            internal ScrollView m_ScrollView;

            internal List<MenuItem> MenuItems;

            public string Key;
            public HashSet<string> Expand;
            public bool Flat;
            public bool Flags;

            internal class MenuItem
            {
                public DropdownItem<T> Item;
                public TreeItem element;
                public Action action;
            }
            static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("Dropdown");
            public DropMenu(DropDownElement<T> dropDownElement)
            {
                DropDownElement = dropDownElement;
                Key = DropDownElement.Meta.DropdownKey;
                if (DropDownElement.Meta.Type.IsSubclassOf(typeof(Enum)))
                {
                    Flags = DropDownElement.Meta.Type.GetCustomAttribute<FlagsAttribute>() != null;
                }
                Flat = DropDownElement.Meta.Dropdown.Flat || Flags;
                if (!Flat)
                {
                    Expand = Get(Key);
                }
                Dic = new();
                MenuItems = new();
                m_MenuContainer = new VisualElement();
                m_MenuContainer.AddToClassList("unity-base-dropdown");
                m_OuterContainer = new VisualElement();
                m_OuterContainer.AddToClassList("unity-base-dropdown__container-outer");
                m_MenuContainer.Add(m_OuterContainer);
                m_ScrollView = new ScrollView();
                m_ScrollView.AddToClassList("unity-base-dropdown__container-inner");
                m_ScrollView.pickingMode = PickingMode.Position;
                m_ScrollView.contentContainer.focusable = true;
                m_ScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                m_ScrollView.mode = ScrollViewMode.VerticalAndHorizontal;
                m_OuterContainer.hierarchy.Add(m_ScrollView);
                //m_MenuContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
                //m_MenuContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
                m_MenuContainer.style.position = new StyleEnum<Position>(Position.Absolute);
                m_MenuContainer.style.width = DropDownElement.VisualInput.localBound.width;
                m_MenuContainer.style.maxHeight = 500;
                m_MenuContainer.RegisterCallback<FocusOutEvent>(OnFocusOut);
                m_MenuContainer.styleSheets.Add(StyleSheet);
                m_ScrollView.RegisterCallback<WheelEvent>(OnWheel);
            }
            private void OnFocusOut(FocusOutEvent evt)
            {
                Hide();
            }

            public void Hide()
            {
                m_MenuContainer.RemoveFromHierarchy();
            }


            void OnWheel(WheelEvent evt)
            {
                evt.StopPropagation();
            }

            public void BuildMenu()
            {
                string path;
                if (!Flat)
                {
                    HashSet<string> parents = new();
                    List<MenuItem> delete = new();
                    for (int i = 0; i < MenuItems.Count; i++)
                    {
                        if (parents.Contains(MenuItems[i].Item.FullText))
                        {
                            delete.Add(MenuItems[i]);
                            continue;
                        }
                        string[] paths = MenuItems[i].Item.FullText.Split('/');
                        if (paths.Length > 1)
                        {
                            string parentPath = paths[0];
                            for (int j = 0; j < paths.Length - 1; j++)
                            {
                                parents.Add(parentPath);
                                parentPath += "/" + paths[j + 1];
                            }
                        }
                    }
                    for (int i = 0; i < delete.Count; i++)
                    {
                        MenuItems.Remove(delete[i]);
                    }
                    for (int i = 0; i < MenuItems.Count; i++)
                    {
                        path = MenuItems[i].Item.FullText;
                        bool selected = MenuItems[i].Item.ValueEquals(DropDownElement.value);
                        if (selected && DropDownElement.Meta.Dropdown.SkipExist) { continue; }
                        MenuItems[i].element = new(MenuItems[i].Item, selected, MenuItems[i].action);
                        if (!path.Contains('/'))
                        {
                            m_ScrollView.Add(MenuItems[i].element);
                        }
                        else
                        {
                            string parentPath = path[..path.LastIndexOf('/')];
                            TreeItem parent = GetAddPath(parentPath);
                            parent.AddTreeItem(MenuItems[i].element);
                        }
                        if (selected)
                        {
                            MenuItems[i].element.Expand2Root();
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < MenuItems.Count; i++)
                    {
                        bool selected = MenuItems[i].Item.ValueEquals(DropDownElement.value);
                        MenuItems[i].element = new(MenuItems[i].Item, selected, MenuItems[i].action, true);
                        m_ScrollView.Add(MenuItems[i].element);
                    }
                }
            }

            TreeItem GetAddPath(string path)
            {
                if (Dic.TryGetValue(path, out TreeItem item)) { return item; }
                Dic[path] = item = new(path, Expand);
                if (!path.Contains('/'))
                {
                    m_ScrollView.Add(item);
                }
                else
                {
                    string parentPath = path[..path.LastIndexOf('/')];
                    TreeItem parent = GetAddPath(parentPath);
                    parent.AddTreeItem(item);
                }
                return item;

            }



            public void Add(DropdownItem<T> item, Action action)
            {
                MenuItems.Add(new()
                {
                    Item = item,
                    action = action + Hide
                });
            }

            public VisualElement DropDown()
            {
                TreeNodeGraphView graphView = DropDownElement.VisualInput.GetFirstAncestorOfType<TreeNodeGraphView>();
                float scale = 1 / graphView.contentViewContainer.transform.scale.x;
                Rect anchor = DropDownElement.VisualInput.worldBound;
                Rect container = graphView.contentViewContainer.worldBound;
                m_MenuContainer.style.left = (anchor.x - container.x) * scale;
                m_MenuContainer.style.top = (anchor.y - container.y + anchor.height) * scale;
                graphView.contentViewContainer.Add(m_MenuContainer);
                m_MenuContainer.schedule.Execute(() =>
                {
                    m_ScrollView.contentContainer.Focus();
                });
                return m_MenuContainer;
            }
            public class TreeItem : VisualElement
            {
                public string Path;
                public string Text;
                public List<TreeItem> List;
                public Action Action;

                VisualElement GroupElement;
                VisualElement Arrow;
                Label Label;

                public bool Selected;

                public HashSet<string> Expands;

                public TreeItem(DropdownItem<T> item, bool selected, Action action, bool flat = false)
                {
                    VisualElement labelElement = Init(item.FullText, flat ? item.FullText : item.Text);
                    Label.style.color = item.TextColor;
                    if (item.IconPath != null)
                    {
                        Image icon = new() { name = "icon", image = IconUtil.Get(item.IconPath) };
                        labelElement.Insert(0, icon);
                    }
                    Selected = selected;
                    if (Selected)
                    {
                        labelElement.AddToClassList("selected");
                    }
                    labelElement.RegisterCallback<PointerDownEvent>((evt) => action());
                    if (DropdownItem<T>.isValueType)
                    {
                        tooltip = item.Value.ToString();
                    }
                }

                public TreeItem(string path, HashSet<string> expands)
                {
                    Expands = expands;
                    VisualElement labelElement = Init(path);
                    List = new();
                    GroupElement = new() { name = "group" };
                    //GroupElement.style.paddingLeft = 5;
                    Add(GroupElement);
                    Arrow = new();
                    Arrow.AddToClassList("unity-enum-field__arrow");
                    //Arrow.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(-90)));
                    Arrow.style.right = 0;
                    Arrow.style.position = Position.Absolute;
                    labelElement.Add(Arrow);
                    labelElement.RegisterCallback<PointerDownEvent>((evt) =>
                    {
                        bool display = GroupElement.style.display == DisplayStyle.Flex;
                        Fold(!display);
                    });
                    InternalFold(Expands.Contains(path));
                }

                void Fold(bool active)
                {
                    if (active)
                    {
                        Expands.Add(Path);
                    }
                    else
                    {
                        Expands.Remove(Path);
                    }
                    InternalFold(active);
                }
                void InternalFold(bool active)
                {
                    Arrow.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(active ? 0 : -90)));
                    GroupElement.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                }


                public VisualElement Init(string path, string text = null)
                {
                    Path = path;
                    string[] paths = Path.Split('/');
                    Text = text ?? paths.Last();
                    VisualElement labelElement = new() { name = "labels" };
                    labelElement.style.flexDirection = FlexDirection.Row;
                    labelElement.style.paddingLeft = 5 * paths.Length - 5;
                    Add(labelElement);
                    Label = new() { text = Text };
                    labelElement.Add(Label);
                    return labelElement;
                }



                public void AddTreeItem(TreeItem item)
                {
                    List.Add(item);
                    GroupElement.Add(item);
                }

                public bool ChildDisplay
                {
                    get
                    {
                        if (List == null)
                        {
                            return Display;
                        }
                        for (int i = 0; i < List.Count; i++)
                        {
                            if (List[i].Display) { return true; }
                        }
                        return false;
                    }
                }

                public bool Display
                {
                    get => style.display == DisplayStyle.Flex;
                    set => style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                }


                public void SetDisplay(bool display)
                {
                    if (display == Display) { return; }
                    Display = display;
                    TreeItem treeItem = GetFirstAncestorOfType<TreeItem>();
                    treeItem?.SetDisplay(treeItem.ChildDisplay);
                }
                public void Expand2Root()
                {
                    if (Arrow != null)
                    {
                        InternalFold(true);
                    }
                    TreeItem treeItem = GetFirstAncestorOfType<TreeItem>();
                    treeItem?.Expand2Root();
                }


            }

        }

    }






    public class EnumDrawer<T> : BaseDrawer
    {
        public override Type DrawType => typeof(T);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            DropDownElement<T> dropdownElement = new();
            dropdownElement.Init(memberMeta, node, path, action);
            return new PropertyElement(memberMeta, node, path, this, dropdownElement);




            //ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            //LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            //BaseField<Enum> field = CreateEnumField(memberMeta.Type, labelInfo);
            //field.dataSourcePath = path;
            //object value = node.Data.GetValue<object>(path);
            //field.SetValueWithoutNotify((Enum)value);
            //object parent = node.Data.GetParent(in path);
            //action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            //bool dirty = memberMeta.Json;
            //field.RegisterValueChangedCallback(evt =>
            //{
            //    node.Data.SetValue(in path, evt.newValue);
            //    if (dirty)
            //    {
            //        field.SetDirty();
            //    }
            //    action?.Invoke();
            //});
            //field.SetEnabled(!showInNode.ReadOnly);
            //return new PropertyElement(memberMeta, node, path, this, field);
        }


        //DropDownElement<Enum> CreateEnumField(Type enumType, LabelInfoAttribute labelInfo)
        //{
        //    DropDownElement < Enum > field =  = 


        //    if (!enumType.IsDefined(typeof(FlagsAttribute)))
        //    {
        //        EnumField field = new(labelInfo.Text, Enum.GetValues(enumType).GetValue(0) as Enum);
        //        field.style.flexGrow = 1;
        //        field.labelElement.SetInfo(labelInfo);
        //        return field;
        //    }
        //    else
        //    {
        //        EnumFlagsField enumFlags = new(labelInfo.Text, Enum.GetValues(enumType).GetValue(0) as Enum);
        //        enumFlags.style.flexGrow = 1;
        //        enumFlags.labelElement.SetInfo(labelInfo);
        //        return enumFlags;
        //    }
        //}
    }








}
