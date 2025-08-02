using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static TreeNode.ReflectionExtensions;

namespace TreeNode.Editor
{
    public class DropdownDrawer<T> : BaseDrawer
    {
        public override Type DrawType => typeof(DropdownList<T>);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            DropdownElement<T> dropdownElement;
            if (memberMeta.Dropdown.Flat)
            {
                dropdownElement = new FlatDropdownElement<T>();
            }
            else
            {
                dropdownElement = new TreeDropDownElement<T>();
            }
            dropdownElement.Init(memberMeta, node, path, action);
            return new PropertyElement(memberMeta, node, path, this, dropdownElement);
        }
    }
    public abstract class DropdownElement<T> : BaseField<T>, IValidator
    {
        protected VisualElement visualInput;
        public VisualElement VisualInput => visualInput;
        public TextElement TextElement;
        protected VisualElement ArrowElement;
        public MemberMeta Meta;
        public string Path;
        public object Data;
        protected bool Dirty;
        protected Action OnChange;
        protected MemberGetter<DropdownList<T>> ListGetter;
        public DropdownList<T> GetList() => ListGetter(Node.View.Asset.Data.GetType());
        public ViewNode Node;
        public DropdownElement() : base(null, null)
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
        public virtual void Init(MemberMeta meta,ViewNode node, string path, Action action)
        {
            Meta = meta;
            Path =path;
            Node = node;
            Data = Node.Data.GetParent(path);
            ShowInNodeAttribute showInNodeAttribute = Meta.ShowInNode;
            LabelInfoAttribute labelInfo = Meta.LabelInfo;
            if (Meta.Type == typeof(List<T>)) { labelInfo.Hide = true; }
            label = labelInfo.Text;
            labelElement.SetInfo(labelInfo);
            InitListGetter(meta);
            Dirty = meta.Json;
            OnChange = Meta.OnChangeMethod.GetOnChangeAction(Data) + action;
            SetEnabled(!showInNodeAttribute.ReadOnly);
            T TValue = Node.Data.GetValue<T>(path);
            SetValueWithoutNotify(TValue);
            TextElement.text = GetValueText(GetList(), TValue);
            SetCallbacks();
        }
        protected virtual string GetValueText(DropdownList<T> items, T Value)
        {
            foreach (var item in items)
            {
                if (item.ValueEquals(Value))
                {
                    return item.FullText;
                }
            }
            return "Null";

        }

        public bool Validate(out string msg)
        {
            DropdownList<T> list = GetList();
            T value = Node.Data.GetValue<T>(Path);
            msg = $"{Path}:{Meta.Type.Name}({value}) must in the list[{string.Join(',', list.Select(n => n.Value))}]";
            foreach (var item in list)
            {
                if (item.ValueEquals(value))
                {
                    return true;
                }
            }
            return false;
        }



        protected void InitListGetter(MemberMeta meta)
        {
            DropdownAttribute dropdownAttribute = Meta.Dropdown;
            if (Meta.Type.IsSubclassOf(typeof(Enum)))
            {
                if (string.IsNullOrEmpty(dropdownAttribute.ListGetter))
                {
                    ListGetter = (Type type) => EnumList<T>.GetList(Node.View.Asset.Data.GetType());
                }
                else
                {
                    InitializeListGetterForEnum(meta, dropdownAttribute);
                }
            }
            else
            {
                InitializeListGetterForNonEnum(meta, dropdownAttribute);
            }
        }

        private void InitializeListGetterForEnum(MemberMeta meta, DropdownAttribute dropdownAttribute)
        {
            MemberInfo member = GetMemberInfo(meta, dropdownAttribute.ListGetter);
            if (member == null) return;

            Type memberType = member.GetValueType();
            if (memberType == typeof(DropdownList<T>))
            {
                ListGetter = member.GetMemberGetter<DropdownList<T>>(Data);
            }
            else if (memberType == typeof(List<T>))
            {
                ListGetter = (Type type) => EnumList<T>.GetList(Node.View.Asset.Data.GetType(), member.GetMemberGetter<List<T>>(Data).Invoke(type));
            }
            else
            {
                SetLabelError($"{dropdownAttribute.ListGetter} is not {typeof(DropdownList<T>).Name} or {typeof(List<T>).Name}");
            }
        }

        private void InitializeListGetterForNonEnum(MemberMeta meta, DropdownAttribute dropdownAttribute)
        {
            MemberInfo member = GetMemberInfo(meta, dropdownAttribute.ListGetter);
            if (member == null) return;

            if (member.GetValueType() != typeof(DropdownList<T>))
            {
                SetLabelError($"{dropdownAttribute.ListGetter} is not {typeof(DropdownList<T>).Name}");
                return;
            }

            ListGetter = member.GetMemberGetter<DropdownList<T>>(Data);
        }

        private MemberInfo GetMemberInfo(MemberMeta meta, string listGetter)
        {
            Type type = meta.DeclaringType;
            MemberInfo member = type.GetMember(listGetter, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
            if (member == null)
            {
                SetLabelError($"{listGetter} not found");
            }
            return member;
        }

        private void SetLabelError(string errorMessage)
        {
            labelElement.text = errorMessage;
            labelElement.style.color = Color.red;
        }





        public void SetCallbacks()
        {
            visualInput.RegisterCallback<MouseDownEvent>(OnMouseDown);
        }
        protected VisualElement menu;
        public virtual void OnMouseDown(MouseDownEvent evt)
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
                    if (Dirty)
                    { 
                        T oldValue = Node.Data.GetValue<T>(Path);
                        Node.RecordField(Path, oldValue, item.Value);
                    }
                    Node.Data.SetValue(Path, item.Value);
                    SetValueWithoutNotify(item.Value);
                    TextElement.text = item.FullText;
                    OnChange?.Invoke();
                    Node.PopupText();
                });
            }
            dropMenu.BuildMenu();
            menu = dropMenu.DropDown();
            evt.StopPropagation();
        }

        protected class DropMenu
        {
            internal DropdownElement<T> DropDownElement;
            internal Dictionary<string, TreeItem> Dic;
            internal VisualElement m_MenuContainer;

            internal VisualElement m_OuterContainer;

            internal ScrollView m_ScrollView;

            internal List<MenuItem> MenuItems;

            //public string Key;
            internal class MenuItem
            {
                public DropdownItem<T> Item;
                public TreeItem element;
                public Action action;
            }
            static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("Dropdown");
            public DropMenu(DropdownElement<T> dropDownElement)
            {
                DropDownElement = dropDownElement;
                //Key = DropDownElement.Meta.DropdownKey;
                //if (DropDownElement.Meta.Type.IsSubclassOf(typeof(Enum)))
                //{
                //    Flags = DropDownElement.Meta.Type.GetCustomAttribute<FlagsAttribute>() != null;
                //}
                //Flat = DropDownElement.Meta.Dropdown.Flat || Flags;
                //if (!Flat)
                //{
                //    Expand = Get(Key);
                //}
                Dic = new();
                MenuItems = new();
                m_MenuContainer = new VisualElement();
                m_MenuContainer.AddToClassList("unity-base-dropdown");
                m_OuterContainer = new VisualElement();
                m_OuterContainer.style.borderBottomLeftRadius = 6;
                m_OuterContainer.style.borderBottomRightRadius = 6;
                m_OuterContainer.AddToClassList("unity-base-dropdown__container-outer");
                m_MenuContainer.Add(m_OuterContainer);
                m_ScrollView = new ScrollView();
                m_ScrollView.AddToClassList("unity-base-dropdown__container-inner");
                m_ScrollView.pickingMode = PickingMode.Position;
                m_ScrollView.contentContainer.focusable = true;
                m_ScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                m_ScrollView.mode = ScrollViewMode.VerticalAndHorizontal;
                m_OuterContainer.hierarchy.Add(m_ScrollView);
                m_OuterContainer.style.flexGrow = 1;
                m_ScrollView.style.flexGrow = 1;
                //m_MenuContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
                //m_MenuContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
                m_MenuContainer.style.position = new StyleEnum<Position>(Position.Absolute);
                m_MenuContainer.style.width = DropDownElement.VisualInput.localBound.width;
                m_OuterContainer.style.width = DropDownElement.VisualInput.localBound.width;
                m_ScrollView.style.width = DropDownElement.VisualInput.localBound.width;
                //Debug.Log(m_MenuContainer.style.width);
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

            public virtual void BuildMenu() {
                Debug.Log(" virtual BuildMenu");
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
                    icon.style.height = 16;
                    icon.style.width = 16;
                    icon.style.flexGrow = 0;

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

    public class FlagsDropDownElement<T> : FlatDropdownElement<T> where T:Enum
    {
        public T Nothing;
        public T Everything;
        public override void Init(MemberMeta meta, ViewNode node, string path, Action action)
        {
            base.Init(meta, node, path, action);
            Type underlyingType = Enum.GetUnderlyingType(typeof(T));
            foreach (var item in GetList())
            {
                Everything = BitwiseOr(underlyingType, Everything, item.Value);
            }
            Nothing = (T)Enum.ToObject(typeof(T), 0);
        }
        public static T BitwiseOr(Type underlyingType, T A, T B)
        {
            if (underlyingType == typeof(int))
            {
                int result = Convert.ToInt32(A) | Convert.ToInt32(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(uint))
            {
                uint result = Convert.ToUInt32(A) | Convert.ToUInt32(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(long))
            {
                long result = Convert.ToInt64(A) | Convert.ToInt64(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(ulong))
            {
                ulong result = Convert.ToUInt64(A) | Convert.ToUInt64(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(short))
            {
                short result = (short)(Convert.ToInt16(A) | Convert.ToInt16(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(ushort))
            {
                ushort result = (ushort)(Convert.ToUInt16(A) | Convert.ToUInt16(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(byte))
            {
                byte result = (byte)(Convert.ToByte(A) | Convert.ToByte(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(sbyte))
            {
                sbyte result = (sbyte)(Convert.ToSByte(A) | Convert.ToSByte(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else
            {
                throw new ArgumentException("Unsupported enum underlying type.");
            }
        }


        public static T BitwiseAndNot(Type underlyingType, T A, T B)
        {
            if (underlyingType == typeof(int))
            {
                int result = Convert.ToInt32(A) & ~Convert.ToInt32(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(uint))
            {
                uint result = Convert.ToUInt32(A) & ~Convert.ToUInt32(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(long))
            {
                long result = Convert.ToInt64(A) & ~Convert.ToInt64(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(ulong))
            {
                ulong result = Convert.ToUInt64(A) & ~Convert.ToUInt64(B);
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(short))
            {
                short result = (short)(Convert.ToInt16(A) & ~Convert.ToInt16(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(ushort))
            {
                ushort result = (ushort)(Convert.ToUInt16(A) & ~Convert.ToUInt16(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(byte))
            {
                byte result = (byte)(Convert.ToByte(A) & ~Convert.ToByte(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else if (underlyingType == typeof(sbyte))
            {
                sbyte result = (sbyte)(Convert.ToSByte(A) & ~Convert.ToSByte(B));
                return (T)Enum.ToObject(typeof(T), result);
            }
            else
            {
                throw new ArgumentException("Unsupported enum underlying type.");
            }
        }


        public override void OnMouseDown(MouseDownEvent evt)
        {
            //Debug.Log(menu?.parent);
            if (menu != null && menu.parent != null)
            {
                menu.RemoveFromHierarchy();
                menu = null;
                return;
            }
            DropdownList<T> items = GetList();
            DropMenu dropMenu = new(this);
            dropMenu.Add(new DropdownItem<T>(I18n.EnumNothing, Nothing), () =>
            {
                if (Dirty)
                {
                    T oldValue = Node.Data.GetValue<T>(Path);
                    Node.RecordField(Path, oldValue, Nothing);
                }

                Node.Data.SetValue(Path, Nothing);
                SetValueWithoutNotify(Nothing);
                TextElement.text = I18n.EnumNothing;
                dropMenu.UpdateSelection();
                OnChange?.Invoke();
                Node.PopupText();
            });
            for (int i = 0; i < items.Count; i++)
            {
                DropdownItem<T> item = items[i];
                dropMenu.Add(item, () =>
                {
                    T value = Node.Data.GetValue<T>(Path);
                    if (value.HasFlag(item.Value))
                    {
                        value = BitwiseAndNot(Enum.GetUnderlyingType(typeof(T)), value, item.Value);
                    }
                    else
                    {
                        value = BitwiseOr(Enum.GetUnderlyingType(typeof(T)), value, item.Value);
                    }
                    if (Dirty)
                    {
                        T oldValue = Node.Data.GetValue<T>(Path);
                        Node.RecordField(Path, oldValue, value);
                    }
                    Node.Data.SetValue(Path, value);
                    SetValueWithoutNotify(value);
                    TextElement.text = GetValueText(GetList(), value);
                    dropMenu.UpdateSelection();
                    OnChange?.Invoke();
                    Node.PopupText();
                });
            }
            dropMenu.Add(new DropdownItem<T>(I18n.EnumEverything, Everything), () =>
            {
                if (Dirty)
                {
                    T oldValue = Node.Data.GetValue<T>(Path);
                    Node.RecordField(Path, oldValue, Everything);
                }
                Node.Data.SetValue(Path, Everything);
                SetValueWithoutNotify(Everything);
                TextElement.text = I18n.EnumEverything;
                dropMenu.UpdateSelection();
                OnChange?.Invoke();
                Node.PopupText();
            });
            dropMenu.BuildMenu();
            menu = dropMenu.DropDown();
            evt.StopPropagation();
        }
        protected override string GetValueText(DropdownList<T> items, T Value)
        {
            List<DropdownItem<T>> flags = GetFlagsValue (items, Value);
            if (flags.Count == 0)   
            {
                return I18n.EnumNothing;
            }
            if (flags.Count == items.Count)
            {
                return I18n.EnumEverything;
            }
            return string.Join(",", flags.Select(x => x.FullText));
        }
        protected List<DropdownItem<T>> GetFlagsValue(DropdownList<T> items, T Value)
        {
            List<DropdownItem<T>> flags = new();
            foreach (var item in items)
            {
                if (Value.HasFlag(item.Value))
                {
                    flags.Add(item);
                }
            }
            return flags;
        }


        protected new class DropMenu : DropdownElement<T>.DropMenu
        {
            public DropMenu(FlatDropdownElement<T> dropDownElement) : base(dropDownElement)
            {
            }
            public override void BuildMenu()
            {
                int count = 0;
                MenuItems[0].element = new(MenuItems[0].Item, false, MenuItems[0].action, true);
                m_ScrollView.Add(MenuItems[0].element);
                for (int i = 1; i < MenuItems.Count-1; i++)
                {
                    bool selected = DropDownElement.value.HasFlag(MenuItems[i].Item.Value);
                    if (selected) { count++; }
                    MenuItems[i].element = new(MenuItems[i].Item, selected, MenuItems[i].action, true);
                    m_ScrollView.Add(MenuItems[i].element);
                }
                MenuItems[^1].element = new(MenuItems[^1].Item, false, MenuItems[^1].action, true);
                m_ScrollView.Add(MenuItems[^1].element);
                if (count == 0)
                {
                    MenuItems[0].element.AddToClassList("selected");
                }
                if (count == MenuItems.Count - 2)
                {
                    MenuItems[^1].element.AddToClassList("selected");
                }
            }
            public void UpdateSelection()
            { 
                int count = 0;
                for (int i = 1; i < MenuItems.Count - 1; i++)
                {
                    bool selected = DropDownElement.value.HasFlag(MenuItems[i].Item.Value);
                    if (selected) { count++; }
                    if (selected)
                    {
                        MenuItems[i].element.AddToClassList("selected");
                    }
                    else
                    {
                        MenuItems[i].element.RemoveFromClassList("selected");
                    }
                }
                if (count == 0)
                {
                    MenuItems[0].element.AddToClassList("selected");
                }
                else
                {
                    MenuItems[0].element.RemoveFromClassList("selected");
                }
                if (count == MenuItems.Count - 2)
                {
                    MenuItems[^1].element.AddToClassList("selected");
                }
                else
                {
                    MenuItems[^1].element.RemoveFromClassList("selected");
                }
            }
        }
    }
    public class TreeDropDownElement<T> : DropdownElement<T>
    {
        public override void OnMouseDown(MouseDownEvent evt)
        {
            //Debug.Log(menu?.parent);
            if (menu != null && menu.parent != null)
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
                    if (Dirty)
                    {
                        T oldValue = Node.Data.GetValue<T>(Path);
                        Node.RecordField(Path, oldValue, item.Value);
                    }
                    Node.Data.SetValue(Path, item.Value);
                    SetValueWithoutNotify(item.Value);
                    TextElement.text = item.FullText;
                    OnChange?.Invoke();
                    Node.PopupText();
                });
            }
            dropMenu.BuildMenu();
            menu = dropMenu.DropDown();
            evt.StopPropagation();
        }



        protected new class DropMenu : DropdownElement<T>.DropMenu
        {
            public HashSet<string> Expand;
            public DropMenu(TreeDropDownElement<T> dropDownElement) : base(dropDownElement)
            {
                Expand =DropdownExpand.Get(dropDownElement.Meta.DropdownKey);
            }

            public override void BuildMenu()
            {
                //Debug.Log("BuildMenu");
                string path;
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
        }
    }
    public class FlatDropdownElement<T> : DropdownElement<T>
    {
        public override void OnMouseDown(MouseDownEvent evt)
        {
            //Debug.Log(menu?.parent);
            if (menu != null && menu.parent != null)
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
                    if (Dirty)
                    {
                        T oldValue = Node.Data.GetValue<T>(Path);
                        Node.RecordField(Path, oldValue, item.Value);
                    }
                    Node.Data.SetValue(Path, item.Value);
                    SetValueWithoutNotify(item.Value);
                    TextElement.text = item.FullText;
                    OnChange?.Invoke();
                    Node.PopupText();
                });
            }
            dropMenu.BuildMenu();
            menu = dropMenu.DropDown();
            evt.StopPropagation();
        }
        protected new class DropMenu : DropdownElement<T>.DropMenu
        {
            public DropMenu(FlatDropdownElement<T> dropDownElement) : base(dropDownElement)
            {
            }
            public override void BuildMenu()
            {

                for (int i = 0; i < MenuItems.Count; i++)
                {
                    bool selected = MenuItems[i].Item.ValueEquals(DropDownElement.value);
                    MenuItems[i].element = new(MenuItems[i].Item, selected, MenuItems[i].action, true);
                    m_ScrollView.Add(MenuItems[i].element);
                }
            }
        }
    }


    public class EnumDrawer<T> : BaseDrawer where T : Enum
    {
        public override Type DrawType => typeof(T);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            DropdownElement<T> dropdownElement;
            if (memberMeta.Type.GetCustomAttribute<FlagsAttribute>() != null)
            {
                dropdownElement = new FlagsDropDownElement<T>();
            }
            else if (memberMeta.Dropdown.Flat)
            {
                dropdownElement = new FlatDropdownElement<T>();
            }
            else
            {
                dropdownElement = new TreeDropDownElement<T>();
            }
            dropdownElement.Init(memberMeta, node, path, action);
            return new PropertyElement(memberMeta, node, path.ToString(), this, dropdownElement);
        }
    }

    public class DropdownExpand
    {
        static readonly Dictionary<string, HashSet<string>> Expands = new();
        public static HashSet<string> Get(string key)
        {
            if (Expands.TryGetValue(key, out HashSet<string> expand)) { return expand; }
            Expands[key] = expand = new();
            return expand;
        }
    }






}
