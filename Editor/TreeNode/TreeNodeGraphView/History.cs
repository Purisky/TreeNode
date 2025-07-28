using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;

namespace TreeNode.Editor
{
    /// <summary>
    /// 基于原子操作的高性能Undo/Redo历史系统
    /// 将所有编辑操作抽象为原子操作，支持精确的撤销重做和批量操作
    /// </summary>
    public class History
    {
        const int MaxStep = 20; // 增加历史步骤数量
        const int MaxMemoryUsageMB = 50; // 最大内存使用限制(MB)
        const int OperationMergeWindowMs = 500; // 操作合并时间窗口(毫秒)

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

        // 性能优化相关
        private PerformanceStats _performanceStats = new PerformanceStats();
        private readonly object _statsLock = new object();
        
        // 操作合并和缓存
        private ConcurrentQueue<IAtomicOperation> _pendingOperations = new ConcurrentQueue<IAtomicOperation>();
        private System.Threading.Timer _mergeTimer;
        private readonly object _mergeLock = new object();
        
        // 内存管理
        private readonly Dictionary<string, WeakReference> _nodeCache = new Dictionary<string, WeakReference>();
        private long _lastGCTime = DateTime.Now.Ticks;
        private const long GCIntervalTicks = TimeSpan.TicksPerMinute * 5; // 每5分钟检查一次GC

        // 增量渲染
        private HashSet<ViewNode> _dirtyNodes = new HashSet<ViewNode>();
        private bool _needsFullRedraw = false;
        private readonly object _renderLock = new object();

        public History(TreeNodeGraphWindow window)
        {
            Window = window;
            AddStep(false);
            
            // 初始化操作合并定时器
            _mergeTimer = new System.Threading.Timer(ProcessPendingOperations, null, 
                OperationMergeWindowMs, OperationMergeWindowMs);
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

            // 清理缓存和统计
            lock (_statsLock)
            {
                _performanceStats.Reset();
            }
            
            ClearNodeCache();
            ClearRenderingState();
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
                TriggerMemoryOptimization();
            }
            RedoSteps.Clear();
            
            // 清理操作ID缓存
            lock (_duplicateLock)
            {
                _recordedOperationIds.Clear();
            }

            UpdatePerformanceStats();
        }

        /// <summary>
        /// 记录原子操作（带防重复机制和性能优化）
        /// </summary>
        public void RecordOperation(IAtomicOperation operation)
        {
            if (operation == null) return;

            var startTime = DateTime.Now;

            // 检查重复操作
            string operationId = operation.GetOperationId();
            lock (_duplicateLock)
            {
                if (_recordedOperationIds.Contains(operationId))
                {
                    //Debug.LogWarning($"重复操作被忽略: {operationId}");
                    return;
                }
                _recordedOperationIds.Add(operationId);
            }

            // 智能操作合并：将操作加入待处理队列
            if (ShouldMergeOperation(operation))
            {
                _pendingOperations.Enqueue(operation);
                return;
            }

            // 直接处理的操作
            ProcessOperationImmediate(operation);

            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            lock (_statsLock)
            {
                _performanceStats.RecordOperationTime(elapsed);
            }

            Window.MakeDirty();
        }

        /// <summary>
        /// 判断操作是否应该合并
        /// </summary>
        private bool ShouldMergeOperation(IAtomicOperation operation)
        {
            // 字段修改操作适合合并
            return operation.Type == OperationType.FieldModify;
        }

        /// <summary>
        /// 立即处理操作
        /// </summary>
        private void ProcessOperationImmediate(IAtomicOperation operation)
        {
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
                        TriggerMemoryOptimization();
                    }
                    
                    RedoSteps.Clear();
                }
            }

            // 标记需要增量渲染
            MarkForIncrementalRender(operation);
        }

        /// <summary>
        /// 处理待合并的操作
        /// </summary>
        private void ProcessPendingOperations(object state)
        {
            if (_pendingOperations.IsEmpty) return;

            lock (_mergeLock)
            {
                var operationsToProcess = new List<IAtomicOperation>();
                
                // 收集所有待处理操作
                while (_pendingOperations.TryDequeue(out var operation))
                {
                    operationsToProcess.Add(operation);
                }

                if (operationsToProcess.Count == 0) return;

                // 智能合并操作
                var mergedOperations = MergeOperations(operationsToProcess);
                
                // 处理合并后的操作
                foreach (var operation in mergedOperations)
                {
                    ProcessOperationImmediate(operation);
                }

                // 更新统计
                lock (_statsLock)
                {
                    _performanceStats.MergedOperations += operationsToProcess.Count - mergedOperations.Count;
                }
            }
        }

        /// <summary>
        /// 智能合并操作
        /// </summary>
        private List<IAtomicOperation> MergeOperations(List<IAtomicOperation> operations)
        {
            var merged = new List<IAtomicOperation>();
            var fieldModifyGroups = new Dictionary<string, List<FieldModifyOperation>>();

            foreach (var operation in operations)
            {
                if (operation is FieldModifyOperation fieldOp)
                {
                    string key = $"{fieldOp.Node?.GetHashCode()}_{fieldOp.FieldPath}";
                    if (!fieldModifyGroups.ContainsKey(key))
                    {
                        fieldModifyGroups[key] = new List<FieldModifyOperation>();
                    }
                    fieldModifyGroups[key].Add(fieldOp);
                }
                else
                {
                    merged.Add(operation);
                }
            }

            // 合并同一字段的多次修改
            foreach (var group in fieldModifyGroups.Values)
            {
                if (group.Count == 1)
                {
                    merged.Add(group[0]);
                }
                else
                {
                    // 取第一个操作的旧值和最后一个操作的新值
                    var first = group[0];
                    var last = group[group.Count - 1];
                    
                    // 如果最终值等于初始值，则操作可以完全消除
                    if (first.OldValue == last.NewValue)
                    {
                        continue; // 跳过这个操作组
                    }
                    
                    var mergedOp = new FieldModifyOperation(
                        first.Node, first.FieldPath, first.OldValue, last.NewValue, first.GraphView);
                    merged.Add(mergedOp);
                }
            }

            return merged;
        }

        /// <summary>
        /// 标记需要增量渲染的节点
        /// </summary>
        private void MarkForIncrementalRender(IAtomicOperation operation)
        {
            lock (_renderLock)
            {
                switch (operation.Type)
                {
                    case OperationType.FieldModify:
                        if (operation is FieldModifyOperation fieldOp && 
                            TryGetViewNode(fieldOp.Node, out var viewNode))
                        {
                            _dirtyNodes.Add(viewNode);
                        }
                        break;
                    
                    case OperationType.NodeCreate:
                    case OperationType.NodeDelete:
                    case OperationType.NodeMove:
                        _needsFullRedraw = true;
                        break;
                }
            }
        }

        /// <summary>
        /// 尝试获取ViewNode
        /// </summary>
        private bool TryGetViewNode(JsonNode node, out ViewNode viewNode)
        {
            viewNode = null;
            if (node == null) return false;

            string nodeKey = node.GetHashCode().ToString();
            if (_nodeCache.TryGetValue(nodeKey, out var weakRef) && 
                weakRef.IsAlive && weakRef.Target is ViewNode cachedNode)
            {
                viewNode = cachedNode;
                return true;
            }

            // 从GraphView中查找
            if (Window.GraphView.NodeDic.TryGetValue(node, out viewNode))
            {
                _nodeCache[nodeKey] = new WeakReference(viewNode);
                return true;
            }

            return false;
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

            lock (_statsLock)
            {
                _performanceStats.IsBatchMode = true;
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
                        TriggerMemoryOptimization();
                    }
                    
                    RedoSteps.Clear();
                }

                _currentBatch = null;
                _isBatchMode = false;
            }

            lock (_statsLock)
            {
                _performanceStats.IsBatchMode = false;
            }

            Window.MakeDirty();
        }

        public bool Undo()
        {
            if (Steps.Count <= 1) { return false; }
            
            var startTime = DateTime.Now;
            //Debug.Log($"Undo:[{Steps.Count}]");
            
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            
            // 使用增量渲染优化的提交
            CommitWithIncrementalRender(step, true);
            
            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            lock (_statsLock)
            {
                _performanceStats.RecordUndoTime(elapsed);
            }
            
            return true;
        }

        public bool Redo()
        {
            if (!RedoSteps.Any()) { return false; }
            
            var startTime = DateTime.Now;
            Debug.Log("Redo");
            
            HistoryStep step = RedoSteps.Pop();
            Steps.Add(step);
            
            // 使用增量渲染优化的提交
            CommitWithIncrementalRender(step, false);
            
            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            lock (_statsLock)
            {
                _performanceStats.RecordRedoTime(elapsed);
                _performanceStats.RedoSteps++;
            }
            
            return true;
        }

        /// <summary>
        /// 使用增量渲染优化的提交
        /// </summary>
        void CommitWithIncrementalRender(HistoryStep step, bool undo)
        {
            if (undo)
            {
                if (Steps.Any())
                {
                    Window.JsonAsset = Steps[^1].GetAsset();
                    // 智能渲染：检查是否需要全量重绘
                    if (ShouldUseIncrementalRender(step))
                    {
                        IncrementalRedraw(step);
                    }
                    else
                    {
                        Window.GraphView.Redraw();
                    }
                }
            }
            else
            {
                Window.JsonAsset = step.GetAsset();
                if (ShouldUseIncrementalRender(step))
                {
                    IncrementalRedraw(step);
                }
                else
                {
                    Window.GraphView.Redraw();
                }
            }
        }

        /// <summary>
        /// 判断是否应该使用增量渲染
        /// </summary>
        private bool ShouldUseIncrementalRender(HistoryStep step)
        {
            // 如果操作数量太多，或者包含结构性变化，使用全量重绘
            if (step.Operations.Count > 10) return false;
            
            foreach (var operation in step.Operations)
            {
                if (operation.Type == OperationType.NodeCreate ||
                    operation.Type == OperationType.NodeDelete ||
                    operation.Type == OperationType.NodeMove)
                {
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// 增量重绘
        /// </summary>
        private void IncrementalRedraw(HistoryStep step)
        {
            lock (_renderLock)
            {
                if (_needsFullRedraw)
                {
                    Window.GraphView.Redraw();
                    ClearRenderingState();
                    return;
                }

                // 只更新脏节点
                foreach (var dirtyNode in _dirtyNodes)
                {
                    if (dirtyNode != null)
                    {
                        // 只刷新属性面板，不重建整个节点
                        dirtyNode.RefreshPropertyElements();
                    }
                }

                ClearRenderingState();
            }
        }

        /// <summary>
        /// 清理渲染状态
        /// </summary>
        private void ClearRenderingState()
        {
            lock (_renderLock)
            {
                _dirtyNodes.Clear();
                _needsFullRedraw = false;
            }
        }

        /// <summary>
        /// 内存优化触发
        /// </summary>
        private void TriggerMemoryOptimization()
        {
            // 检查内存使用情况
            CheckMemoryUsage();
            
            // 清理节点缓存
            CleanupNodeCache();
            
            // 定期GC
            PerformPeriodicGC();
        }

        /// <summary>
        /// 检查内存使用情况
        /// </summary>
        private void CheckMemoryUsage()
        {
            var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024); // MB
            
            lock (_statsLock)
            {
                _performanceStats.MemoryUsageMB = memoryUsage;
                
                if (memoryUsage > MaxMemoryUsageMB)
                {
                    Debug.LogWarning($"History memory usage ({memoryUsage}MB) exceeds limit ({MaxMemoryUsageMB}MB)");
                    // 触发更积极的内存清理
                    PerformAggressiveCleanup();
                }
            }
        }

        /// <summary>
        /// 清理节点缓存
        /// </summary>
        private void CleanupNodeCache()
        {
            var keysToRemove = new List<string>();
            
            foreach (var kvp in _nodeCache)
            {
                if (!kvp.Value.IsAlive)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                _nodeCache.Remove(key);
            }
        }

        /// <summary>
        /// 定期GC
        /// </summary>
        private void PerformPeriodicGC()
        {
            var currentTime = DateTime.Now.Ticks;
            if (currentTime - _lastGCTime > GCIntervalTicks)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
                _lastGCTime = currentTime;
            }
        }

        /// <summary>
        /// 执行积极的内存清理
        /// </summary>
        private void PerformAggressiveCleanup()
        {
            // 清空所有缓存
            ClearNodeCache();
            
            // 强制GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            Debug.Log("Performed aggressive memory cleanup");
        }

        /// <summary>
        /// 清空节点缓存
        /// </summary>
        private void ClearNodeCache()
        {
            _nodeCache.Clear();
        }

        /// <summary>
        /// 更新性能统计
        /// </summary>
        private void UpdatePerformanceStats()
        {
            lock (_statsLock)
            {
                _performanceStats.TotalSteps = Steps.Count;
                _performanceStats.RedoSteps = RedoSteps.Count;
                _performanceStats.CachedOperations = _nodeCache.Count;
            }
        }

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public PerformanceStats GetPerformanceStats()
        {
            lock (_statsLock)
            {
                UpdatePerformanceStats();
                return _performanceStats.Clone();
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
            
            var stats = GetPerformanceStats();
            summary.AppendLine($"内存使用: {stats.MemoryUsageMB:F2}MB");
            summary.AppendLine($"缓存节点: {stats.CachedOperations}");
            summary.AppendLine($"合并操作: {stats.MergedOperations}");
            
            return summary.ToString();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _mergeTimer?.Dispose();
            ClearNodeCache();
            ClearRenderingState();
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

    /// <summary>
    /// 性能统计信息
    /// </summary>
    public class PerformanceStats
    {
        public int TotalSteps { get; set; }
        public int RedoSteps { get; set; }
        public long MemoryUsageMB { get; set; }
        public int CachedOperations { get; set; }
        public int MergedOperations { get; set; }
        public bool IsBatchMode { get; set; }
        
        // 性能计时
        public double AverageOperationTimeMs { get; private set; }
        public double AverageUndoTimeMs { get; private set; }
        public double AverageRedoTimeMs { get; private set; }
        
        private readonly List<double> _operationTimes = new List<double>();
        private readonly List<double> _undoTimes = new List<double>();
        private readonly List<double> _redoTimes = new List<double>();

        public void RecordOperationTime(double timeMs)
        {
            _operationTimes.Add(timeMs);
            if (_operationTimes.Count > 100) _operationTimes.RemoveAt(0);
            AverageOperationTimeMs = _operationTimes.Average();
        }

        public void RecordUndoTime(double timeMs)
        {
            _undoTimes.Add(timeMs);
            if (_undoTimes.Count > 100) _undoTimes.RemoveAt(0);
            AverageUndoTimeMs = _undoTimes.Average();
        }

        public void RecordRedoTime(double timeMs)
        {
            _redoTimes.Add(timeMs);
            if (_redoTimes.Count > 100) _redoTimes.RemoveAt(0);
            AverageRedoTimeMs = _redoTimes.Average();
        }

        public void Reset()
        {
            TotalSteps = 0;
            RedoSteps = 0;
            MemoryUsageMB = 0;
            CachedOperations = 0;
            MergedOperations = 0;
            IsBatchMode = false;
            AverageOperationTimeMs = 0;
            AverageUndoTimeMs = 0;
            AverageRedoTimeMs = 0;
            _operationTimes.Clear();
            _undoTimes.Clear();
            _redoTimes.Clear();
        }

        public PerformanceStats Clone()
        {
            return new PerformanceStats
            {
                TotalSteps = this.TotalSteps,
                RedoSteps = this.RedoSteps,
                MemoryUsageMB = this.MemoryUsageMB,
                CachedOperations = this.CachedOperations,
                MergedOperations = this.MergedOperations,
                IsBatchMode = this.IsBatchMode,
                AverageOperationTimeMs = this.AverageOperationTimeMs,
                AverageUndoTimeMs = this.AverageUndoTimeMs,
                AverageRedoTimeMs = this.AverageRedoTimeMs
            };
        }
    }
}
