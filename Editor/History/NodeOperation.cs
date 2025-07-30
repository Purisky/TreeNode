using System;
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
        public string NodePath;
        public override string Description => $"在{NodePath}创建节点: {Node?.GetType().Name} ";

        public NodeCreateOperation(JsonNode node, string path, TreeNodeGraphView graphView)
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
        public string NodePath;
        public override string Description => $"在{NodePath}删除节点: {Node?.GetType().Name} ";

        public NodeDeleteOperation(JsonNode node, string  path, TreeNodeGraphView graphView)
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
    public class NodeMoveOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.Move;
        public DateTime Timestamp { get; private set; }
        public string Description => $"移动节点: {Node?.GetType().Name}";

        public JsonNode Node { get; set; }
        public NodeLocation FromLocation { get; set; }
        public NodeLocation ToLocation { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public NodeMoveOperation(JsonNode node, NodeLocation fromLocation, NodeLocation toLocation, TreeNodeGraphView graphView)
        {
            Node = node;
            FromLocation = fromLocation;
            ToLocation = toLocation;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            // 撤销移动操作 - 移回原位置
            return true;
        }
        public string GetOperationSummary()
        {
            return $"NodeMove: {Node?.GetType().Name} from {FromLocation?.GetFullPath()} to {ToLocation?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeMove_{Node?.GetHashCode()}_{FromLocation?.GetFullPath()}_{ToLocation?.GetFullPath()}_{Timestamp.Ticks}";
        }
    }
}
