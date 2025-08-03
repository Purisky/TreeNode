using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{

    /// <summary>
    /// 原子操作接口
    /// </summary>
    public interface IAtomicOperation
    {
        List<ViewChange> Execute();
        List<ViewChange> Undo();
    }
    public enum OperationType
    {
        Create,
        Delete,
        Move,
    }

    public enum ViewChangeType
    {
        NodeCreate,
        NodeDelete,
        NodeField,
        ListItem,
        EdgeCreate,
        EdgeDelete,
    }
    public struct ViewChange 
    {
        public ViewChangeType ChangeType;
        public JsonNode Node;
        public PAPath Path;
        public int[] ExtraInfo;
        public ViewChange(ViewChangeType type, JsonNode node, PAPath path)
        {
            ChangeType = type;
            Node = node;
            Path = path;
            ExtraInfo = null;
        }
    }
}
