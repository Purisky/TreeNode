using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 树结构变更操作类型枚举
    /// </summary>
    public enum TreeStructureOperationType
    {
        SingleNodeAdd,      // 单个节点添加
        SingleNodeRemove,   // 单个节点删除
        SingleNodeMove,     // 单个节点移动
        BatchAdd,          // 批量节点添加
        BatchRemove,       // 批量节点删除
        BatchMove,         // 批量节点移动
        StructureRebuild   // 结构重建
    }

    /// <summary>
    /// 树结构变更操作 - 扩展现有 NodeOperation
    /// 专注于树结构变更，不涉及节点内部属性
    /// </summary>
    public class TreeStructureOperation : IAtomicOperation
    {
        public TreeStructureOperationType StructureType { get; set; }
        public List<NodeOperation> NodeOperations { get; private set; } = new();
        public JsonAsset Asset { get; set; }
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 影响范围信息
        /// </summary>
        public StructureImpactScope ImpactScope { get; set; }

        public TreeStructureOperation()
        {
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 创建单个节点操作
        /// </summary>
        public static TreeStructureOperation CreateSingle(NodeOperation nodeOp, JsonAsset asset)
        {
            if (nodeOp == null || asset == null)
            {
                return null;
            }

            var structureOp = new TreeStructureOperation
            {
                Asset = asset,
                ImpactScope = CalculateImpactScope(nodeOp)
            };

            structureOp.NodeOperations.Add(nodeOp);
            
            structureOp.StructureType = nodeOp.Type switch
            {
                OperationType.Create => TreeStructureOperationType.SingleNodeAdd,
                OperationType.Delete => TreeStructureOperationType.SingleNodeRemove,
                OperationType.Move => TreeStructureOperationType.SingleNodeMove,
                _ => throw new ArgumentException($"Unsupported operation type: {nodeOp.Type}")
            };

            return structureOp;
        }

        /// <summary>
        /// 创建批量操作
        /// </summary>
        public static TreeStructureOperation CreateBatch(IEnumerable<NodeOperation> nodeOps, JsonAsset asset, TreeStructureOperationType batchType)
        {
            if (nodeOps == null || !nodeOps.Any() || asset == null)
            {
                return null;
            }

            var structureOp = new TreeStructureOperation
            {
                Asset = asset,
                StructureType = batchType
            };

            structureOp.NodeOperations.AddRange(nodeOps);
            structureOp.ImpactScope = CalculateBatchImpactScope(nodeOps);

            return structureOp;
        }

        /// <summary>
        /// 添加节点操作到批量操作中
        /// </summary>
        public void AddNodeOperation(NodeOperation nodeOp)
        {
            if (nodeOp == null)
            {
                return;
            }

            NodeOperations.Add(nodeOp);
            
            // 重新计算影响范围
            ImpactScope = CalculateBatchImpactScope(NodeOperations);
        }

        /// <summary>
        /// 执行操作
        /// </summary>
        public List<ViewChange> Execute()
        {
            var changes = new List<ViewChange>();
            
            // 按照优化的顺序执行操作
            var optimizedOperations = OptimizeOperationOrder(NodeOperations);
            
            foreach (var nodeOp in optimizedOperations)
            {
                changes.AddRange(nodeOp.Execute());
            }

            return changes;
        }

        /// <summary>
        /// 撤销操作
        /// </summary>
        public List<ViewChange> Undo()
        {
            var changes = new List<ViewChange>();
            
            // 撤销时需要反向执行
            for (int i = NodeOperations.Count - 1; i >= 0; i--)
            {
                changes.AddRange(NodeOperations[i].Undo());
            }

            return changes;
        }

        /// <summary>
        /// 优化操作顺序以减少重建次数
        /// </summary>
        private List<NodeOperation> OptimizeOperationOrder(List<NodeOperation> operations)
        {
            // 策略：先执行删除，再执行移动，最后执行添加
            var deleteOps = operations.Where(op => op.Type == OperationType.Delete).ToList();
            var moveOps = operations.Where(op => op.Type == OperationType.Move).ToList();
            var createOps = operations.Where(op => op.Type == OperationType.Create).ToList();

            var result = new List<NodeOperation>();
            result.AddRange(deleteOps);
            result.AddRange(moveOps);
            result.AddRange(createOps);

            return result;
        }

        /// <summary>
        /// 计算单个操作的影响范围
        /// </summary>
        private static StructureImpactScope CalculateImpactScope(NodeOperation nodeOp)
        {
            var scope = new StructureImpactScope();
            
            // 根据操作类型和路径计算影响范围
            switch (nodeOp.Type)
            {
                case OperationType.Create:
                    if (nodeOp.To.HasValue)
                    {
                        scope.AddAffectedPath(nodeOp.To.Value);
                    }
                    break;
                case OperationType.Delete:
                    if (nodeOp.From.HasValue)
                    {
                        scope.AddAffectedPath(nodeOp.From.Value);
                    }
                    break;
                case OperationType.Move:
                    if (nodeOp.From.HasValue)
                    {
                        scope.AddAffectedPath(nodeOp.From.Value);
                    }
                    if (nodeOp.To.HasValue)
                    {
                        scope.AddAffectedPath(nodeOp.To.Value);
                    }
                    break;
            }

            return scope;
        }

        /// <summary>
        /// 计算批量操作的影响范围
        /// </summary>
        private static StructureImpactScope CalculateBatchImpactScope(IEnumerable<NodeOperation> operations)
        {
            var scope = new StructureImpactScope();
            
            foreach (var nodeOp in operations)
            {
                var singleScope = CalculateImpactScope(nodeOp);
                scope.Merge(singleScope);
            }

            return scope;
        }

        public override string ToString()
        {
            var operationCount = NodeOperations.Count;
            var nodeNames = string.Join(", ", NodeOperations.Take(3).Select(op => op.Node?.GetType().Name ?? "Unknown"));
            var suffix = operationCount > 3 ? $" (and {operationCount - 3} more)" : "";
            
            return $"TreeStructure[{StructureType}]: {operationCount} operations ({nodeNames}{suffix})";
        }
    }

    /// <summary>
    /// 结构影响范围
    /// </summary>
    public class StructureImpactScope
    {
        public HashSet<PAPath> AffectedPaths { get; private set; } = new();
        public int MinDepth { get; private set; } = int.MaxValue;
        public int MaxDepth { get; private set; } = int.MinValue;
        public bool IsFullTreeImpact { get; private set; } = false;

        /// <summary>
        /// 添加受影响的路径
        /// </summary>
        public void AddAffectedPath(PAPath path)
        {
            AffectedPaths.Add(path);
            
            var depth = path.Depth;
            if (depth < MinDepth)
            {
                MinDepth = depth;
            }
            if (depth > MaxDepth)
            {
                MaxDepth = depth;
            }

            // 如果影响范围太大，标记为全树影响
            if (AffectedPaths.Count > 100) // 阈值可配置
            {
                IsFullTreeImpact = true;
            }
        }

        /// <summary>
        /// 合并另一个影响范围
        /// </summary>
        public void Merge(StructureImpactScope other)
        {
            if (other == null)
            {
                return;
            }

            foreach (var path in other.AffectedPaths)
            {
                AddAffectedPath(path);
            }

            if (other.IsFullTreeImpact)
            {
                IsFullTreeImpact = true;
            }
        }

        /// <summary>
        /// 获取需要重建的最小公共祖先路径
        /// </summary>
        public List<PAPath> GetMinimalRebuildPaths()
        {
            if (IsFullTreeImpact || AffectedPaths.Count == 0)
            {
                return new List<PAPath> { PAPath.Empty }; // 全树重建
            }

            // 计算最小公共祖先路径集合
            var result = new List<PAPath>();
            var sortedPaths = AffectedPaths.OrderBy(p => p.Depth).ThenBy(p => p.ToString()).ToList();
            
            foreach (var path in sortedPaths)
            {
                // 检查是否已经被某个祖先路径包含
                bool isContained = result.Any(ancestor => path.IsChildOf(ancestor));
                
                if (!isContained)
                {
                    // 移除被当前路径包含的子路径
                    result.RemoveAll(child => child.IsChildOf(path));
                    result.Add(path);
                }
            }

            return result;
        }

        public override string ToString()
        {
            if (IsFullTreeImpact)
            {
                return "FullTree Impact";
            }

            var pathCount = AffectedPaths.Count;
            var depthRange = MinDepth == MaxDepth ? $"Depth {MinDepth}" : $"Depth {MinDepth}-{MaxDepth}";
            
            return $"Paths: {pathCount}, {depthRange}";
        }
    }
}
