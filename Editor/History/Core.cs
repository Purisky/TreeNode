using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class History
    {
        /// <summary>
        /// 判断操作是否应该合并
        /// </summary>
        private bool ShouldMergeOperation(IAtomicOperation operation)
        {
            // 字段修改操作适合合并
            return operation.Type == OperationType.FieldModify;
        }

        /// <summary>
        /// 立即处理操作 - 修改为智能步骤合并版本
        /// </summary>
        private void ProcessOperationImmediate(IAtomicOperation operation)
        {
            if (_isBatchMode && _currentBatch != null)
            {
                _currentBatch.AddOperation(operation);
                Debug.Log($"[批量模式] 添加操作到当前批次: {operation.Description}");
            }
            else
            {
                // 🔥 关键改进：尝试合并到最后一个步骤而不是总是创建新步骤
                var lastStep = GetLastModifiableStep();
                if (lastStep != null && CanMergeToStep(lastStep, operation))
                {
                    lastStep.AddOperation(operation);
                    lastStep.EnsureSnapshot(Window.JsonAsset); // 更新快照
                    Debug.Log($"[智能合并] 合并操作到现有步骤: {operation.Description} -> 步骤[{Steps.Count - 1}]");
                }
                else
                {
                    // 只有在不能合并时才创建新步骤
                    CreateNewStepWithOperation(operation);
                    Debug.Log($"[新建步骤] 创建新步骤: {operation.Description} -> 步骤[{Steps.Count - 1}]");
                }
            }

            // 标记需要增量渲染
            MarkForIncrementalRender(operation);
        }

        /// <summary>
        /// 获取最后一个可修改的步骤
        /// </summary>
        private HistoryStep GetLastModifiableStep()
        {
            if (Steps.Count <= 1) return null; // 跳过初始步骤
            
            var lastStep = Steps[^1];
            
            // 只有原子操作步骤才能被合并，传统快照步骤不能合并
            if (lastStep.Operations.Count == 0) return null;
            
            return lastStep;
        }

        /// <summary>
        /// 判断操作是否可以合并到指定步骤
        /// </summary>
        private bool CanMergeToStep(HistoryStep step, IAtomicOperation operation)
        {
            if (step == null || operation == null) return false;
            
            // 时间窗口检查（1秒内的操作可以合并）
            var timeSinceStep = DateTime.Now - step.Timestamp;
            if (timeSinceStep.TotalMilliseconds > 1000)
            {
                Debug.Log($"[合并检查] 时间窗口超时: {timeSinceStep.TotalMilliseconds}ms > 1000ms");
                return false;
            }
            
            // 如果是空步骤，总是可以合并
            if (step.Operations.Count == 0) return true;
            
            // 操作类型兼容性检查
            switch (operation.Type)
            {
                case OperationType.FieldModify:
                    // 字段修改操作通常可以合并，特别是同一字段的连续修改
                    return CanMergeFieldModifyOperation(step, operation);
                
                case OperationType.NodeCreate:
                case OperationType.NodeDelete:
                case OperationType.EdgeCreate:
                case OperationType.EdgeRemove:
                    // 结构性操作：如果步骤中已有相同类型的操作，可以合并
                    return HasCompatibleStructuralOperations(step, operation);
                
                case OperationType.NodeMove:
                    // 节点移动操作可以与其他移动操作合并
                    return step.Operations.Any(op => op.Type == OperationType.NodeMove);
                
                default:
                    return false;
            }
        }

        /// <summary>
        /// 检查字段修改操作是否可以合并
        /// </summary>
        private bool CanMergeFieldModifyOperation(HistoryStep step, IAtomicOperation operation)
        {
            // 字段修改操作更宽松的合并策略：
            // 1. 同一节点的不同字段修改可以合并（如同时修改位置和名称）
            // 2. 同一字段的连续修改应该在上层合并逻辑中处理
            
            var operationNode = operation.GetNode();
            if (operationNode == null) return true; // 安全合并
            
            // 检查是否有同一节点的操作
            foreach (var existingOp in step.Operations)
            {
                if (existingOp.Type == OperationType.FieldModify)
                {
                    var existingNode = existingOp.GetNode();
                    if (existingNode == operationNode)
                    {
                        // 同一节点的字段修改，允许合并
                        Debug.Log($"[合并检查] 同节点字段修改合并: {existingOp.GetFieldPath()} + {operation.GetFieldPath()}");
                        return true;
                    }
                }
            }
            
            // 不同节点的字段修改也可以合并到同一步骤
            return true;
        }

        /// <summary>
        /// 检查结构性操作是否可以合并
        /// </summary>
        private bool HasCompatibleStructuralOperations(HistoryStep step, IAtomicOperation operation)
        {
            // 结构性操作的合并策略：
            // 1. 同类型操作可以合并（如批量删除节点）
            // 2. 兼容的操作类型可以合并（如删除节点 + 删除边）
            
            var operationType = operation.Type;
            
            foreach (var existingOp in step.Operations)
            {
                // 同类型操作
                if (existingOp.Type == operationType) return true;
                
                // 兼容的操作类型组合
                if (AreCompatibleOperationTypes(existingOp.Type, operationType))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 检查两种操作类型是否兼容
        /// </summary>
        private bool AreCompatibleOperationTypes(OperationType type1, OperationType type2)
        {
            // 定义兼容的操作类型组合
            var compatiblePairs = new[]
            {
                (OperationType.NodeDelete, OperationType.EdgeRemove), // 删除节点时删除相关边
                (OperationType.EdgeRemove, OperationType.NodeDelete), // 反向兼容
                (OperationType.NodeCreate, OperationType.EdgeCreate), // 创建节点时创建连接
                (OperationType.EdgeCreate, OperationType.NodeCreate), // 反向兼容
            };
            
            return compatiblePairs.Any(pair => 
                (pair.Item1 == type1 && pair.Item2 == type2) ||
                (pair.Item1 == type2 && pair.Item2 == type1));
        }

        /// <summary>
        /// 创建包含单个操作的新步骤
        /// </summary>
        private void CreateNewStepWithOperation(IAtomicOperation operation)
        {
            var step = new HistoryStep();
            step.AddOperation(operation);
            step.Commit(operation.Description);
            
            // 确保步骤包含当前状态的快照
            step.EnsureSnapshot(Window.JsonAsset);
            
            Steps.Add(step);

            // 清理超出限制的旧步骤
            if (Steps.Count > MaxStep)
            {
                Steps.RemoveAt(0);
                TriggerMemoryOptimization();
            }

            // 清空重做栈
            RedoSteps.Clear();
        }

        /// <summary>
        /// 处理待合并的操作 - 使用智能步骤合并版本
        /// </summary>
        private void ProcessPendingOperations()
        {
            if (_pendingOperations.Count == 0) return;

            var operationsToProcess = new List<IAtomicOperation>(_pendingOperations);
            _pendingOperations.Clear();
            _lastMergeTime = DateTime.Now;

            if (operationsToProcess.Count == 0) return;

            Debug.Log($"[待处理操作] 开始处理 {operationsToProcess.Count} 个待合并操作");

            // 智能合并操作
            var mergedOperations = MergeOperations(operationsToProcess);

            Debug.Log($"[操作合并] 合并后剩余 {mergedOperations.Count} 个操作 (原 {operationsToProcess.Count} 个)");

            // 🔥 关键改进：合并后的操作仍然使用智能步骤合并逻辑
            foreach (var operation in mergedOperations)
            {
                ProcessOperationImmediate(operation);
            }

            // 更新统计
            _performanceStats.MergedOperations += operationsToProcess.Count - mergedOperations.Count;
        }

        /// <summary>
        /// 智能合并操作 - 支持泛型FieldModifyOperation
        /// </summary>
        private List<IAtomicOperation> MergeOperations(List<IAtomicOperation> operations)
        {
            var merged = new List<IAtomicOperation>();
            var fieldModifyGroups = new Dictionary<string, List<IAtomicOperation>>();

            foreach (var operation in operations)
            {
                if (operation.Type == OperationType.FieldModify)
                {
                    string key = operation.GetMergeKey();
                    if (!string.IsNullOrEmpty(key))
                    {
                        if (!fieldModifyGroups.ContainsKey(key))
                        {
                            fieldModifyGroups[key] = new List<IAtomicOperation>();
                        }
                        fieldModifyGroups[key].Add(operation);
                    }
                    else
                    {
                        merged.Add(operation);
                    }
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
                    // 智能合并逻辑：按时间戳排序确保正确的合并顺序
                    var sortedGroup = group.OrderBy(op => op.Timestamp).ToList();
                    var first = sortedGroup[0];
                    var last = sortedGroup[sortedGroup.Count - 1];

                    // 尝试合并操作
                    var mergedOperation = TryMergeFieldOperations(first, last, sortedGroup.Count);
                    if (mergedOperation != null)
                    {
                        merged.Add(mergedOperation);
                    }
                }
            }

            return merged;
        }

        /// <summary>
        /// 尝试合并字段操作 - 支持泛型FieldModifyOperation
        /// </summary>
        private IAtomicOperation TryMergeFieldOperations(IAtomicOperation first, IAtomicOperation last, int operationCount)
        {
            try
            {
                var firstOldValue = first.GetOldValueString();
                var lastNewValue = last.GetNewValueString();
                var fieldPath = first.GetFieldPath();

                // 如果最终值等于初始值，则操作可以完全消除
                if (firstOldValue == lastNewValue)
                {
                    // 针对Position字段的特殊处理：即使回到原位置，如果有中间移动过程也记录为一次"移动并返回"操作
                    if (fieldPath == "Position" && operationCount > 2)
                    {
                        return CreateMergedPositionOperation(first, last, operationCount, true);
                    }
                    return null; // 其他情况跳过这个操作组
                }

                // 创建合并操作
                if (fieldPath == "Position")
                {
                    return CreateMergedPositionOperation(first, last, operationCount, false);
                }
                else
                {
                    return CreateMergedGenericOperation(first, last, operationCount);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"合并字段操作失败: {e.Message}");
                return first; // 失败时返回第一个操作
            }
        }

        /// <summary>
        /// 创建合并后的Position操作
        /// </summary>
        private IAtomicOperation CreateMergedPositionOperation(IAtomicOperation first, IAtomicOperation last, int operationCount, bool returnedToOriginal)
        {
            var node = first.GetNode();
            var firstOldValue = first.GetOldValueString();
            var lastNewValue = last.GetNewValueString();
            
            // 尝试解析为Vec2
            if (TryParseVec2(firstOldValue, out var oldPos) && TryParseVec2(lastNewValue, out var newPos))
            {
                var mergedOp = new FieldModifyOperation<Vec2>(
                    node, "Position", oldPos, newPos, 
                    first.GetType().GetProperty("GraphView")?.GetValue(first) as TreeNodeGraphView);
                
                if (returnedToOriginal)
                {
                    mergedOp.SetDescription($"节点位置移动（经过{operationCount}步最终返回原位置）");
                }
                else
                {
                    mergedOp.SetDescription($"节点位置变化（{operationCount}步操作已合并）: {oldPos} → {newPos}");
                }
                
                return mergedOp;
            }
            else
            {
                // 回退到字符串版本
                var mergedOp = new FieldModifyOperation<string>(
                    node, "Position", firstOldValue, lastNewValue,
                    first.GetType().GetProperty("GraphView")?.GetValue(first) as TreeNodeGraphView);
                
                mergedOp.SetDescription($"节点位置变化（{operationCount}步操作已合并）");
                return mergedOp;
            }
        }

        /// <summary>
        /// 创建合并后的通用操作
        /// </summary>
        private IAtomicOperation CreateMergedGenericOperation(IAtomicOperation first, IAtomicOperation last, int operationCount)
        {
            var node = first.GetNode();
            var fieldPath = first.GetFieldPath();
            var firstOldValue = first.GetOldValueString();
            var lastNewValue = last.GetNewValueString();
            
            var mergedOp = new FieldModifyOperation<string>(
                node, fieldPath, firstOldValue, lastNewValue,
                first.GetType().GetProperty("GraphView")?.GetValue(first) as TreeNodeGraphView);
            
            mergedOp.SetDescription($"字段修改（{operationCount}步操作已合并）: {fieldPath}");
            return mergedOp;
        }

        /// <summary>
        /// 尝试解析Vec2字符串
        /// </summary>
        private bool TryParseVec2(string vec2Str, out Vec2 result)
        {
            result = default;
            
            if (string.IsNullOrEmpty(vec2Str))
                return false;

            // 移除括号和空格
            vec2Str = vec2Str.Trim('(', ')', ' ');
            var parts = vec2Str.Split(',');

            if (parts.Length != 2)
                return false;

            if (float.TryParse(parts[0].Trim(), out var x) &&
                float.TryParse(parts[1].Trim(), out var y))
            {
                result = new Vec2(x, y);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 标记需要增量渲染的节点
        /// </summary>
        private void MarkForIncrementalRender(IAtomicOperation operation)
        {
            switch (operation.Type)
            {
                case OperationType.FieldModify:
                    var node = operation.GetNode();
                    if (node != null && TryGetViewNode(node, out var viewNode))
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

        /// <summary>
        /// 清理渲染状态
        /// </summary>
        private void ClearRenderingState()
        {
            _dirtyNodes.Clear();
            _needsFullRedraw = false;
        }
    }
}
