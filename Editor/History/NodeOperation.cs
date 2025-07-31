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
        public List<JsonNode> Nodes => GraphView.Asset.Data.Nodes;

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

            switch (Type)
            {
                case OperationType.Create:
                    Insert(To.Value);
                    break;
                case OperationType.Delete:
                    Remove(From.Value);
                    break;
                case OperationType.Move:
                    Remove(From.Value);
                    Insert(To.Value);
                    break;
            }
            return true; 
        }
        public bool Undo() { 
            switch (Type)
            {
                case OperationType.Create:
                    Remove(To.Value);
                    break;
                case OperationType.Delete:
                    Insert(From.Value);
                    break;
                case OperationType.Move:
                    Remove(To.Value);
                    Insert(From.Value);
                    break;
            }
            return true;
        }
        public void Insert(PAPath path)
        {
            if (path.ItemOfCollection)
            {
                IList<JsonNode> collection = PropertyAccessor.GetParentObject(Nodes, path, out PAPart last) as IList<JsonNode>;
                collection.Insert(last.Index, Node);
            }
            else
            {
                PropertyAccessor.SetValue(Nodes, path, Node);
            }
        }
        public void Remove(PAPath path)
        {
            if (path.ItemOfCollection)
            {
                IList<JsonNode> collection = PropertyAccessor.GetParentObject(Nodes, path, out PAPart last) as IList<JsonNode>;
                collection.RemoveAt(last.Index);
            }
            else
            {
                PropertyAccessor.SetValueNull(Nodes, path);
            }
        }
    }
}
