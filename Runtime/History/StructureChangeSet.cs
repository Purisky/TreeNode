using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 结构变更集合 - 管理多个树结构操作
    /// 提供变更冲突检测和优化功能
    /// </summary>
    public class StructureChangeSet
    {
        public List<TreeStructureOperation> StructureOperations { get; private set; } = new();
        public DateTime StartTime { get; private set; }
        public DateTime? EndTime { get; private set; }
        public JsonAsset Asset { get; private set; }
        
        /// <summary>
        /// 变更集合的总体影响范围
        /// </summary>
        public StructureImpactScope TotalImpactScope { get; private set; } = new();
        
        /// <summary>
        /// 是否包含冲突操作
        /// </summary>
        public bool HasConflicts { get; private set; } = false;
        
        /// <summary>
        /// 冲突详情
        /// </summary>
        public List<StructureConflict> Conflicts { get; private set; } = new();

        public StructureChangeSet(JsonAsset asset)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            StartTime = DateTime.Now;
        }

        /// <summary>
        /// 添加结构操作
        /// </summary>
        public void AddOperation(TreeStructureOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            StructureOperations.Add(operation);
            TotalImpactScope.Merge(operation.ImpactScope);
            
            // 检测冲突
            DetectConflicts();
        }

        /// <summary>
        /// 添加多个结构操作
        /// </summary>
        public void AddOperations(IEnumerable<TreeStructureOperation> operations)
        {
            if (operations == null)
            {
                return;
            }

            foreach (var operation in operations)
            {
                AddOperation(operation);
            }
        }

        /// <summary>
        /// 完成变更集合
        /// </summary>
        public void Complete()
        {
            EndTime = DateTime.Now;
            
            // 最终优化
            OptimizeOperations();
            
            // 最终冲突检测
            DetectConflicts();
        }

        /// <summary>
        /// 检测操作间的冲突
        /// </summary>
        private void DetectConflicts()
        {
            Conflicts.Clear();
            HasConflicts = false;

            for (int i = 0; i < StructureOperations.Count; i++)
            {
                for (int j = i + 1; j < StructureOperations.Count; j++)
                {
                    var conflict = DetectConflictBetween(StructureOperations[i], StructureOperations[j]);
                    if (conflict != null)
                    {
                        Conflicts.Add(conflict);
                        HasConflicts = true;
                    }
                }
            }
        }

        /// <summary>
        /// 检测两个操作间的冲突
        /// </summary>
        private StructureConflict DetectConflictBetween(TreeStructureOperation op1, TreeStructureOperation op2)
        {
            // 检查路径冲突
            var paths1 = GetAllAffectedPaths(op1);
            var paths2 = GetAllAffectedPaths(op2);
            
            var commonPaths = paths1.Intersect(paths2).ToList();
            if (commonPaths.Any())
            {
                return new StructureConflict
                {
                    Operation1 = op1,
                    Operation2 = op2,
                    ConflictType = StructureConflictType.PathOverlap,
                    ConflictPaths = commonPaths,
                    Description = $"Operations affect overlapping paths: {string.Join(", ", commonPaths.Take(3))}"
                };
            }

            // 检查父子关系冲突
            var parentChildConflict = DetectParentChildConflict(paths1, paths2);
            if (parentChildConflict.Any())
            {
                return new StructureConflict
                {
                    Operation1 = op1,
                    Operation2 = op2,
                    ConflictType = StructureConflictType.ParentChildDependency,
                    ConflictPaths = parentChildConflict,
                    Description = $"Operations have parent-child dependencies: {string.Join(", ", parentChildConflict.Take(3))}"
                };
            }

            return null;
        }

        /// <summary>
        /// 获取操作影响的所有路径
        /// </summary>
        private HashSet<PAPath> GetAllAffectedPaths(TreeStructureOperation operation)
        {
            var paths = new HashSet<PAPath>();
            
            foreach (var nodeOp in operation.NodeOperations)
            {
                if (nodeOp.From.HasValue)
                {
                    paths.Add(nodeOp.From.Value);
                }
                if (nodeOp.To.HasValue)
                {
                    paths.Add(nodeOp.To.Value);
                }
            }

            return paths;
        }

        /// <summary>
        /// 检测父子关系冲突
        /// </summary>
        private List<PAPath> DetectParentChildConflict(HashSet<PAPath> paths1, HashSet<PAPath> paths2)
        {
            var conflicts = new List<PAPath>();

            foreach (var path1 in paths1)
            {
                foreach (var path2 in paths2)
                {
                    if (path1.IsChildOf(path2) || path2.IsChildOf(path1))
                    {
                        conflicts.Add(path1);
                        conflicts.Add(path2);
                    }
                }
            }

            return conflicts.Distinct().ToList();
        }

        /// <summary>
        /// 优化操作序列
        /// </summary>
        private void OptimizeOperations()
        {
            if (StructureOperations.Count <= 1)
            {
                return;
            }

            // 合并可以合并的操作
            var optimized = new List<TreeStructureOperation>();
            var processed = new HashSet<TreeStructureOperation>();

            foreach (var operation in StructureOperations)
            {
                if (processed.Contains(operation))
                {
                    continue;
                }

                var mergeableOps = FindMergeableOperations(operation, StructureOperations, processed);
                if (mergeableOps.Count > 1)
                {
                    // 创建合并的操作
                    var mergedOp = MergeOperations(mergeableOps);
                    optimized.Add(mergedOp);
                    
                    foreach (var op in mergeableOps)
                    {
                        processed.Add(op);
                    }
                }
                else
                {
                    optimized.Add(operation);
                    processed.Add(operation);
                }
            }

            StructureOperations = optimized;
        }

        /// <summary>
        /// 查找可以合并的操作
        /// </summary>
        private List<TreeStructureOperation> FindMergeableOperations(TreeStructureOperation baseOp, 
            List<TreeStructureOperation> allOps, HashSet<TreeStructureOperation> processed)
        {
            var mergeable = new List<TreeStructureOperation> { baseOp };

            foreach (var otherOp in allOps)
            {
                if (processed.Contains(otherOp) || otherOp == baseOp)
                {
                    continue;
                }

                if (CanMergeOperations(baseOp, otherOp))
                {
                    mergeable.Add(otherOp);
                }
            }

            return mergeable;
        }

        /// <summary>
        /// 判断两个操作是否可以合并
        /// </summary>
        private bool CanMergeOperations(TreeStructureOperation op1, TreeStructureOperation op2)
        {
            // 相同类型的单个节点操作可以合并为批量操作
            if (op1.StructureType == TreeStructureOperationType.SingleNodeAdd && 
                op2.StructureType == TreeStructureOperationType.SingleNodeAdd)
            {
                return true;
            }

            if (op1.StructureType == TreeStructureOperationType.SingleNodeRemove && 
                op2.StructureType == TreeStructureOperationType.SingleNodeRemove)
            {
                return true;
            }

            if (op1.StructureType == TreeStructureOperationType.SingleNodeMove && 
                op2.StructureType == TreeStructureOperationType.SingleNodeMove)
            {
                return true;
            }

            // 批量操作和相同类型的单个操作可以合并
            if ((op1.StructureType == TreeStructureOperationType.BatchAdd && op2.StructureType == TreeStructureOperationType.SingleNodeAdd) ||
                (op1.StructureType == TreeStructureOperationType.SingleNodeAdd && op2.StructureType == TreeStructureOperationType.BatchAdd))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 合并多个操作
        /// </summary>
        private TreeStructureOperation MergeOperations(List<TreeStructureOperation> operations)
        {
            if (operations.Count == 1)
            {
                return operations[0];
            }

            var allNodeOps = new List<NodeOperation>();
            TreeStructureOperationType mergedType;

            // 确定合并后的类型
            var firstType = operations[0].StructureType;
            if (operations.All(op => op.StructureType == TreeStructureOperationType.SingleNodeAdd || 
                                   op.StructureType == TreeStructureOperationType.BatchAdd))
            {
                mergedType = TreeStructureOperationType.BatchAdd;
            }
            else if (operations.All(op => op.StructureType == TreeStructureOperationType.SingleNodeRemove || 
                                        op.StructureType == TreeStructureOperationType.BatchRemove))
            {
                mergedType = TreeStructureOperationType.BatchRemove;
            }
            else if (operations.All(op => op.StructureType == TreeStructureOperationType.SingleNodeMove || 
                                        op.StructureType == TreeStructureOperationType.BatchMove))
            {
                mergedType = TreeStructureOperationType.BatchMove;
            }
            else
            {
                mergedType = TreeStructureOperationType.StructureRebuild;
            }

            // 收集所有节点操作
            foreach (var operation in operations)
            {
                allNodeOps.AddRange(operation.NodeOperations);
            }

            return TreeStructureOperation.CreateBatch(allNodeOps, Asset, mergedType);
        }

        /// <summary>
        /// 解决冲突
        /// </summary>
        public void ResolveConflicts()
        {
            if (!HasConflicts)
            {
                return;
            }

            foreach (var conflict in Conflicts)
            {
                ResolveConflict(conflict);
            }

            // 重新检测冲突
            DetectConflicts();
        }

        /// <summary>
        /// 解决单个冲突
        /// </summary>
        private void ResolveConflict(StructureConflict conflict)
        {
            switch (conflict.ConflictType)
            {
                case StructureConflictType.PathOverlap:
                    ResolvePathOverlapConflict(conflict);
                    break;
                case StructureConflictType.ParentChildDependency:
                    ResolveParentChildConflict(conflict);
                    break;
            }
        }

        /// <summary>
        /// 解决路径重叠冲突
        /// </summary>
        private void ResolvePathOverlapConflict(StructureConflict conflict)
        {
            // 策略：合并冲突的操作
            var mergedOp = MergeOperations(new List<TreeStructureOperation> { conflict.Operation1, conflict.Operation2 });
            
            StructureOperations.Remove(conflict.Operation1);
            StructureOperations.Remove(conflict.Operation2);
            StructureOperations.Add(mergedOp);
        }

        /// <summary>
        /// 解决父子依赖冲突
        /// </summary>
        private void ResolveParentChildConflict(StructureConflict conflict)
        {
            // 策略：确保操作顺序正确（父操作在前）
            var op1Index = StructureOperations.IndexOf(conflict.Operation1);
            var op2Index = StructureOperations.IndexOf(conflict.Operation2);

            if (op1Index > op2Index)
            {
                // 交换顺序
                StructureOperations[op1Index] = conflict.Operation2;
                StructureOperations[op2Index] = conflict.Operation1;
            }
        }

        /// <summary>
        /// 获取优化后的操作序列
        /// </summary>
        public List<TreeStructureOperation> GetOptimizedOperations()
        {
            if (!HasConflicts)
            {
                return StructureOperations.ToList();
            }

            // 如果有冲突，先解决冲突
            ResolveConflicts();
            return StructureOperations.ToList();
        }

        public override string ToString()
        {
            var duration = EndTime.HasValue ? (EndTime.Value - StartTime).TotalMilliseconds : -1;
            var durationStr = duration >= 0 ? $"{duration:F1}ms" : "ongoing";
            
            return $"ChangeSet: {StructureOperations.Count} operations, {TotalImpactScope}, {durationStr}" +
                   (HasConflicts ? $", {Conflicts.Count} conflicts" : "");
        }
    }

    /// <summary>
    /// 结构冲突信息
    /// </summary>
    public class StructureConflict
    {
        public TreeStructureOperation Operation1 { get; set; }
        public TreeStructureOperation Operation2 { get; set; }
        public StructureConflictType ConflictType { get; set; }
        public List<PAPath> ConflictPaths { get; set; } = new();
        public string Description { get; set; }

        public override string ToString()
        {
            return $"Conflict[{ConflictType}]: {Description}";
        }
    }

    /// <summary>
    /// 结构冲突类型
    /// </summary>
    public enum StructureConflictType
    {
        PathOverlap,           // 路径重叠
        ParentChildDependency, // 父子依赖
        OperationOrder,        // 操作顺序
        ResourceContention     // 资源争用
    }
}
