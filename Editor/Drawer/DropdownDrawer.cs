using System;
using System.Collections.Generic;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor;
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
            dropdownElement.Init(memberMeta, node.Data, path, action);
            DropdownList<T> dropdownItems = dropdownElement.GetList();
            T TValue = node.Data.GetValue<T>(in path);
            dropdownElement.TextElement.text = "Null";
            foreach (var item in dropdownItems)
            {
                if (item.ValueEquals(TValue))
                {
                    dropdownElement.TextElement.text = item.Text;
                    break;
                }
            }
            dropdownElement.SetCallbacks();
            return new PropertyElement(memberMeta, node, path, this, dropdownElement);
        }
    }
    public class DropDownElement<T> : BaseField<T>
    {
        VisualElement visualInput;
        public TextElement TextElement;
        VisualElement ArrowElement;
        public MemberMeta Meta;
        public object Data;
        bool Dirty;
        Action OnChange;
        public delegate DropdownList<T> DropdownListGetter();
        DropdownListGetter ListGetter;

        public DropdownList<T> GetList() => ListGetter();

        public ViewNode  Node=> GetFirstAncestorOfType< ViewNode >();

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
        public void Init(MemberMeta meta, JsonNode data, PropertyPath path, Action action)
        {
            Meta = meta;
            dataSourcePath = path;
            Data = data.GetParent(in path);
            ShowInNodeAttribute showInNodeAttribute = Meta.ShowInNode;
            LabelInfoAttribute labelInfo = Meta.LabelInfo;
            if (Meta.Type == typeof(List<T>)) { labelInfo.Hide = true; }
            label = labelInfo.Text;
            labelElement.SetInfo(labelInfo);
            DropdownAttribute dropdownAttribute = Meta.Dropdown;
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
                    MethodInfo getMethod = propertyInfo.GetGetMethod();
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
            Dirty = member.SerializeByJsonDotNet();
            OnChange = Meta.OnChangeMethod.GetOnChangeAction(Data) + action;
            SetEnabled(!showInNodeAttribute.ReadOnly);
        }



        public void SetCallbacks()
        {
            visualInput.RegisterCallback<MouseDownEvent>(OnMouseDown);
        }
        public void OnMouseDown(MouseDownEvent evt)
        {
            DropdownList<T> items = GetList();
            GenericMenu menu = new();
            foreach (var item in items)
            {
                bool equal = item.ValueEquals(value);
                menu.AddItem(new(item.FullText), equal, () =>
                {
                    if (!equal)
                    {
                        Node.Data.SetValue(dataSourcePath, item.Value);
                        TextElement.text = item.Text;
                        if (Dirty)
                        {
                            this.SetDirty();
                        }
                        OnChange?.Invoke();
                    }
                });
            }
            Vector2 pos = worldBound.position;
            pos.y += worldBound.size.y;
            menu.DropDown(new Rect(pos, Vector2.zero));
            evt.StopPropagation();
        }

    }







    public class EnumDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(Enum);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            BaseField<Enum> field = CreateEnumField(memberMeta.Type, labelInfo);
            field.dataSourcePath = path;
            object value = node.Data.GetValue<object>(path);
            field.SetValueWithoutNotify((Enum)value);
            object parent = node.Data.GetParent(in path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            bool dirty = memberMeta.Json;
            field.RegisterValueChangedCallback(evt =>
            {
                node.Data.SetValue(in path, evt.newValue);
                if (dirty)
                {
                    field.SetDirty();
                }
                action?.Invoke();
            });
            field.SetEnabled(!showInNode.ReadOnly);
            return new PropertyElement(memberMeta, node, path, this, field);
        }


        BaseField<Enum> CreateEnumField(Type enumType, LabelInfoAttribute labelInfo)
        {
            if (!enumType.IsDefined(typeof(FlagsAttribute)))
            {
                EnumField field = new(labelInfo.Text, Enum.GetValues(enumType).GetValue(0) as Enum);
                field.style.flexGrow = 1;
                field.labelElement.SetInfo(labelInfo);
                return field;
            }
            else
            {
                EnumFlagsField enumFlags = new(labelInfo.Text, Enum.GetValues(enumType).GetValue(0) as Enum);
                enumFlags.style.flexGrow = 1;
                enumFlags.labelElement.SetInfo(labelInfo);
                return enumFlags;
            }
        }
    }

    public class EnumElement : BaseField<Enum>
    {




        public EnumElement() : base(null, null)
        {
        }



    }




}
