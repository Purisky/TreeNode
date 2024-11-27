using System.Collections.Generic;
using System;
using TreeNode.Runtime;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using NUnit.Framework;

namespace TreeNode.Editor
{
    public class ParentPort : BasePort
    {
        public int Index;
        public int Depth;
        protected ParentPort(Type type) : base( Direction.Input, Capacity.Single, type)
        {
        }
        public static ParentPort Create(Type type)
        {
            ParentPort port = new(type)
            {
                tooltip = type.Name,
                portName = ""
            };
            port.style.alignSelf = Align.Center;
            port.style.paddingRight = 0;
            port.style.position = Position.Absolute;
            port.Q<Label>().style.marginLeft = 0;
            port.Q<Label>().style.marginRight = 0;
            return port;
        }
        public void SetIndex(int index)
        {
            Index = index;
            portName = Index < 0 ? "" : Index.ToString();
        }
        public void SetIndex(List<JsonNode> list)
        {
            SetIndex(list.IndexOf(node.Data));
        }
    }
}
