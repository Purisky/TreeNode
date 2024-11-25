using System;
using System.Collections.Generic;
using System.Reflection;
using TreeNode.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace TreeNode.Editor
{
    public abstract class ChildPort : BasePort
    {
        //public MemberInfo Member;
        public MemberMeta Meta;
        public ChildPort(Capacity portCapacity, Type type) : base(Direction.Output, portCapacity, type)
        {
        }

        public abstract List<JsonNode> GetChildValues();
        public abstract void SetNodeValue(JsonNode child, bool remove = true);
        public object GetPortValue()
        {
           return node.Data.GetValue<object>(Meta.Path);
        }

        public virtual void OnAddEdge(Edge edge)
        {
            //Debug.Log("OnAddEdge");
            ParentPort parentport_of_child = edge.ParentPort();
            parentport_of_child.OnChange?.Invoke();
            OnChange?.Invoke();
        }
        public virtual void OnRemoveEdge(Edge edge)
        {
            //Debug.Log("OnRemoveEdge");
            ParentPort parentport_of_child = edge.ParentPort();
            parentport_of_child.OnChange?.Invoke();
            OnChange?.Invoke();
        }


    }
}
