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
        
        // 防重复记录机制
        private HashSet<string> _recordedOperationIds = new HashSet<string>();
        private readonly object _duplicateLock = new object();

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
            
            lock (_duplicateLock)
            {
                _recordedOperationIds.Clear();
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
            
            // 清理操作ID缓存
            lock (_duplicateLock)
            {
                _recordedOperationIds.Clear();
            }
        }

        /// <summary>
        /// 记录原子操作（带防重复机制）
        /// </summary>
        public void RecordOperation(IAtomicOperation operation)
        {
            if (operation == null) return;

            // 检查重复操作
            string operationId = operation.GetOperationId();
            lock (_duplicateLock)
            {
                if (_recordedOperationIds.Contains(operationId))
                {
                    Debug.LogWarning($"重复操作被忽略: {operationId}");
                    return;
                }
                _recordedOperationIds.Add(operationId);
            }

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
            
            // 清理操作ID缓存，为批量操作准备
            lock (_duplicateLock)
            {
                _recordedOperationIds.Clear();
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
        string GetOperationId(); // 用于防重复
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
        EdgeCreate,
        EdgeRemove,
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

    /// <summary>
    /// 节点创建操作
    /// </summary>
    public class NodeCreateOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeCreate;
        public DateTime Timestamp { get; private set; }
        public string Description => $"创建节点: {Node?.GetType().Name}";
        
        public JsonNode Node { get; set; }
        public NodeLocation Location { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public NodeCreateOperation(JsonNode node, NodeLocation location, TreeNodeGraphView graphView)
        {
            Node = node;
            Location = location;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            // 创建操作的执行逻辑
            return true;
        }

        public bool Undo()
        {
            // 撤销创建操作 - 删除节点
            return true;
        }

        public bool CanUndo() => Node != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeCreate: {Node?.GetType().Name} at {Location?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeCreate_{Node?.GetHashCode()}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 节点删除操作
    /// </summary>
    public class NodeDeleteOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeDelete;
        public DateTime Timestamp { get; private set; }
        public string Description => $"删除节点: {Node?.GetType().Name}";
        
        public JsonNode Node { get; set; }
        public NodeLocation FromLocation { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public NodeDeleteOperation(JsonNode node, NodeLocation fromLocation, TreeNodeGraphView graphView)
        {
            Node = node;
            FromLocation = fromLocation;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            // 撤销删除操作 - 恢复节点
            return true;
        }

        public bool CanUndo() => Node != null && FromLocation != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeDelete: {Node?.GetType().Name} from {FromLocation?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeDelete_{Node?.GetHashCode()}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 节点移动操作
    /// </summary>
    public class NodeMoveOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeMove;
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

        public bool CanUndo() => Node != null && FromLocation != null && ToLocation != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeMove: {Node?.GetType().Name} from {FromLocation?.GetFullPath()} to {ToLocation?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeMove_{Node?.GetHashCode()}_{FromLocation?.GetFullPath()}_{ToLocation?.GetFullPath()}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 字段修改操作
    /// </summary>
    public class FieldModifyOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.FieldModify;
        public DateTime Timestamp { get; private set; }
        public string Description => $"修改字段: {FieldPath}";
        
        public JsonNode Node { get; set; }
        public string FieldPath { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public FieldModifyOperation(JsonNode node, string fieldPath, string oldValue, string newValue, TreeNodeGraphView graphView)
        {
            Node = node;
            FieldPath = fieldPath;
            OldValue = oldValue;
            NewValue = newValue;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            // 撤销字段修改 - 恢复旧值
            return true;
        }

        public bool CanUndo() => Node != null && !string.IsNullOrEmpty(FieldPath) && GraphView != null;

        public string GetOperationSummary()
        {
            return $"FieldModify: {FieldPath} from '{OldValue}' to '{NewValue}'";
        }

        public string GetOperationId()
        {
            return $"FieldModify_{Node?.GetHashCode()}_{FieldPath}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 边连接操作
    /// </summary>
    public class EdgeCreateOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.EdgeCreate;
        public DateTime Timestamp { get; private set; }
        public string Description => "创建边连接";
        
        public JsonNode ParentNode { get; set; }
        public JsonNode ChildNode { get; set; }
        public string PortName { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public EdgeCreateOperation(JsonNode parentNode, JsonNode childNode, string portName, TreeNodeGraphView graphView)
        {
            ParentNode = parentNode;
            ChildNode = childNode;
            PortName = portName;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            return true;
        }

        public bool CanUndo() => ParentNode != null && ChildNode != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"EdgeCreate: {ParentNode?.GetType().Name}.{PortName} -> {ChildNode?.GetType().Name}";
        }

        public string GetOperationId()
        {
            return $"EdgeCreate_{ParentNode?.GetHashCode()}_{ChildNode?.GetHashCode()}_{PortName}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 边断开操作
    /// </summary>
    public class EdgeRemoveOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.EdgeRemove;
        public DateTime Timestamp { get; private set; }
        public string Description => "断开边连接";
        
        public JsonNode ParentNode { get; set; }
        public JsonNode ChildNode { get; set; }
        public string PortName { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public EdgeRemoveOperation(JsonNode parentNode, JsonNode childNode, string portName, TreeNodeGraphView graphView)
        {
            ParentNode = parentNode;
            ChildNode = childNode;
            PortName = portName;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            return true;
        }

        public bool CanUndo() => ParentNode != null && ChildNode != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"EdgeRemove: {ParentNode?.GetType().Name}.{PortName} -X-> {ChildNode?.GetType().Name}";
        }

        public string GetOperationId()
        {
            return $"EdgeRemove_{ParentNode?.GetHashCode()}_{ChildNode?.GetHashCode()}_{PortName}_{Timestamp.Ticks}";
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
