using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{

    public class NodeOperation : IAtomicOperation
    {
        public OperationType Type
        {
            get
            {
                if (From.HasValue) { return OperationType.Create; }
                if (To.HasValue) { return OperationType.Delete; }
                return OperationType.Move;
            }
        }
        public PAPath? From;
        public PAPath? To;
        public JsonNode Node;
        public TreeNodeGraphView GraphView;
        public string Description
        {
            get
            {
                return Type switch
                {
                    OperationType.Create => $"在{From}创建节点: {Node?.GetType().Name}",
                    OperationType.Delete => $"在{To}删除节点: {Node?.GetType().Name}",
                    OperationType.Move => $"移动节点: {From}->{To}",
                    _ => "未知操作"
                };
            }
        }
        public string GetOperationSummary() { return Description; }




        public static NodeOperation Create(JsonNode node, PAPath path, TreeNodeGraphView graphView)
        {
            if (node == null || graphView == null)
            {
                return null;
            }
            return new NodeOperation
            {
                Node = node,
                To = path,
                GraphView = graphView
            };
        }
        public static NodeOperation Delete(JsonNode node, PAPath path, TreeNodeGraphView graphView)
        {
            if (node == null || graphView == null)
            {
                return null;
            }
            return new NodeOperation
            {
                Node = node,
                From = path,
                GraphView = graphView
            };
        }
        public static NodeOperation Move(JsonNode node, PAPath from, PAPath to, TreeNodeGraphView graphView)
        {
            if (from == to|| node==null|| graphView==null)
            {
                return null;
            }
            return new NodeOperation
            {
                Node = node,
                From = from,
                To = to,
                GraphView = graphView
            };
        }

        public bool Execute() {






            return true; 
        }

        public bool Undo() { 
            
            return true; }

    }
}
