using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                if (!From.HasValue) { return OperationType.Create; }
                if (!To.HasValue) { return OperationType.Delete; }
                return OperationType.Move;
            }
        }
        public PAPath? From;
        public PAPath? To;
        public JsonNode Node;
        public TreeNodeGraphView GraphView;
        public List<JsonNode> Nodes => GraphView.Asset.Data.Nodes;





        public static NodeOperation Create(JsonNode node, PAPath path, TreeNodeGraphView graphView)
        {
            if (node == null || graphView == null)
            {
                return null;
            }
            Debug.Log($"Create Node: {node.GetType().Name} at {path}");
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
            Debug.Log($"Delete Node: {node.GetType().Name} at {path}");
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

        public List<ViewChange> Execute() {

            List < ViewChange > changes = new();
            switch (Type)
            {
                case OperationType.Create:
                    Insert(To.Value);
                    changes.Add(new(ViewChangeType.NodeCreate, Node, To.Value));
                    if (!To.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeCreate, Node, To.Value));
                    }
                    break;
                case OperationType.Delete:
                    Remove(From.Value);
                    if (!From.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeDelete, Node, From.Value));
                    }
                    changes.Add(new(ViewChangeType.NodeDelete, Node, From.Value));
                    break;
                case OperationType.Move:
                    Remove(From.Value);
                    Insert(To.Value);
                    if (!From.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeDelete, Node, From.Value));
                    }
                    if (!To.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeCreate, Node, To.Value));
                    }
                    break;
            }
            return changes; 
        }
        public List<ViewChange> Undo() {
            List<ViewChange> changes = new();
            switch (Type)
            {
                case OperationType.Create:
                    Remove(To.Value);
                    if (!To.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeDelete, Node, To.Value));
                    }
                    changes.Add(new(ViewChangeType.NodeDelete, Node, To.Value));
                    break;
                case OperationType.Delete:
                    Insert(From.Value);
                    changes.Add(new(ViewChangeType.NodeCreate, Node, From.Value));
                    if (!From.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeCreate, Node, From.Value));
                    }
                    break;
                case OperationType.Move:
                    Remove(To.Value);
                    Insert(From.Value);
                    if (!To.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeDelete, Node, To.Value));
                    }
                    if (!From.Value.Root)
                    {
                        changes.Add(new(ViewChangeType.EdgeCreate, Node, From.Value));
                    }
                    break;
            }
            return changes;
        }
        public void Insert(PAPath path)
        {
            //Debug.Log($"Insert to {path}");
            if (path.ItemOfCollection)
            {
                IList collection = PropertyAccessor.GetParentObject(Nodes, path, out PAPart last) as IList;
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
                IList collection = PropertyAccessor.GetParentObject(Nodes, path, out PAPart last) as IList;
                collection.RemoveAt(last.Index);
            }
            else
            {
                PropertyAccessor.SetValueNull(Nodes, path);
            }
        }

        public override string ToString()
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
}
