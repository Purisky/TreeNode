using System;
using System.Collections;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using static TreeEditor.TreeEditorHelper;

namespace TreeNode.Editor
{
    public class ParentPort : BasePort
    {
        public int Index;
        public int Depth;
        public bool Collection;
        protected ParentPort(ViewNode node_,Type type) : base(node_, Direction.Input, Capacity.Single, type)
        {
        }
        public static ParentPort Create(ViewNode node, Type type)
        {
            bool collection = false;
            if (type.Inherited(typeof(IList)))
            {
                type = type.GetGenericArguments()[0];
                collection = true;
            }

            ParentPort port = new(node,type)
            {
                tooltip = type.Name,
                portName = ""
            };
            port.style.alignSelf = Align.Center;
            port.style.paddingRight = 0;
            port.style.position = Position.Absolute;
            port.Q<Label>().style.marginLeft = 0;
            port.Q<Label>().style.marginRight = 0;
            port.Collection = collection;
            if (collection)
            {
                port.AddToClassList("ArrayPort");
            }
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
