using System;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public abstract class TextValueFieldDrawer<T, Tv> : BaseDrawer where T : TextInputBaseField<Tv>, new()
    {
        public override Type DrawType => typeof(Tv);
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
