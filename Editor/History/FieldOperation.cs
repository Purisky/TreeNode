using System;
using System.Collections;
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
        public override List<ViewChange> Execute()=> ApplyFieldValue(NewValue);
        public override List<ViewChange> Undo()=> ApplyFieldValue(OldValue);
        private List<ViewChange> ApplyFieldValue(T value)
        {
            try
            {
                Node.SetValue(FieldPath, value);
                return new List<ViewChange>
                {
                    new (ViewChangeType.NodeField,Node,FieldPath)
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

    public class ListItemModifyOperation : FieldModifyOperation
    {
        public int FromIndex;
        public int ToIndex;
        public object Value;

        public enum ItemModifyType
        {
            Add,
            Remove,
            Move
        }
        public ItemModifyType Type
        {
            get {
                if (FromIndex < 0) { return ItemModifyType.Add; }
                if (ToIndex < 0) { return ItemModifyType.Remove; }
                return ItemModifyType.Move;
            }
        }
        public ListItemModifyOperation(JsonNode node, PAPath listFieldPath, int fromIndex,int toIndex, object value, TreeNodeGraphView graphView)
        {
            Node = node;
            FieldPath = listFieldPath;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            Value = value;
            GraphView = graphView;
        }
        public override List<ViewChange> Execute()
        {
            IList list = Node.GetValue<IList>(FieldPath);
            List<ViewChange> changes = new();
            switch (Type)
            {
                case ItemModifyType.Add:
                    list.Insert(ToIndex, Value);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { -1, ToIndex } });
                    break;
                case ItemModifyType.Remove:
                    list.RemoveAt(FromIndex);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { FromIndex, -1 } });
                    break;
                case ItemModifyType.Move:
                    MoveItemInList(list,FromIndex,ToIndex);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { FromIndex, ToIndex } });
                    break;
            }
            return changes;
        }

        public override List<ViewChange> Undo()
        {
            IList list = Node.GetValue<IList>(FieldPath);
            List<ViewChange> changes = new();

            switch (Type)
            {
                case ItemModifyType.Add:
                    list.RemoveAt(ToIndex);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { ToIndex, -1 } });
                    break;
                case ItemModifyType.Remove:
                    list.Insert(FromIndex, Value);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { -1, FromIndex } });
                    break;
                case ItemModifyType.Move:
                    MoveItemInList(list, ToIndex, FromIndex);
                    changes.Add(new (ViewChangeType.ListItem, Node, FieldPath) { ExtraInfo = new[] { ToIndex, FromIndex } });
                    break;
            }

            return changes;
        }

        private void MoveItemInList(IList list,int fromIndex,int toIndex)
        {
            object item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
        }

        public override string GetOperationSummary()
        {
            return Type switch
            {
                ItemModifyType.Add => $"ListAdd: {FieldPath}[{ToIndex}] = '{Value}'",
                ItemModifyType.Remove => $"ListRemove: {FieldPath}[{FromIndex}] ('{Value}')",
                ItemModifyType.Move => $"ListMove: {FieldPath}[{FromIndex}] -> [{ToIndex}] ('{Value}')",
                _ => $"ListModify: {FieldPath} (Unknown operation)"
            };
        }
    }
}
