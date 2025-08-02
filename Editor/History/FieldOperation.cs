using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{
    public abstract class FieldModifyOperation : IAtomicOperation
    {
        public PAPath FieldPath;
        public JsonNode Node;
        public string Description=> $"修改字段: {FieldPath}";
        public TreeNodeGraphView GraphView;
        public abstract List<ViewChange> Execute();
        public abstract List<ViewChange> Undo();
        public abstract string GetOperationSummary();
    }
    public class FieldModifyOperation<T> : FieldModifyOperation
    {
        public T OldValue;
        public T NewValue;
        public FieldModifyOperation(JsonNode node, PAPath fieldPath, T oldValue, T newValue, TreeNodeGraphView graphView)
        {
            Node = node;
            FieldPath = fieldPath;
            OldValue = oldValue;
            NewValue = newValue;
            GraphView = graphView;
        }
        public override List<ViewChange> Execute()
        {
            if (Node == null)
            {
                Debug.LogError("FieldModifyOperation.Execute: Node为空");
                return new();
            }

            return ApplyFieldValue(NewValue);
        }
        public override List<ViewChange> Undo()
        {
            if (Node == null)
            {
                Debug.LogError("FieldModifyOperation.Undo: Node为空");
                return new();
            }
            return ApplyFieldValue(OldValue);

        }
        private List<ViewChange> ApplyFieldValue(T value)
        {
            try
            {
                Node.SetValue(FieldPath, value);
                return new List<ViewChange>
                {
                    new ViewChange
                    {
                        ChangeType = ViewChangeType.NodeField,
                        Node = Node,
                        Path = FieldPath
                    }
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"通过JsonNode设置字段值失败: {e.Message}");
                return new();
            }
        }
        public override string GetOperationSummary()
        {
            return $"FieldModify<{typeof(T).Name}>: {FieldPath} from '{OldValue}' to '{NewValue}'";
        }
    }
}
