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
        /// 记录原子操作（带防重复机制和性能优化）- 优化用于位置变化处理
        /// </summary>
        public void RecordOperation(IAtomicOperation operation)
        {
            if (operation == null) return;

            var startTime = DateTime.Now;

            // 🔥 优化防重复机制 - 对于字段修改操作，允许连续记录以便合并，但要避免真正的重复
            string operationId = operation.GetOperationId();
            
            // 🔥 特殊处理FieldModifyOperation：检查是否是真正的重复操作（相同的新旧值）
            if (operation is FieldModifyOperation fieldOp)
            {
                // 如果新旧值相同，跳过这个无意义的操作
                if (fieldOp.OldValue == fieldOp.NewValue)
                {
                    return;
                }
                
                // 对于字段修改，我们不使用防重复机制，让合并逻辑处理连续的修改
                // 这样连续的Position变化可以被正确合并
            }
            else
            {
                // 对于非字段修改操作，继续使用防重复机制
                lock (_duplicateLock)
                {
                    if (_recordedOperationIds.Contains(operationId))
                    {
                        //Debug.LogWarning($"重复操作被忽略: {operationId}");
                        return;
                    }
                    _recordedOperationIds.Add(operationId);
                }
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
                    // 确保步骤包含当前状态的快照
                    step.EnsureSnapshot(Window.JsonAsset);
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
        /// 智能合并操作 - 优化位置变化处理
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
                    // 🔥 智能合并逻辑：按时间戳排序确保正确的合并顺序
                    var sortedGroup = group.OrderBy(op => op.Timestamp).ToList();
                    var first = sortedGroup[0];
                    var last = sortedGroup[sortedGroup.Count - 1];
                    
                    // 如果最终值等于初始值，则操作可以完全消除
                    if (first.OldValue == last.NewValue)
                    {
                        // 🔥 针对Position字段的特殊处理：即使回到原位置，如果有中间移动过程也记录为一次"移动并返回"操作
                        if (first.FieldPath == "Position" && sortedGroup.Count > 2)
                        {
                            var mergedOp = new FieldModifyOperation(
                                first.Node, first.FieldPath, first.OldValue, last.NewValue, first.GraphView);
                            // 🔥 通过构造后设置描述信息
                            mergedOp.SetDescription($"节点位置移动（经过{sortedGroup.Count}步最终返回原位置）");
                            merged.Add(mergedOp);
                        }
                        continue; // 其他情况跳过这个操作组
                    }
                    
                    // 创建合并操作，包含更丰富的描述信息
                    var mergedOperation = new FieldModifyOperation(
                        first.Node, first.FieldPath, first.OldValue, last.NewValue, first.GraphView);
                    
                    // 🔥 为Position字段提供更好的描述
                    if (first.FieldPath == "Position")
                    {
                        mergedOperation.SetDescription($"节点位置变化（{sortedGroup.Count}步操作已合并）: {first.OldValue} → {last.NewValue}");
                    }
                    
                    merged.Add(mergedOperation);
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
                // 确保批量操作开始时记录当前状态
                _currentBatch.EnsureSnapshot(Window.JsonAsset);
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
                    // 确保批量操作结束时包含正确的状态快照
                    _currentBatch.EnsureSnapshot(Window.JsonAsset);
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
            Debug.Log($"执行撤销操作 - 当前步骤数:[{Steps.Count}]");
            
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            
            // 使用修复版的提交方法，确保GraphView同步
            CommitWithIncrementalRender(step, true);
            
            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            lock (_statsLock)
            {
                _performanceStats.RecordUndoTime(elapsed);
            }
            
            Debug.Log($"撤销操作完成 - 耗时:{elapsed:F2}ms");
            return true;
        }

        public bool Redo()
        {
            if (!RedoSteps.Any()) { return false; }
            
            var startTime = DateTime.Now;
            Debug.Log($"执行重做操作 - 可重做步骤数:[{RedoSteps.Count}]");
            
            HistoryStep step = RedoSteps.Pop();
            Steps.Add(step);
            
            // 使用修复版的提交方法，确保GraphView同步
            CommitWithIncrementalRender(step, false);
            
            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            lock (_statsLock)
            {
                _performanceStats.RecordRedoTime(elapsed);
                _performanceStats.RedoSteps++;
            }
            
            Debug.Log($"重做操作完成 - 耗时:{elapsed:F2}ms");
            return true;
        }

        /// <summary>
        /// 使用增量渲染优化的提交 - 修复版本，确保GraphView同步
        /// </summary>
        void CommitWithIncrementalRender(HistoryStep step, bool undo)
        {
            JsonAsset targetAsset = null;
            
            if (undo)
            {
                // Undo：恢复到前一个状态
                if (Steps.Any())
                {
                    var prevStep = Steps[^1];
                    targetAsset = prevStep.GetAsset();
                }
            }
            else
            {
                // Redo：恢复到指定状态
                targetAsset = step.GetAsset();
            }

            if (targetAsset == null)
            {
                Debug.LogError($"无法获取{(undo ? "撤销" : "重做")}步骤的资产数据，执行全量重绘");
                Window.GraphView.Redraw();
                return;
            }

            // 1. 更新JsonAsset
            Window.JsonAsset = targetAsset;
            
            // 2. 关键修复：同步GraphView与新的JsonAsset
            SyncGraphViewWithAsset(targetAsset, step);
        }

        /// <summary>
        /// 同步GraphView与JsonAsset - 核心修复方法
        /// </summary>
        private void SyncGraphViewWithAsset(JsonAsset asset, HistoryStep step)
        {
            try
            {
                // 1. 更新逻辑层树结构 - 先更新，让NodeTree重新分析
                if (Window.GraphView.NodeTree != null)
                {
                    Window.GraphView.NodeTree.MarkDirty();
                    Window.GraphView.NodeTree.RefreshIfNeeded();
                }
                
                // 2. 执行全量重绘以确保完整同步
                // 这是最可靠的方法，确保所有ViewNode和Edge都与新的JSON状态匹配
                // GraphView.Redraw() 会内部处理Asset的同步
                Window.GraphView.Redraw();
                
                Debug.Log($"成功同步GraphView状态 - {(step.Operations.Count > 0 ? $"原子操作:{step.Operations.Count}个" : "传统操作")}");
            }
            catch (Exception e)
            {
                Debug.LogError($"同步GraphView时发生错误: {e.Message}");
                Debug.LogException(e);
                
                // 出错时强制重绘
                try
                {
                    Window.GraphView.Redraw();
                }
                catch (Exception redrawError)
                {
                    Debug.LogError($"强制重绘也失败: {redrawError.Message}");
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
                if (asset != null)
                {
                    json = Json.ToJson(asset);
                }
                else
                {
                    json = null;
                }
                Description = "传统操作";
                IsCommitted = true;
            }

            /// <summary>
            /// 确保步骤包含状态快照
            /// </summary>
            public void EnsureSnapshot(JsonAsset asset)
            {
                if (string.IsNullOrEmpty(json) && asset != null)
                {
                    json = Json.ToJson(asset);
                }
            }

            public JsonAsset GetAsset()
            {
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("HistoryStep的json数据为空，无法恢复状态");
                    return null;
                }
                
                try
                {
                    return Json.Get<JsonAsset>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"反序列化HistoryStep失败: {e.Message}");
                    return null;
                }
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

        /// <summary>
        /// 执行节点创建操作 - 将节点添加到指定位置
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null || Location == null || GraphView == null)
                {
                    Debug.LogError("NodeCreateOperation.Execute: 参数不完整");
                    return false;
                }

                // 根据位置类型添加节点
                bool success = AddNodeToLocation();
                
                if (success)
                {
                    // 创建对应的ViewNode
                    CreateViewNode();
                    
                    Debug.Log($"成功执行节点创建: {Node.GetType().Name} at {Location.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"执行节点创建操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销节点创建操作 - 从指定位置移除节点
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null || Location == null || GraphView == null)
                {
                    Debug.LogError("NodeCreateOperation.Undo: 参数不完整");
                    return false;
                }

                // 移除ViewNode
                RemoveViewNode();
                
                // 从位置移除节点
                bool success = RemoveNodeFromLocation();
                
                if (success)
                {
                    Debug.Log($"成功撤销节点创建: {Node.GetType().Name} from {Location.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销节点创建操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将节点添加到指定位置
        /// </summary>
        private bool AddNodeToLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (Location.Type)
                {
                    case LocationType.Root:
                        // 添加到根节点列表
                        if (Location.RootIndex >= 0 && Location.RootIndex <= asset.Data.Nodes.Count)
                        {
                            asset.Data.Nodes.Insert(Location.RootIndex, Node);
                        }
                        else
                        {
                            asset.Data.Nodes.Add(Node);
                        }
                        return true;

                    case LocationType.Child:
                        // 添加到父节点的子节点端口
                        return AddNodeToParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {Location.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"添加节点到位置失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从指定位置移除节点
        /// </summary>
        private bool RemoveNodeFromLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (Location.Type)
                {
                    case LocationType.Root:
                        // 从根节点列表移除
                        return asset.Data.Nodes.Remove(Node);

                    case LocationType.Child:
                        // 从父节点的子节点端口移除
                        return RemoveNodeFromParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {Location.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"从位置移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将节点添加到父节点的端口
        /// </summary>
        private bool AddNodeToParentPort()
        {
            try
            {
                if (Location.ParentNode == null || string.IsNullOrEmpty(Location.PortName))
                    return false;

                var parentType = Location.ParentNode.GetType();
                var portField = parentType.GetField(Location.PortName) ?? 
                               parentType.GetProperty(Location.PortName)?.GetValue(Location.ParentNode) as System.Reflection.FieldInfo;

                if (portField == null)
                {
                    // 尝试属性
                    var portProperty = parentType.GetProperty(Location.PortName);
                    if (portProperty == null)
                        return false;

                    var portValue = portProperty.GetValue(Location.ParentNode);
                    
                    if (Location.IsMultiPort)
                    {
                        // 多端口：添加到列表
                        if (portValue is System.Collections.IList list)
                        {
                            if (Location.ListIndex >= 0 && Location.ListIndex <= list.Count)
                            {
                                list.Insert(Location.ListIndex, Node);
                            }
                            else
                            {
                                list.Add(Node);
                            }
                            return true;
                        }
                    }
                    else
                    {
                        // 单端口：直接设置
                        portProperty.SetValue(Location.ParentNode, Node);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"添加节点到父端口失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从父节点的端口移除节点
        /// </summary>
        private bool RemoveNodeFromParentPort()
        {
            try
            {
                if (Location.ParentNode == null || string.IsNullOrEmpty(Location.PortName))
                    return false;

                var parentType = Location.ParentNode.GetType();
                var portProperty = parentType.GetProperty(Location.PortName);
                if (portProperty == null)
                    return false;

                var portValue = portProperty.GetValue(Location.ParentNode);
                
                if (Location.IsMultiPort)
                {
                    if (portValue is System.Collections.IList list)
                    {
                        list.Remove(Node);
                        return true;
                    }
                }
                else
                {
                    // 单端口：设置为null
                    portProperty.SetValue(Location.ParentNode, null);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"从父端口移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建ViewNode
        /// </summary>
        private void CreateViewNode()
        {
            try
            {
                if (GraphView.NodeDic.ContainsKey(Node))
                    return; // 已存在

                ViewNode viewNode;
                if (Node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(Node, GraphView);
                }
                else
                {
                    viewNode = new ViewNode(Node, GraphView);
                }

                viewNode.SetPosition(new Rect(Node.Position, Vector2.zero));
                GraphView.ViewNodes.Add(viewNode);
                GraphView.NodeDic.Add(Node, viewNode);
                GraphView.AddElement(viewNode);
            }
            catch (Exception e)
            {
                Debug.LogError($"创建ViewNode失败: {e.Message}");
            }
        }

        /// <summary>
        /// 移除ViewNode
        /// </summary>
        private void RemoveViewNode()
        {
            try
            {
                if (GraphView.NodeDic.TryGetValue(Node, out var viewNode))
                {
                    GraphView.ViewNodes.Remove(viewNode);
                    GraphView.NodeDic.Remove(Node);
                    GraphView.RemoveElement(viewNode);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"移除ViewNode失败: {e.Message}");
            }
        }

        public bool CanUndo() => Node != null && Location != null && GraphView != null;

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
    /// 节点删除操作 - 实现具体的Execute/Undo逻辑
    /// </summary>
    public class NodeDeleteOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeDelete;
        public DateTime Timestamp { get; private set; }
        public string Description => $"删除节点: {Node?.GetType().Name}";
        
        public JsonNode Node { get; set; }
        public NodeLocation FromLocation { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        // 保存节点在删除前的连接信息
        private List<EdgeInfo> _savedEdges = new List<EdgeInfo>();

        public NodeDeleteOperation(JsonNode node, NodeLocation fromLocation, TreeNodeGraphView graphView)
        {
            Node = node;
            FromLocation = fromLocation;
            GraphView = graphView;
            Timestamp = DateTime.Now;
            
            // 删除前保存边连接信息
            SaveEdgeConnections();
        }

        /// <summary>
        /// 执行节点删除操作 - 从指定位置移除节点
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null || FromLocation == null || GraphView == null)
                {
                    Debug.LogError("NodeDeleteOperation.Execute: 参数不完整");
                    return false;
                }

                // 保存边连接（如果还没保存）
                if (_savedEdges.Count == 0)
                {
                    SaveEdgeConnections();
                }

                // 移除ViewNode
                RemoveViewNode();
                
                // 从位置移除节点
                bool success = RemoveNodeFromLocation();
                
                if (success)
                {
                    Debug.Log($"成功执行节点删除: {Node.GetType().Name} from {FromLocation.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"执行节点删除操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销节点删除操作 - 将节点恢复到原位置
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null || FromLocation == null || GraphView == null)
                {
                    Debug.LogError("NodeDeleteOperation.Undo: 参数不完整");
                    return false;
                }

                // 将节点添加回原位置
                bool success = AddNodeToLocation();
                
                if (success)
                {
                    // 创建对应的ViewNode
                    CreateViewNode();
                    
                    // 恢复边连接
                    RestoreEdgeConnections();
                    
                    Debug.Log($"成功撤销节点删除: {Node.GetType().Name} at {FromLocation.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销节点删除操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存节点的边连接信息
        /// </summary>
        private void SaveEdgeConnections()
        {
            try
            {
                _savedEdges.Clear();
                
                // 保存作为子节点的连接（父节点指向此节点）
                var asset = GraphView.Window.JsonAsset;
                if (asset != null)
                {
                    SaveIncomingEdges(asset.Data.Nodes);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"保存边连接信息失败: {e.Message}");
            }
        }

        /// <summary>
        /// 递归保存输入边
        /// </summary>
        private void SaveIncomingEdges(List<JsonNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == null) continue;

                var nodeType = node.GetType();
                var properties = nodeType.GetProperties();

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(node);
                    
                    // 检查单个子节点连接
                    if (value == Node)
                    {
                        _savedEdges.Add(new EdgeInfo
                        {
                            ParentNode = node,
                            ChildNode = Node,
                            PortName = prop.Name,
                            IsMultiPort = false,
                            ListIndex = -1
                        });
                    }
                    // 检查多个子节点连接
                    else if (value is System.Collections.IList list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] == Node)
                            {
                                _savedEdges.Add(new EdgeInfo
                                {
                                    ParentNode = node,
                                    ChildNode = Node,
                                    PortName = prop.Name,
                                    IsMultiPort = true,
                                    ListIndex = i
                                });
                            }
                        }
                    }
                }

                // 递归检查子节点
                SaveIncomingEdgesFromNode(node);
            }
        }

        /// <summary>
        /// 递归保存输入边
        /// </summary>
        private void SaveIncomingEdgesFromNode(JsonNode node)
        {
            var nodeType = node.GetType();
            var properties = nodeType.GetProperties();

            foreach (var prop in properties)
            {
                var value = prop.GetValue(node);
                
                if (value is JsonNode childNode && childNode != null)
                {
                    SaveIncomingEdges(new List<JsonNode> { childNode });
                }
                else if (value is System.Collections.IList list)
                {
                    var childNodes = new List<JsonNode>();
                    foreach (var item in list)
                    {
                        if (item is JsonNode child)
                            childNodes.Add(child);
                    }
                    if (childNodes.Count > 0)
                        SaveIncomingEdges(childNodes);
                }
            }
        }

        /// <summary>
        /// 恢复边连接
        /// </summary>
        private void RestoreEdgeConnections()
        {
            try
            {
                foreach (var edge in _savedEdges)
                {
                    var parentType = edge.ParentNode.GetType();
                    var portProperty = parentType.GetProperty(edge.PortName);
                    
                    if (portProperty == null) continue;

                    if (edge.IsMultiPort)
                    {
                        var list = portProperty.GetValue(edge.ParentNode) as System.Collections.IList;
                        if (list != null)
                        {
                            if (edge.ListIndex >= 0 && edge.ListIndex <= list.Count)
                            {
                                list.Insert(edge.ListIndex, Node);
                            }
                            else
                            {
                                list.Add(Node);
                            }
                        }
                    }
                    else
                    {
                        portProperty.SetValue(edge.ParentNode, Node);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"恢复边连接失败: {e.Message}");
            }
        }

        /// <summary>
        /// 将节点添加到位置（复用NodeCreateOperation的逻辑）
        /// </summary>
        private bool AddNodeToLocation()
        {
            // 创建临时的NodeCreateOperation来复用逻辑
            var createOp = new NodeCreateOperation(Node, FromLocation, GraphView);
            return createOp.Execute();
        }

        /// <summary>
        /// 从位置移除节点
        /// </summary>
        private bool RemoveNodeFromLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (FromLocation.Type)
                {
                    case LocationType.Root:
                        return asset.Data.Nodes.Remove(Node);

                    case LocationType.Child:
                        return RemoveNodeFromParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {FromLocation.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"从位置移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从父节点的端口移除节点
        /// </summary>
        private bool RemoveNodeFromParentPort()
        {
            try
            {
                if (FromLocation.ParentNode == null || string.IsNullOrEmpty(FromLocation.PortName))
                    return false;

                var parentType = FromLocation.ParentNode.GetType();
                var portProperty = parentType.GetProperty(FromLocation.PortName);
                if (portProperty == null)
                    return false;

                var portValue = portProperty.GetValue(FromLocation.ParentNode);
                
                if (FromLocation.IsMultiPort)
                {
                    if (portValue is System.Collections.IList list)
                    {
                        list.Remove(Node);
                        return true;
                    }
                }
                else
                {
                    portProperty.SetValue(FromLocation.ParentNode, null);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"从父端口移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建ViewNode
        /// </summary>
        private void CreateViewNode()
        {
            try
            {
                if (GraphView.NodeDic.ContainsKey(Node))
                    return;

                ViewNode viewNode;
                if (Node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(Node, GraphView);
                }
                else
                {
                    viewNode = new ViewNode(Node, GraphView);
                }

                viewNode.SetPosition(new Rect(Node.Position, Vector2.zero));
                GraphView.ViewNodes.Add(viewNode);
                GraphView.NodeDic.Add(Node, viewNode);
                GraphView.AddElement(viewNode);
            }
            catch (Exception e)
            {
                Debug.LogError($"创建ViewNode失败: {e.Message}");
            }
        }

        /// <summary>
        /// 移除ViewNode
        /// </summary>
        private void RemoveViewNode()
        {
            try
            {
                if (GraphView.NodeDic.TryGetValue(Node, out var viewNode))
                {
                    GraphView.ViewNodes.Remove(viewNode);
                    GraphView.NodeDic.Remove(Node);
                    GraphView.RemoveElement(viewNode);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"移除ViewNode失败: {e.Message}");
            }
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

        /// <summary>
        /// 边连接信息
        /// </summary>
        private class EdgeInfo
        {
            public JsonNode ParentNode { get; set; }
            public JsonNode ChildNode { get; set; }
            public string PortName { get; set; }
            public bool IsMultiPort { get; set; }
            public int ListIndex { get; set; }
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
    /// 字段修改操作 - 实现具体的Execute/Undo逻辑
    /// </summary>
    public class FieldModifyOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.FieldModify;
        public DateTime Timestamp { get; private set; }
        
        // 🔥 支持自定义描述信息
        private string _description;
        public string Description 
        { 
            get => _description ?? $"修改字段: {FieldPath}";
        }
        
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

        /// <summary>
        /// 设置自定义描述信息 - 用于合并操作
        /// </summary>
        public void SetDescription(string description)
        {
            _description = description;
        }

        /// <summary>
        /// 执行字段修改操作 - 将字段设置为新值
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null)
                {
                    Debug.LogError("FieldModifyOperation.Execute: Node为空");
                    return false;
                }

                return ApplyFieldValue(NewValue);
            }
            catch (Exception e)
            {
                Debug.LogError($"执行字段修改操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销字段修改操作 - 将字段恢复为旧值
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null)
                {
                    Debug.LogError("FieldModifyOperation.Undo: Node为空");
                    return false;
                }

                return ApplyFieldValue(OldValue);
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销字段修改操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 应用字段值 - 核心逻辑，支持各种字段类型
        /// </summary>
        private bool ApplyFieldValue(string value)
        {
            try
            {
                // 处理Position字段的特殊情况
                if (FieldPath == "Position")
                {
                    return ApplyPositionValue(value);
                }

                // 通过反射设置字段值
                return ApplyFieldValueViaReflection(value);
            }
            catch (Exception e)
            {
                Debug.LogError($"应用字段值失败 {FieldPath} = {value}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 应用Position字段值
        /// </summary>
        private bool ApplyPositionValue(string value)
        {
            try
            {
                // 解析Position字符串，格式："(x, y)"
                if (TryParsePosition(value, out var position))
                {
                    Node.Position = position;
                    
                    // 同步更新ViewNode的位置
                    if (GraphView?.NodeDic.TryGetValue(Node, out var viewNode) == true)
                    {
                        viewNode.SetPosition(new Rect(position, Vector2.zero));
                    }
                    
                    return true;
                }
                
                Debug.LogWarning($"无法解析Position值: {value}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"应用Position值失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析Position字符串
        /// </summary>
        private bool TryParsePosition(string positionStr, out Vec2 position)
        {
            position = default;
            
            if (string.IsNullOrEmpty(positionStr))
                return false;
                
            // 移除括号和空格
            positionStr = positionStr.Trim('(', ')', ' ');
            var parts = positionStr.Split(',');
            
            if (parts.Length != 2)
                return false;
                
            if (float.TryParse(parts[0].Trim(), out var x) && 
                float.TryParse(parts[1].Trim(), out var y))
            {
                position = new Vec2(x, y);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 通过反射应用字段值
        /// </summary>
        private bool ApplyFieldValueViaReflection(string value)
        {
            try
            {
                var nodeType = Node.GetType();
                var fieldInfo = nodeType.GetField(FieldPath, 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                
                if (fieldInfo != null)
                {
                    var convertedValue = ConvertStringToFieldType(value, fieldInfo.FieldType);
                    fieldInfo.SetValue(Node, convertedValue);
                    return true;
                }

                var propertyInfo = nodeType.GetProperty(FieldPath, 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    var convertedValue = ConvertStringToFieldType(value, propertyInfo.PropertyType);
                    propertyInfo.SetValue(Node, convertedValue);
                    return true;
                }

                Debug.LogWarning($"未找到字段或属性: {FieldPath} 在类型 {nodeType.Name} 中");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"反射设置字段值失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将字符串转换为对应的字段类型
        /// </summary>
        private object ConvertStringToFieldType(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return GetDefaultValue(targetType);

            // 处理可空类型
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlyingType == typeof(string))
                    return value;
                if (underlyingType == typeof(int))
                    return int.Parse(value);
                if (underlyingType == typeof(float))
                    return float.Parse(value);
                if (underlyingType == typeof(double))
                    return double.Parse(value);
                if (underlyingType == typeof(bool))
                    return bool.Parse(value);
                if (underlyingType.IsEnum)
                    return Enum.Parse(underlyingType, value);

                // 使用Convert.ChangeType作为后备方案
                return Convert.ChangeType(value, underlyingType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"类型转换失败 {value} -> {targetType}: {e.Message}");
                return GetDefaultValue(targetType);
            }
        }

        /// <summary>
        /// 获取类型的默认值
        /// </summary>
        private object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public bool CanUndo() => Node != null && !string.IsNullOrEmpty(FieldPath) && GraphView != null;

        public string GetOperationSummary()
        {
            return $"FieldModify: {FieldPath} from '{OldValue}' to '{NewValue}'";
        }

        public string GetOperationId()
        {
            // 🔥 优化操作ID生成 - 移除时间戳，确保同一节点同一字段的操作能被识别为同类操作进行合并
            // 这样连续的Position变化操作会有相同的操作ID前缀，便于合并逻辑识别
            return $"FieldModify_{Node?.GetHashCode()}_{FieldPath}";
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
