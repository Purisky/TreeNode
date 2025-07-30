using System;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{
    public abstract class FieldModifyOperation : IAtomicOperation
    {
        public PAPath FieldPath;
        public JsonNode Node;
        public DateTime Timestamp { get; protected set; }
        public string Description=> $"修改字段: {FieldPath}";
        public TreeNodeGraphView GraphView;
        public abstract bool Execute();
        public abstract bool Undo();
        public abstract string GetOperationSummary();

    }

    /// <summary>
    /// 泛型字段修改操作 - 减少装箱操作的优化版本
    /// </summary>
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
            Timestamp = DateTime.Now;
        }
        /// <summary>
        /// 执行字段修改操作 - 将字段设置为新值
        /// </summary>
        public override bool Execute()
        {
            if (Node == null)
            {
                Debug.LogError("FieldModifyOperation.Execute: Node为空");
                return false;
            }

            return ApplyFieldValue(NewValue);
        }

        /// <summary>
        /// 撤销字段修改操作 - 将字段恢复为旧值
        /// </summary>
        public override bool Undo()
        {
            if (Node == null)
            {
                Debug.LogError("FieldModifyOperation.Undo: Node为空");
                return false;
            }
            return ApplyFieldValue(OldValue);

        }
        /// <summary>
        /// 通过JsonNode的本地路径应用字段值 - 更准确的实现
        /// </summary>
        private bool ApplyFieldValue(T value)
        {
            try
            {
                Node.SetValue(FieldPath, value);
                return true; // SetValue方法返回void，成功执行即返回true
            }
            catch (Exception e)
            {
                Debug.LogError($"通过JsonNode设置字段值失败: {e.Message}");
                return false;
            }
        }

        public override string GetOperationSummary()
        {
            return $"FieldModify<{typeof(T).Name}>: {FieldPath} from '{OldValue}' to '{NewValue}'";
        }
    }
}
