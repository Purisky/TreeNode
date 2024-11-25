using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public abstract class TextValueFieldDrawer<T, Tv> : BaseDrawer where T : TextInputBaseField<Tv>, new()
    {
        public override Type DrawType => typeof(Tv);

        //public override PropertyElement Create(MemberInfo memberInfo, ViewNode node, PropertyPath path, Action action)
        //{
        //    ShowInNodeAttribute showInNode = memberInfo?.GetCustomAttribute<ShowInNodeAttribute>()??new();
        //    LabelInfoAttribute labelInfo = memberInfo?.GetLabelInfo()??new();
        //    T field = new()
        //    {
        //        name = labelInfo.Text,
        //    };
        //    field.style.flexGrow = 1;
        //    field.style.height = 20;
        //    field.Insert(0, CreateLabel(labelInfo));

        //    //Debug.Log(node.Data.GetType());
        //    //Debug.Log(typeof(Tv).Name);
        //    //Json.Log(node.Data.GetValue<object>(in path));
        //    Tv value = node.Data.GetValue<Tv>(in path);
        //    field.SetValueWithoutNotify(value);
        //    object parent = node.Data.GetParent(path);
        //    action = memberInfo.GetOnChangeAction(parent) + action;
        //    bool dirty = memberInfo.SerializeByJsonDotNet();
        //    field.RegisterValueChangedCallback(evt =>
        //    {
        //        node.Data.SetValue(in path, evt.newValue);
        //        if (dirty)
        //        {
        //            field.SetDirty();
        //        }
        //        action?.Invoke();
        //    });
        //    field.SetEnabled(!showInNode.ReadOnly);
        //    return new(memberInfo, node, path, this, field);
        //}
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            T field = new()
            {
                name = labelInfo.Text,
            };
            field.style.flexGrow = 1;
            field.style.height = 20;
            field.Insert(0, CreateLabel(labelInfo));
            Tv value = node.Data.GetValue<Tv>(in path);
            field.SetValueWithoutNotify(value);
            object parent = node.Data.GetParent(path);
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
            return new(memberMeta, node, path, this, field);
        }
    }
    public class FloatDrawer : TextValueFieldDrawer<FloatField, float> { }
    public class IntDrawer : TextValueFieldDrawer<IntegerField, int> { }
    public class StringDrawer : TextValueFieldDrawer<TextField, string> { }
    public class BoolDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(bool);

        //public override PropertyElement Create(MemberInfo memberInfo, ViewNode node, PropertyPath path, Action action)
        //{
        //    ShowInNodeAttribute showInNode = memberInfo.GetCustomAttribute<ShowInNodeAttribute>();
        //    LabelInfoAttribute labelInfo = memberInfo.GetLabelInfo();
        //    Toggle Toggle = NewToggle(labelInfo);
        //    Toggle.SetEnabled(!showInNode.ReadOnly);
        //    bool value = node.Data.GetValue<bool>(in path);
        //    Toggle.SetValueWithoutNotify(value);
        //    object parent = node.Data.GetParent(in path);
        //    action = memberInfo.GetOnChangeAction(parent) + action;
        //    bool dirty = memberInfo.SerializeByJsonDotNet();
        //    Toggle.RegisterValueChangedCallback(evt =>
        //    {
        //        node.Data.SetValue(in path, evt.newValue);
        //        if (dirty)
        //        {
        //            Toggle.SetDirty();
        //        }
        //        action?.Invoke();
        //    });
        //    return new PropertyElement(memberInfo, node, path, this, Toggle);
        //}
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            Toggle Toggle = NewToggle(labelInfo);
            Toggle.SetEnabled(!showInNode.ReadOnly);
            bool value = node.Data.GetValue<bool>(in path);
            Toggle.SetValueWithoutNotify(value);
            object parent = node.Data.GetParent(in path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            bool dirty = memberMeta.Json;
            Toggle.RegisterValueChangedCallback(evt =>
            {
                node.Data.SetValue(in path, evt.newValue);
                if (dirty)
                {
                    Toggle.SetDirty();
                }
                action?.Invoke();
            });
            return new PropertyElement(memberMeta, node, path, this, Toggle);
        }



        Toggle NewToggle(LabelInfoAttribute labelInfo)
        {
            Toggle Toggle = new(labelInfo.Text);
            Toggle.style.height = 20;
            Toggle.labelElement.name = "name";
            Toggle.labelElement.SetInfo(labelInfo);
            Toggle.style.flexGrow = 0;
            Toggle.style.justifyContent = Justify.SpaceAround;
            Toggle.Q<VisualElement>("unity-checkmark").parent.style.width = 20;
            Toggle.Q<VisualElement>("unity-checkmark").parent.style.flexGrow = 0;
            return Toggle;
        }
    }


}
