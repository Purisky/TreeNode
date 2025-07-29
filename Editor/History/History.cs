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
        
        // 防重复记录机制
        private HashSet<string> _recordedOperationIds = new HashSet<string>();

        // 操作合并和缓存 - 简化为同步机制
        private List<IAtomicOperation> _pendingOperations = new List<IAtomicOperation>();
        private DateTime _lastMergeTime = DateTime.MinValue;
        
        // 增量渲染
        private HashSet<ViewNode> _dirtyNodes = new HashSet<ViewNode>();
        private bool _needsFullRedraw = false;

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
            
            _currentBatch = null;
            _isBatchMode = false;
            
            _recordedOperationIds.Clear();

            // 清理缓存和统计
            _performanceStats.Reset();
            
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
            if (_isBatchMode && _currentBatch != null)
            {
                return;
            }

            Steps.Add(new HistoryStep(Window.JsonAsset));
            if (Steps.Count > MaxStep)
            {
                Steps.RemoveAt(0);
                TriggerMemoryOptimization();
            }
            RedoSteps.Clear();
            
            // 清理操作ID缓存
            _recordedOperationIds.Clear();

            UpdatePerformanceStats();
        }

        /// <summary>
        /// 记录原子操作（带防重复机制和性能优化）- 优化用于位置变化处理
        /// </summary>
        public void RecordOperation(IAtomicOperation operation)
        {
            Debug.Log($"RecordOperation:{operation.Description}");
            if (operation == null) return;

            var startTime = DateTime.Now;

            // 优化防重复机制 - 对于字段修改操作，允许连续记录以便合并，但要避免真正的重复
            string operationId = operation.GetOperationId();
            
            // 特殊处理FieldModifyOperation：检查是否是真正的重复操作（相同的新旧值）
            if (operation.Type == OperationType.FieldModify)
            {
                // 获取新旧值字符串表示
                var oldValue = operation.GetOldValueString();
                var newValue = operation.GetNewValueString();
                
                // 如果新旧值相同，跳过这个无意义的操作
                if (oldValue == newValue)
                {
                    return;
                }
                
                // 对于字段修改，我们不使用防重复机制，让合并逻辑处理连续的修改
                // 这样连续的Position变化可以被正确合并
            }
            else
            {
                // 对于非字段修改操作，继续使用防重复机制
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
                _pendingOperations.Add(operation);
                TryProcessPendingOperations();
                return;
            }

            // 直接处理的操作
            ProcessOperationImmediate(operation);

            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            _performanceStats.RecordOperationTime(elapsed);

            Window.MakeDirty();
        }

        /// <summary>
        /// 基于时间窗口尝试处理待合并操作
        /// </summary>
        private void TryProcessPendingOperations()
        {
            var timeSinceLastMerge = DateTime.Now - _lastMergeTime;
            if (timeSinceLastMerge.TotalMilliseconds >= OperationMergeWindowMs || _pendingOperations.Count >= 10)
            {
                ProcessPendingOperations();
            }
        }

        /// <summary>
        /// 开始批量操作
        /// </summary>
        public void BeginBatch(string description = "批量操作")
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
            
            // 清理操作ID缓存，为批量操作准备
            _recordedOperationIds.Clear();

            _performanceStats.IsBatchMode = true;
        }

        /// <summary>
        /// 结束批量操作
        /// </summary>
        public void EndBatch()
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

            _performanceStats.IsBatchMode = false;

            Window.MakeDirty();
        }

        public bool Undo()
        {
            if (Steps.Count <= 1) { return false; }
            
            var startTime = DateTime.Now;
            Debug.Log($"执行撤销操作 - 当前步骤数:[{Steps.Count}]");
            Debug.Log(GetHistorySummary());
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            
            // 使用修复版的提交方法，确保GraphView同步
            CommitWithIncrementalRender(step, true);
            
            // 更新性能统计
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            _performanceStats.RecordUndoTime(elapsed);
            
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
            _performanceStats.RecordRedoTime(elapsed);
            _performanceStats.RedoSteps++;
            
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

            // 显示最近的步骤详情，最多5个
            summary.AppendLine();
            summary.AppendLine("=== 最近的步骤详情 ===");
            
            if (Steps.Count <= 1)
            {
                summary.AppendLine("无历史步骤");
            }
            else
            {
                // 获取最近的步骤（跳过第一个初始步骤）
                var recentSteps = Steps.Skip(Math.Max(1, Steps.Count - 5)).ToList();
                
                for (int i = 0; i < recentSteps.Count; i++)
                {
                    var step = recentSteps[i];
                    var stepIndex = Steps.Count - recentSteps.Count + i;
                    
                    summary.AppendLine($"步骤 {stepIndex}:");
                    summary.AppendLine($"  时间: {step.Timestamp:HH:mm:ss.fff}");
                    summary.AppendLine($"  描述: {step.Description}");
                    summary.AppendLine($"  状态: {(step.IsCommitted ? "已提交" : "未提交")}");
                    
                    if (step.Operations.Count > 0)
                    {
                        summary.AppendLine($"  原子操作数: {step.Operations.Count}");
                        
                        // 显示操作摘要（最多显示3个操作）
                        var operationsToShow = step.Operations.Take(3);
                        foreach (var operation in operationsToShow)
                        {
                            summary.AppendLine($"    - {operation.GetOperationSummary()}");
                        }
                        
                        if (step.Operations.Count > 3)
                        {
                            summary.AppendLine($"    ... 还有 {step.Operations.Count - 3} 个操作");
                        }
                    }
                    else
                    {
                        summary.AppendLine($"  操作类型: 传统操作（状态快照）");
                    }
                    
                    if (i < recentSteps.Count - 1)
                    {
                        summary.AppendLine();
                    }
                }
            }
            
            return summary.ToString();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            ClearNodeCache();
            ClearRenderingState();
        }
    }
}
