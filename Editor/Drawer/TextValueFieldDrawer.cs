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
            StyleAttribute style = memberMeta.Style;
            
            Toggle Toggle;
            
            // 检查是否有StyleAttribute，使用新样式
            if (style != null)
            {
                Toggle = NewStyledToggle(labelInfo);
            }
            else
            {
                Toggle = NewToggle(labelInfo);
            }
            
            Toggle.SetEnabled(!showInNode.ReadOnly);
            bool value = node.Data.GetValue<bool>(path);
            Toggle.SetValueWithoutNotify(value);
            
            // 如果是新样式，设置初始状态
            if (style != null)
            {
                UpdateStyledToggleAppearance(Toggle, value, labelInfo);
            }
            
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
                
                // 如果是新样式，更新外观
                if (style != null)
                {
                    UpdateStyledToggleAppearance(Toggle, evt.newValue, labelInfo);
                }
                
                node.PopupText();
                action?.Invoke();
            });
            PropertyElement propertyElement =   new PropertyElement(memberMeta, node, path, this, Toggle);
            propertyElement.style.flexGrow = 0;
            return propertyElement;
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

        Toggle NewStyledToggle(LabelInfoAttribute labelInfo)
        {
            Toggle Toggle = new(labelInfo.Text);
            Toggle.style.height = 20;
            Toggle.labelElement.name = "name";
            Toggle.labelElement.SetInfo(labelInfo);
            
            // Toggle的宽度与labelElement一致
            Toggle.style.width = Toggle.labelElement.style.width;
            Toggle.style.flexGrow = 0;
            Toggle.style.justifyContent = Justify.FlexStart;
            
            Toggle.style.borderTopLeftRadius = 5;
            Toggle.style.borderTopRightRadius = 5;
            Toggle.style.borderBottomLeftRadius = 5;
            Toggle.style.borderBottomRightRadius = 5;
            Toggle.style.borderTopWidth = 1;
            Toggle.style.borderBottomWidth = 1;
            Toggle.style.borderLeftWidth = 1;
            Toggle.style.borderRightWidth = 1;
            // 隐藏默认的checkmark
            var checkmarkContainer = Toggle.Q<VisualElement>("unity-checkmark").parent;
            checkmarkContainer.style.display = DisplayStyle.None;
            
            return Toggle;
        }

        void UpdateStyledToggleAppearance(Toggle toggle, bool value, LabelInfoAttribute labelInfo)
        {

            
            if (value)
            {
                // true时：文字变为labelInfo.Color，显示绿色外框
                if (UnityEngine.ColorUtility.TryParseHtmlString(labelInfo.Color, out UnityEngine.Color textColor))
                {
                    toggle.labelElement.style.color = textColor;
                }
                toggle.style.borderTopColor = UnityEngine.Color.green;
                toggle.style.borderBottomColor = UnityEngine.Color.green;
                toggle.style.borderLeftColor = UnityEngine.Color.green;
                toggle.style.borderRightColor = UnityEngine.Color.green;
            }
            else
            {
                // false时：显示灰色文字，边框变为透明色
                toggle.labelElement.style.color = UnityEngine.Color.gray;
                toggle.style.borderTopColor = UnityEngine.Color.clear;
                toggle.style.borderBottomColor = UnityEngine.Color.clear;
                toggle.style.borderLeftColor = UnityEngine.Color.clear;
                toggle.style.borderRightColor = UnityEngine.Color.clear;
            }
        }
    }


}
