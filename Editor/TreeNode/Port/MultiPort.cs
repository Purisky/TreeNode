using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using UnityEditor.Experimental.GraphView;

namespace TreeNode.Editor
{
    public class MultiPort : ChildPort
    {
        protected MultiPort(MemberMeta meta,Type type) : base(meta, Capacity.Multi, type)
        {
        }
        public static MultiPort Create(MemberMeta meta)
        {
            Type type = meta.Type.GetGenericArguments()[0];
            MultiPort port = new(meta,type)
            {
                tooltip = type.Name,
                portName = meta.LabelInfo.Hide?null: meta.LabelInfo.Text,
            };
            port.AddToClassList("ArrayPort");

            return port;
        }



        public override List<JsonNode> GetChildValues()
        {
            object portValue = GetPortValue();
            if (portValue == null) { return new(); }
            return (portValue as IList).Cast<JsonNode>().ToList();
        }


        public override void SetNodeValue(JsonNode child, bool remove = true)
        {
            JsonNode parent = node.Data;
            IList list = node.Data.GetValue<IList>( Meta.Path);
            if (remove)
            {
                if (list == null)
                {
                    return;
                }
                list.Remove(child);
            }
            else
            {
                if (list == null)
                {
                    list = Activator.CreateInstance(Meta.Type) as IList;
                    node.Data.SetValue(Meta.Path, list);
                }
                list.Add(child);
            }
        }

        public override void OnRemoveEdge(Edge edge)
        {
            ParentPort parentport_of_child = edge.ParentPort();
            parentport_of_child.SetIndex(-1);
            List<JsonNode> list = GetChildValues();
            if (list.Any())
            {
                foreach (var existEdge in connections)
                {
                    existEdge.ParentPort().SetIndex(list);
                }
            }
            base.OnRemoveEdge(edge);
        }
        public override void OnAddEdge(Edge edge)
        {
            ParentPort parentport_of_child = edge.ParentPort();
            parentport_of_child.SetIndex(GetChildValues().Count - 1);
            base.OnAddEdge(edge);
        }

    }
}
