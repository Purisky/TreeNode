﻿using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace TreeNode.Editor
{
    public abstract class ChildPort : BasePort,IValidator
    {
        public MemberMeta Meta;
        public bool Require;
        public ChildPort(MemberMeta meta, Capacity portCapacity, Type type) : base(Direction.Output, portCapacity, type)
        {
            Meta = meta;
            if (this is not NumPort)
            {
                //Debug.Log(Meta.ShowInNode.GetType());
                Require = (Meta.ShowInNode as ChildAttribute).Require;
                UpdateRequire();
            }
        }
        public abstract List<JsonNode> GetChildValues();
        public abstract PAPath SetNodeValue(JsonNode child, bool remove = true);
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
            UpdateRequire();
        }
        public virtual void OnRemoveEdge(Edge edge)
        {
            ParentPort parentport_of_child = edge.ParentPort();
            parentport_of_child.OnChange?.Invoke();
            OnChange?.Invoke();
            UpdateRequire();
        }
        public void UpdateRequire()
        {
            if (!Require) { return; }
            schedule.Execute(() =>
            {
                if (connected)
                {
                    RemoveFromClassList("Require");
                }
                else
                {
                    AddToClassList("Require");
                }
            }).StartingIn(200);

        }
        public bool Validate(out string msg)
        {
            msg = $"{Meta.Path}:{portType.Name} is null but required";
            if (Require && !connected)
            {
                return false;
            }
            return true;
        }

    }
}
