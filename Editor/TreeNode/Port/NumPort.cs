using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class NumPort : ChildPort
    {
        protected NumPort() : base(Capacity.Single, typeof(NumNode))
        {
        }
        public NumValue NumValue;
        public Label Text;
        public FloatField FloatField;
        public new ViewNode node;
        public static NumPort Create(MemberMeta memberMeta, ViewNode node)
        {
            NumPort port = new()
            {
                Meta = memberMeta,
                tooltip = nameof(NumNode),
                node = node
            };
            port.InitProperty();
            return port;
        }
        public void InitProperty()
        {
            style.flexGrow = 1;
            this.Q<VisualElement>("connector").style.minWidth = 8;
            LabelInfoAttribute labelInfo = Meta.LabelInfo;
            this.Q<Label>().SetInfo(labelInfo);
            this.Q<Label>().style.unityTextAlign = TextAnchor.MiddleLeft;
            VisualElement element = new();
            element.style.flexDirection = FlexDirection.Row;
            element.style.flexGrow = 1;
            Insert(1, element);
            Text = new Label();
            Text.style.color = new Color(0.5f, 0.5f, 0.5f);
            Text.style.flexGrow = 1;
            Text.style.overflow = Overflow.Hidden;
            Text.style.fontSize = 9;
            element.Add(Text);
            FloatField = new FloatField();
            FloatField.style.flexGrow = 1;
            element.Add(FloatField);
            SetNumState(connected);
        }

        public void InitNumValue(PropertyPath path)
        {
            
            NumValue = node.Data.GetValue< NumValue >(in path);
            //Debug.Log($"{path}  :  {Json.ToJson(NumValue)}");
            if (NumValue == null)
            {
                NumValue = new();
                node.Data.SetValue(in path, NumValue);
            }
            FloatField.SetValueWithoutNotify(NumValue.Value);
            TryPopUpText();
        }
        public void SetOnChange(PropertyPath path, Action action)
        {
            object parent = node.Data.GetParent(in path);
            OnChange = Meta.OnChangeMethod.GetOnChangeAction(parent) + action;
            FloatField.RegisterValueChangedCallback(evt =>
            {
                NumValue.Value = evt.newValue;
                TryPopUpText();
                this.SetDirty();
                OnChange?.Invoke();

            });


        }

        public void SetNumState(bool connected)
        {
            Text.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;
            FloatField.style.display = connected ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public override void Connect(Edge edge)
        {
            base.Connect(edge);
            SetNumState(connected);
        }
        public override void Disconnect(Edge edge)
        {
            base.Disconnect(edge);
            SetNumState(connected);
        }

        public override List<JsonNode> GetChildValues()
        {
            //Json.Log(NumValue);
            if (NumValue.Node == null)
            {
                return new();
            }
            return new() { NumValue.Node };
        }


        public override void SetNodeValue(JsonNode child, bool remove = true)
        {
            if (remove)
            {
                NumValue.Node = null;
            }
            else
            {
                NumValue.Node = child as NumNode;
            }
            TryPopUpText();
            OnChange?.Invoke();
        }

        public void TryPopUpText()
        {
            Text.text = NumValue.GetText();
            Text.tooltip = Text.text;
            ViewNode viewNode = node;
            JsonNode jsonNode = viewNode.Data;
            if (jsonNode is NumNode && viewNode.ParentPort != null && viewNode.ParentPort.connected)
            {
                Edge edge = viewNode.ParentPort.connections.First();
                if (edge.output is NumPort numPort)
                {
                    numPort.TryPopUpText();
                }
            }
        }

    }
}
