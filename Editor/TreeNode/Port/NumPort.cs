﻿using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class NumPort : ChildPort
    {
        protected NumPort(MemberMeta meta, Type type) : base(meta, Capacity.Single, type)
        {
        }
        public NumValue NumValue;
        public Label Text;
        public FloatField FloatField;
        public new ViewNode node;
        public static NumPort Create(MemberMeta meta, ViewNode node)
        {
            Type type = typeof(NumNode);
            if (meta.Type != typeof(NumValue))
            {
                Type gType = GetGenericType(meta.Type, typeof(NumValue<>));
                type = gType.GetGenericArguments()[0];
            }
            NumPort port = new(meta, type)
            {
                node = node
            };
            port.InitProperty();
            return port;
        }

        static Type GetGenericType(Type type, Type gType)
        {
            if (type.BaseType == null)
            {
                return null;
            }
            if (type.BaseType.IsGenericType&& type.BaseType.GetGenericTypeDefinition()== gType)
            {
                return type.BaseType;
            }
            return GetGenericType(type.BaseType, gType);
        }


        public void InitProperty()
        {
            style.flexGrow = 1;
            this.Q<VisualElement>("connector").style.minWidth = 8;
            LabelInfoAttribute labelInfo = Meta.LabelInfo;
            this.Q<Label>().SetInfo(labelInfo);
            this.Q<Label>().style.unityTextAlign = TextAnchor.MiddleLeft;
            this.Q<Label>().style.marginLeft = 1;
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

        public void InitNumValue(string path)
        {
            NumValue = node.Data.GetValue<NumValue>(path);
            //Debug.Log($"{path}  :  {Json.ToJson(NumValue)}");
            if (NumValue == null)
            {
                NumValue = Activator.CreateInstance(Meta.Type) as NumValue;
                node.Data.SetValue(path, NumValue);
            }
            FloatField.SetValueWithoutNotify(NumValue.Value);
            TryPopUpText();
        }
        public void SetOnChange(string path, Action action)
        {
            object parent = node.Data.GetParent(path);
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


        public override PAPath SetNodeValue(JsonNode child, bool remove = true)
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
            return Meta.Path;
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
