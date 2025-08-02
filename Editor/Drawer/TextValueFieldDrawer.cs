using System;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public abstract class TextValueFieldDrawer<T, Tv> : BaseDrawer where T : TextInputBaseField<Tv>, new()
    {
        public override Type DrawType => typeof(Tv);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
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
            Tv value = node.Data.GetValue<Tv>(path);

            field.SetValueWithoutNotify(value);
            object parent = node.Data.GetParent(path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            bool dirty = memberMeta.Json;
            field.RegisterValueChangedCallback(evt =>
            {
                if (dirty)
                {
                    Tv oldValue = node.Data.GetValue<Tv>(path);
                    node.RecordField(path, oldValue, evt.newValue);
                }
                node.Data.SetValue(path, evt.newValue);
                action?.Invoke();
                node.PopupText();
            });
            field.SetEnabled(!showInNode.ReadOnly);
            return new(memberMeta, node, path.ToString(), this, field);
        }
    }
    public class FloatDrawer : TextValueFieldDrawer<FloatField, float> { }
    public class IntDrawer : TextValueFieldDrawer<IntegerField, int> { }
    public class StringDrawer : TextValueFieldDrawer<TextField, string> { }
    public class BoolDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(bool);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            Toggle Toggle = NewToggle(labelInfo);
            Toggle.SetEnabled(!showInNode.ReadOnly);
            bool value = node.Data.GetValue<bool>(path);
            Toggle.SetValueWithoutNotify(value);
            object parent = node.Data.GetParent(path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            bool dirty = memberMeta.Json;
            Toggle.RegisterValueChangedCallback(evt =>
            {
                if (dirty)
                {
                    bool oldValue = node.Data.GetValue<bool>(path);
                    node.RecordField(path, oldValue, evt.newValue);
                }
                node.Data.SetValue(path, evt.newValue);
                node.PopupText();
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
            Toggle.style.justifyContent = Justify.SpaceBetween;
            Toggle.Q<VisualElement>("unity-checkmark").parent.style.width = 14;
            Toggle.Q<VisualElement>("unity-checkmark").parent.style.flexGrow = 0;
            return Toggle;
        }
    }


}
