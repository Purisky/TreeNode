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

    }




}
