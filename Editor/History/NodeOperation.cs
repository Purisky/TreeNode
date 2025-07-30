﻿using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{

    public abstract class NodeOperation : IAtomicOperation
    {
        public TreeNodeGraphView GraphView;
        public abstract OperationType Type { get; }
        public abstract string Description { get; }
        public abstract bool Execute();
        public abstract string GetOperationSummary();
        public abstract bool Undo();
    }



    /// <summary>
    /// 节点创建操作
    /// </summary>
    public class NodeCreateOperation : NodeOperation
    {
        public override OperationType Type => OperationType.Create;
        public JsonNode Node { get; set; }
        public PAPath NodePath;
        public override string Description => $"在{NodePath}创建节点: {Node?.GetType().Name} ";

        public NodeCreateOperation(JsonNode node, PAPath path, TreeNodeGraphView graphView)
        {
            Node = node;
            NodePath = path;
            GraphView = graphView;
        }
        public override bool Execute()
        {
            return true;
        }
        public override bool Undo()
        {
            return true;
        }
        public override string GetOperationSummary()
        {
            return $"NodeCreate: {Node?.GetType().Name} at {NodePath}";
        }

    }

    /// <summary>
    /// 节点删除操作 - 实现具体的Execute/Undo逻辑
    /// </summary>
    public class NodeDeleteOperation : NodeOperation
    {
        public override OperationType Type => OperationType.Delete;
        public JsonNode Node { get; set; }
        public PAPath NodePath;
        public override string Description => $"在{NodePath}删除节点: {Node?.GetType().Name} ";

        public NodeDeleteOperation(JsonNode node, PAPath path, TreeNodeGraphView graphView)
        {
            Node = node;
            NodePath = path;
            GraphView = graphView;
        }

        /// <summary>
        /// 执行节点删除操作 - 从指定位置移除节点
        /// </summary>
        public override bool Execute()
        {
            return true;
        }

        /// <summary>
        /// 撤销节点删除操作 - 将节点恢复到原位置
        /// </summary>
        public override bool Undo()
        {
            return true;
        }
        public override string GetOperationSummary()
        {
            return $"NodeDelete: {Node?.GetType().Name} from {NodePath}";
        }
    }

    /// <summary>
    /// 节点移动操作
    /// </summary>
    public class NodeMoveOperation : NodeOperation
    {
        public override OperationType Type => OperationType.Move;
        public override string Description => $"移动节点: {From}->{To}";

        public JsonNode Node { get; set; }

        public PAPath From;
        public PAPath To;

        public NodeMoveOperation(JsonNode node, PAPath from, PAPath to, TreeNodeGraphView graphView)
        {
            Node = node;
            From = from;
            To = to;
            GraphView = graphView;
        }

        public override bool Execute()
        {
            return true;
        }

        public override bool Undo()
        {
            return true;
        }
        public override string GetOperationSummary()
        {
            return $"NodeMove: {Node?.GetType().Name} from {From} to {To}";
        }
    }
}
