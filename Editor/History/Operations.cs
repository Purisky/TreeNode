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
        string Description { get; }
        List<ViewChange> Execute();
        List<ViewChange> Undo();
        string GetOperationSummary();
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
        EdgeCreate,
        EdgeDelete,
    }
    public struct ViewChange : IEquatable<ViewChange>
    {
        public ViewChangeType ChangeType;
        public JsonNode Node;
        public PAPath Path;
        public readonly bool Equals(ViewChange other)
        {
            return ChangeType == other.ChangeType &&
                    Node == other.Node &&
                   Path.Equals(other.Path);
        }
        public ViewChange(ViewChangeType type, JsonNode node, PAPath path)
        {
            ChangeType = type;
            Node = node;
            Path = path;
        }
    }
}
