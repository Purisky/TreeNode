using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class History
    {
        public class HistoryStep
        {
            public List<IAtomicOperation> Operations { get; private set; } = new();
            public DateTime Timestamp { get; set; }
            public HistoryStep()
            {
                Timestamp = DateTime.Now;
            }
            public void AddOperation(IAtomicOperation operation)
            {
                if (operation != null)
                {
                    Operations.Add(operation);
                }
            }
            public List<ViewChange> Undo()
            {
                List<ViewChange> changes = new ();
                for (int i = 1; i <= Operations.Count; i++)
                {
                    changes.AddRange( Operations[^i].Undo());
                }
                return changes;
            }
            public List<ViewChange> Redo()
            {
                List<ViewChange> changes = new ();
                for (int i = 0; i < Operations.Count; i++)
                {
                    changes.AddRange(Operations[i].Execute());
                }
                return changes;
            }

            public override string ToString()
            {
                //列出所有原子操作
                string operationsSummary = string.Join("\n", Operations.ConvertAll(op => op.ToString()));
                return $"HistoryStep: {Timestamp:HH:mm:ss} - 操作数: {Operations.Count}\n{operationsSummary}";
            }

        }
    }
}
