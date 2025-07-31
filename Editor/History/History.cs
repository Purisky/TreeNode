using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 简化的历史系统 - 只保留核心撤销重做功能
    /// </summary>
    public partial class History
    {
        TreeNodeGraphWindow Window;
        List<HistoryStep> Steps = new();
        Stack<HistoryStep> RedoSteps = new();

        // 批量操作管理
        private HistoryStep _currentBatch;
        private bool _isBatchMode = false;

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
            if (Steps.Count > 20) // 简化的最大步骤数
            {
                Steps.RemoveAt(0);
            }
            RedoSteps.Clear();
        }

        /// <summary>
        /// 记录原子操作
        /// </summary>
        public void Record(IAtomicOperation operation)
        {
            if (operation == null) return;

            Debug.Log($"RecordOperation: {operation.Description}");

            // 如果在批量模式中，添加到当前批次
            if (_isBatchMode && _currentBatch != null)
            {
                _currentBatch.AddOperation(operation);
                Debug.Log($"[批量模式] 添加操作到当前批次: {operation.Description}");
            }
            else
            {

                // 简化版本：直接创建新步骤包含此操作
                var step = new HistoryStep();
                step.AddOperation(operation);
                step.Commit(operation.Description);
                step.EnsureSnapshot(Window.JsonAsset);
                Steps.Add(step);

                if (Steps.Count > 20)
                {
                    Steps.RemoveAt(0);
                }
                RedoSteps.Clear();
                Debug.Log($"[新建步骤] 创建新步骤: {operation.Description}");
            }

            Window.MakeDirty();
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
            _currentBatch.EnsureSnapshot(Window.JsonAsset);
            _isBatchMode = true;
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
                _currentBatch.EnsureSnapshot(Window.JsonAsset);
                Steps.Add(_currentBatch);
                
                if (Steps.Count > 20)
                {
                    Steps.RemoveAt(0);
                }
                
                RedoSteps.Clear();
            }

            _currentBatch = null;
            _isBatchMode = false;

            Window.MakeDirty();
        }

        public bool Undo()
        {
            if (Steps.Count <= 1) { return false; }
            
            Debug.Log($"执行撤销操作 - 当前步骤数: {Steps.Count}");
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            
            // 简化版本：直接恢复到前一个状态
            if (Steps.Any())
            {
                var prevStep = Steps[^1];
                var targetAsset = prevStep.GetAsset();
                if (targetAsset != null)
                {
                    Window.JsonAsset = targetAsset;
                    Window.GraphView.Redraw();
                }
            }
            
            Debug.Log("撤销操作完成");
            return true;
        }

        public bool Redo()
        {
            if (!RedoSteps.Any()) { return false; }
            
            Debug.Log($"执行重做操作 - 可重做步骤数: {RedoSteps.Count}");
            
            HistoryStep step = RedoSteps.Pop();
            Steps.Add(step);
            
            // 简化版本：直接恢复到指定状态
            var targetAsset = step.GetAsset();
            if (targetAsset != null)
            {
                Window.JsonAsset = targetAsset;
                Window.GraphView.Redraw();
            }
            
            Debug.Log("重做操作完成");
            return true;
        }

        /// <summary>
        /// 获取历史记录摘要（简化版本）
        /// </summary>
        public string GetHistorySummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"历史步骤总数: {Steps.Count}");
            summary.AppendLine($"可重做步骤: {RedoSteps.Count}");
            summary.AppendLine($"批量模式: {(_isBatchMode ? "开启" : "关闭")}");

            if (Steps.Count <= 1)
            {
                summary.AppendLine("无历史步骤");
            }
            else
            {
                summary.AppendLine("=== 最近的步骤 ===");
                var recentSteps = Steps.Skip(Math.Max(1, Steps.Count - 3)).ToList();
                
                for (int i = 0; i < recentSteps.Count; i++)
                {
                    var step = recentSteps[i];
                    var stepIndex = Steps.Count - recentSteps.Count + i;
                    
                    summary.AppendLine($"步骤 {stepIndex}: {step.Description} ({step.Timestamp:HH:mm:ss})");
                    
                    if (step.Operations.Count > 0)
                    {
                        summary.AppendLine($"  原子操作数: {step.Operations.Count}");
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
            Steps.Clear();
            RedoSteps.Clear();
        }
    }
}