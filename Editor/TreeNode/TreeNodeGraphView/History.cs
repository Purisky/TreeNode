using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    /// <summary>
    /// 基于原子操作的高性能Undo/Redo历史系统
    /// 将所有编辑操作抽象为原子操作，支持精确的撤销重做和批量操作
    /// </summary>
    public class History
    {
        const int MaxStep = 20; // 增加历史步骤数量

        TreeNodeGraphWindow Window;
        List<HistoryStep> Steps = new();
        Stack<HistoryStep> RedoSteps = new();

        // 批量操作管理
        private HistoryStep _currentBatch;
        private bool _isBatchMode = false;
        private readonly object _batchLock = new object();

        public History(TreeNodeGraphWindow window)
        {
            Window = window;
            AddStep(false);
        }

        public void Clear()
        {
            HistoryStep historyStep = Steps[0];
            Steps.Clear();
            Steps.Add(historyStep);
            RedoSteps.Clear();
            
            lock (_batchLock)
            {
                _currentBatch = null;
                _isBatchMode = false;
            }
        }

        /// <summary>
        /// 添加步骤（保持兼容性）
        /// </summary>
        public void AddStep(bool dirty = true)
        {
            if (dirty)
            {
                Window.MakeDirty();
            }

            // 如果在批量模式中，不创建新步骤
            lock (_batchLock)
            {
                if (_isBatchMode && _currentBatch != null)
                {
                    return;
                }
            }

            Steps.Add(new HistoryStep(Window.JsonAsset));
            if (Steps.Count > MaxStep)
            {
                Steps.RemoveAt(0);
            }
            RedoSteps.Clear();
        }

        /// <summary>
        /// 记录原子操作
        /// </summary>
        public void RecordOperation(IAtomicOperation operation)
        {
            if (operation == null) return;

            lock (_batchLock)
            {
                if (_isBatchMode && _currentBatch != null)
                {
                    _currentBatch.AddOperation(operation);
                }
                else
                {
                    var step = new HistoryStep();
                    step.AddOperation(operation);
                    step.Commit(operation.Description);
                    Steps.Add(step);
                    
                    if (Steps.Count > MaxStep)
                    {
                        Steps.RemoveAt(0);
                    }
                    
                    RedoSteps.Clear();
                }
            }

            Window.MakeDirty();
        }

        /// <summary>
        /// 开始批量操作
        /// </summary>
        public void BeginBatch(string description = "批量操作")
        {
            lock (_batchLock)
            {
                if (_isBatchMode)
                {
                    EndBatch();
                }

                _currentBatch = new HistoryStep();
                _currentBatch.Description = description;
                _isBatchMode = true;
            }
        }

        /// <summary>
        /// 结束批量操作
        /// </summary>
        public void EndBatch()
        {
            lock (_batchLock)
            {
                if (!_isBatchMode || _currentBatch == null) return;

                if (_currentBatch.Operations.Count > 0)
                {
                    _currentBatch.Commit();
                    Steps.Add(_currentBatch);
                    
                    if (Steps.Count > MaxStep)
                    {
                        Steps.RemoveAt(0);
                    }
                    
                    RedoSteps.Clear();
                }

                _currentBatch = null;
                _isBatchMode = false;
            }

            Window.MakeDirty();
        }

        public bool Undo()
        {
            if (Steps.Count <= 1) { return false; }
            Debug.Log($"Undo:[{Steps.Count}]");
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            Commit(step, true);
            return true;
        }

        public bool Redo()
        {
            if (!RedoSteps.Any()) { return false; }
            Debug.Log("Redo");
            HistoryStep step = RedoSteps.Pop();
            Steps.Add(step);
            Commit(step, false);
            return true;
        }

        void Commit(HistoryStep step, bool undo)
        {
            if (undo)
            {
                if (Steps.Any())
                {
                    Window.JsonAsset = Steps[^1].GetAsset();
                    Window.GraphView.Redraw();
                }
            }
            else
            {
                Window.JsonAsset = step.GetAsset();
                Window.GraphView.Redraw();
            }
        }

        /// <summary>
        /// 获取历史记录摘要
        /// </summary>
        public string GetHistorySummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"历史步骤总数: {Steps.Count}");
            summary.AppendLine($"可重做步骤: {RedoSteps.Count}");
            summary.AppendLine($"批量模式: {(_isBatchMode ? "开启" : "关闭")}");
            return summary.ToString();
        }

        public class HistoryStep
        {
            string json;
            public List<IAtomicOperation> Operations { get; private set; } = new();
            public string Description { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public bool IsCommitted { get; set; } = false;

            public HistoryStep()
            {
                Timestamp = DateTime.Now;
            }

            public HistoryStep(JsonAsset asset) : this()
            {
                json = Json.ToJson(asset);
                Description = "传统操作";
                IsCommitted = true;
            }

            public JsonAsset GetAsset()
            {
                return Json.Get<JsonAsset>(json);
            }

            public void AddOperation(IAtomicOperation operation)
            {
                if (operation != null)
                {
                    Operations.Add(operation);
                }
            }

            public void Commit(string description = "")
            {
                if (!string.IsNullOrEmpty(description))
                {
                    Description = description;
                }
                IsCommitted = true;
            }
        }
    }

    /// <summary>
    /// 原子操作接口
    /// </summary>
    public interface IAtomicOperation
    {
        OperationType Type { get; }
        DateTime Timestamp { get; }
        string Description { get; }
        bool Execute();
        bool Undo();
        bool CanUndo();
        string GetOperationSummary();
    }

    /// <summary>
    /// 操作类型枚举
    /// </summary>
    public enum OperationType
    {
        NodeCreate,
        NodeDelete,
        NodeMove,
        FieldModify,
        BatchStart,
        BatchEnd,
        StateSnapshot
    }

    /// <summary>
    /// 位置类型枚举
    /// </summary>
    public enum LocationType
    {
        Root,
        Child,
        Deleted,
        Unknown
    }

    /// <summary>
    /// 节点位置描述
    /// </summary>
    public class NodeLocation
    {
        public LocationType Type { get; set; }
        public int RootIndex { get; set; } = -1;
        public JsonNode ParentNode { get; set; }
        public string PortName { get; set; } = "";
        public bool IsMultiPort { get; set; } = false;
        public int ListIndex { get; set; } = -1;

        public static NodeLocation Root(int index = -1) => new()
        {
            Type = LocationType.Root,
            RootIndex = index
        };

        public static NodeLocation Child(JsonNode parent, string portName, bool isMultiPort, int listIndex) => new()
        {
            Type = LocationType.Child,
            ParentNode = parent,
            PortName = portName,
            IsMultiPort = isMultiPort,
            ListIndex = listIndex
        };

        public static NodeLocation Deleted() => new()
        {
            Type = LocationType.Deleted
        };

        public static NodeLocation Unknown() => new()
        {
            Type = LocationType.Unknown
        };

        public string GetFullPath()
        {
            return Type switch
            {
                LocationType.Root => $"Root[{RootIndex}]",
                LocationType.Child => $"{ParentNode?.GetType().Name}.{PortName}" + 
                                     (IsMultiPort ? $"[{ListIndex}]" : ""),
                LocationType.Deleted => "Deleted",
                LocationType.Unknown => "Unknown",
                _ => "Invalid"
            };
        }
    }

    public static class HistoryExtensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.History.AddStep();
        }
    }
}
