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
    public partial class History
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


        // 操作合并和缓存
        private ConcurrentQueue<IAtomicOperation> _pendingOperations = new ConcurrentQueue<IAtomicOperation>();
        private System.Threading.Timer _mergeTimer;
        private readonly object _mergeLock = new object();
        
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




}
