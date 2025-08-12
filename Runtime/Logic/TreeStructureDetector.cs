using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 树结构变更检测器
    /// 监听 History 系统中的 NodeOperation 记录，分析树结构变更
    /// </summary>
    public class TreeStructureDetector
    {
        private readonly History _history;
        private readonly JsonAsset _asset;
        private readonly List<IStructureChangeListener> _listeners = new();
        
        /// <summary>
        /// 当前正在处理的变更集合
        /// </summary>
        private StructureChangeSet _currentChangeSet;
        
        /// <summary>
        /// 最后处理的 HistoryStep 索引
        /// </summary>
        private int _lastProcessedStepIndex = -1;

        public TreeStructureDetector(History history, JsonAsset asset)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        /// <summary>
        /// 添加变更监听器
        /// </summary>
        public void AddListener(IStructureChangeListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>
        /// 移除变更监听器
        /// </summary>
        public void RemoveListener(IStructureChangeListener listener)
        {
            if (listener != null)
            {
                _listeners.Remove(listener);
            }
        }

        /// <summary>
        /// 开始监听变更（进入批处理模式时调用）
        /// </summary>
        public void BeginChangeDetection()
        {
            if (_currentChangeSet != null)
            {
                // 如果已经有正在进行的变更集合，先完成它
                EndChangeDetection();
            }

            _currentChangeSet = new StructureChangeSet(_asset);
            _lastProcessedStepIndex = _history.Steps.Count - 1;
        }

        /// <summary>
        /// 结束变更检测（退出批处理模式时调用）
        /// </summary>
        public void EndChangeDetection()
        {
            if (_currentChangeSet == null)
            {
                return;
            }

            // 处理自上次检测以来的所有新步骤
            ProcessNewHistorySteps();

            // 完成变更集合
            _currentChangeSet.Complete();

            // 通知监听器
            if (_currentChangeSet.StructureOperations.Any())
            {
                NotifyStructureChanged(_currentChangeSet);
            }

            _currentChangeSet = null;
        }

        /// <summary>
        /// 实时检测变更（单个操作时调用）
        /// </summary>
        public void DetectSingleChange()
        {
            // 检查是否有新的 HistoryStep
            if (_history.Steps.Count > _lastProcessedStepIndex + 1)
            {
                var newSteps = _history.Steps.Skip(_lastProcessedStepIndex + 1).ToList();
                
                var singleChangeSet = new StructureChangeSet(_asset);
                
                foreach (var step in newSteps)
                {
                    ProcessHistoryStep(step, singleChangeSet);
                }

                singleChangeSet.Complete();
                _lastProcessedStepIndex = _history.Steps.Count - 1;

                // 通知监听器
                if (singleChangeSet.StructureOperations.Any())
                {
                    NotifyStructureChanged(singleChangeSet);
                }
            }
        }

        /// <summary>
        /// 处理新的历史步骤
        /// </summary>
        private void ProcessNewHistorySteps()
        {
            if (_currentChangeSet == null)
            {
                return;
            }

            var newSteps = _history.Steps.Skip(_lastProcessedStepIndex + 1).ToList();
            
            foreach (var step in newSteps)
            {
                ProcessHistoryStep(step, _currentChangeSet);
            }

            _lastProcessedStepIndex = _history.Steps.Count - 1;
        }

        /// <summary>
        /// 处理单个历史步骤
        /// </summary>
        private void ProcessHistoryStep(History.HistoryStep step, StructureChangeSet changeSet)
        {
            var nodeOperations = ExtractNodeOperations(step);
            
            if (nodeOperations.Any())
            {
                var structureOp = CreateStructureOperation(nodeOperations);
                if (structureOp != null)
                {
                    changeSet.AddOperation(structureOp);
                }
            }
        }

        /// <summary>
        /// 从历史步骤中提取节点操作
        /// </summary>
        private List<NodeOperation> ExtractNodeOperations(History.HistoryStep step)
        {
            var nodeOperations = new List<NodeOperation>();

            foreach (var operation in step.Operations)
            {
                if (operation is NodeOperation nodeOp)
                {
                    nodeOperations.Add(nodeOp);
                }
            }

            return nodeOperations;
        }

        /// <summary>
        /// 创建树结构操作
        /// </summary>
        private TreeStructureOperation CreateStructureOperation(List<NodeOperation> nodeOperations)
        {
            if (!nodeOperations.Any())
            {
                return null;
            }

            if (nodeOperations.Count == 1)
            {
                return TreeStructureOperation.CreateSingle(nodeOperations[0], _asset);
            }

            // 多个操作，判断是否可以创建批量操作
            var operationTypes = nodeOperations.Select(op => op.Type).Distinct().ToList();
            
            if (operationTypes.Count == 1)
            {
                // 单一类型的批量操作
                var batchType = operationTypes[0] switch
                {
                    OperationType.Create => TreeStructureOperationType.BatchAdd,
                    OperationType.Delete => TreeStructureOperationType.BatchRemove,
                    OperationType.Move => TreeStructureOperationType.BatchMove,
                    _ => TreeStructureOperationType.StructureRebuild
                };

                return TreeStructureOperation.CreateBatch(nodeOperations, _asset, batchType);
            }
            else
            {
                // 混合操作，创建结构重建操作
                return TreeStructureOperation.CreateBatch(nodeOperations, _asset, TreeStructureOperationType.StructureRebuild);
            }
        }

        /// <summary>
        /// 通知结构变更
        /// </summary>
        private void NotifyStructureChanged(StructureChangeSet changeSet)
        {
            foreach (var listener in _listeners)
            {
                try
                {
                    listener.OnStructureChanged(changeSet);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error notifying structure change listener: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 强制刷新检测
        /// </summary>
        public void ForceRefresh()
        {
            _lastProcessedStepIndex = -1;
            DetectSingleChange();
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetDetectionStats()
        {
            var totalSteps = _history.Steps.Count;
            var processedSteps = _lastProcessedStepIndex + 1;
            var pendingSteps = Math.Max(0, totalSteps - processedSteps);
            
            return $"Detection Stats: {processedSteps}/{totalSteps} steps processed, {pendingSteps} pending" +
                   (_currentChangeSet != null ? $", Active ChangeSet: {_currentChangeSet}" : "");
        }
    }

    /// <summary>
    /// 结构变更监听器接口
    /// </summary>
    public interface IStructureChangeListener
    {
        void OnStructureChanged(StructureChangeSet changeSet);
    }

    /// <summary>
    /// 结构影响分析器
    /// 计算结构变更的影响范围和最优重建策略
    /// </summary>
    public class StructureImpactAnalyzer
    {
        private readonly JsonAsset _asset;
        private readonly JsonNodeTree _nodeTree;

        public StructureImpactAnalyzer(JsonAsset asset, JsonNodeTree nodeTree)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _nodeTree = nodeTree ?? throw new ArgumentNullException(nameof(nodeTree));
        }

        /// <summary>
        /// 分析变更集合的影响
        /// </summary>
        public StructureImpactAnalysis AnalyzeImpact(StructureChangeSet changeSet)
        {
            if (changeSet == null)
            {
                return new StructureImpactAnalysis();
            }

            var analysis = new StructureImpactAnalysis
            {
                ChangeSet = changeSet,
                TotalOperationCount = changeSet.StructureOperations.Sum(op => op.NodeOperations.Count)
            };

            // 分析每个操作的影响
            foreach (var structureOp in changeSet.StructureOperations)
            {
                AnalyzeOperationImpact(structureOp, analysis);
            }

            // 计算最优重建策略
            CalculateOptimalRebuildStrategy(analysis);

            // 评估性能影响
            EstimatePerformanceImpact(analysis);

            return analysis;
        }

        /// <summary>
        /// 分析单个操作的影响
        /// </summary>
        private void AnalyzeOperationImpact(TreeStructureOperation operation, StructureImpactAnalysis analysis)
        {
            foreach (var nodeOp in operation.NodeOperations)
            {
                switch (nodeOp.Type)
                {
                    case OperationType.Create:
                        AnalyzeCreateImpact(nodeOp, analysis);
                        break;
                    case OperationType.Delete:
                        AnalyzeDeleteImpact(nodeOp, analysis);
                        break;
                    case OperationType.Move:
                        AnalyzeMoveImpact(nodeOp, analysis);
                        break;
                }
            }
        }

        /// <summary>
        /// 分析节点创建的影响
        /// </summary>
        private void AnalyzeCreateImpact(NodeOperation nodeOp, StructureImpactAnalysis analysis)
        {
            if (!nodeOp.To.HasValue)
            {
                return;
            }

            var targetPath = nodeOp.To.Value;
            analysis.AffectedPaths.Add(targetPath);
            
            // 创建操作影响父节点的子节点列表
            var parentPath = targetPath.GetParent();
            if (!parentPath.IsEmpty)
            {
                analysis.AffectedPaths.Add(parentPath);
                analysis.ParentChildRelationshipChanges++;
            }

            analysis.NodesAdded++;
        }

        /// <summary>
        /// 分析节点删除的影响
        /// </summary>
        private void AnalyzeDeleteImpact(NodeOperation nodeOp, StructureImpactAnalysis analysis)
        {
            if (!nodeOp.From.HasValue)
            {
                return;
            }

            var sourcePath = nodeOp.From.Value;
            analysis.AffectedPaths.Add(sourcePath);
            
            // 删除操作影响父节点的子节点列表
            var parentPath = sourcePath.GetParent();
            if (!parentPath.IsEmpty)
            {
                analysis.AffectedPaths.Add(parentPath);
                analysis.ParentChildRelationshipChanges++;
            }

            // 删除操作还会影响所有子节点
            var childrenCount = CountDescendantNodes(sourcePath);
            analysis.NodesRemoved += 1 + childrenCount;
            analysis.DescendantNodesAffected += childrenCount;
        }

        /// <summary>
        /// 分析节点移动的影响
        /// </summary>
        private void AnalyzeMoveImpact(NodeOperation nodeOp, StructureImpactAnalysis analysis)
        {
            if (!nodeOp.From.HasValue || !nodeOp.To.HasValue)
            {
                return;
            }

            var sourcePath = nodeOp.From.Value;
            var targetPath = nodeOp.To.Value;
            
            analysis.AffectedPaths.Add(sourcePath);
            analysis.AffectedPaths.Add(targetPath);
            
            // 移动操作影响源父节点和目标父节点
            var sourceParent = sourcePath.GetParent();
            var targetParent = targetPath.GetParent();
            
            if (!sourceParent.IsEmpty)
            {
                analysis.AffectedPaths.Add(sourceParent);
                analysis.ParentChildRelationshipChanges++;
            }
            
            if (!targetParent.IsEmpty && !targetParent.Equals(sourceParent))
            {
                analysis.AffectedPaths.Add(targetParent);
                analysis.ParentChildRelationshipChanges++;
            }

            analysis.NodesMoved++;
        }

        /// <summary>
        /// 计算最优重建策略
        /// </summary>
        private void CalculateOptimalRebuildStrategy(StructureImpactAnalysis analysis)
        {
            var totalNodes = GetTotalNodeCount();
            var affectedRatio = (double)analysis.AffectedPaths.Count / totalNodes;

            if (affectedRatio > 0.5) // 50%以上的节点受影响
            {
                analysis.RecommendedStrategy = RebuildStrategy.FullTreeRebuild;
                analysis.StrategyReason = "More than 50% of nodes are affected";
            }
            else if (analysis.ParentChildRelationshipChanges > 10) // 大量父子关系变更
            {
                analysis.RecommendedStrategy = RebuildStrategy.RegionalRebuild;
                analysis.StrategyReason = "High number of parent-child relationship changes";
            }
            else if (analysis.AffectedPaths.Count > 20) // 影响路径较多
            {
                analysis.RecommendedStrategy = RebuildStrategy.RegionalRebuild;
                analysis.StrategyReason = "Large number of affected paths";
            }
            else
            {
                analysis.RecommendedStrategy = RebuildStrategy.IncrementalUpdate;
                analysis.StrategyReason = "Localized changes suitable for incremental update";
            }

            // 计算需要重建的区域
            analysis.RebuildRegions = CalculateRebuildRegions(analysis.AffectedPaths);
        }

        /// <summary>
        /// 计算重建区域
        /// </summary>
        private List<PAPath> CalculateRebuildRegions(HashSet<PAPath> affectedPaths)
        {
            if (!affectedPaths.Any())
            {
                return new List<PAPath>();
            }

            // 找到最小公共祖先
            var sortedPaths = affectedPaths.OrderBy(p => p.Depth).ToList();
            var regions = new List<PAPath>();

            foreach (var path in sortedPaths)
            {
                // 检查是否已经被某个祖先区域包含
                bool isContained = regions.Any(region => path.IsChildOf(region));
                
                if (!isContained)
                {
                    // 移除被当前路径包含的子区域
                    regions.RemoveAll(region => region.IsChildOf(path));
                    regions.Add(path);
                }
            }

            return regions;
        }

        /// <summary>
        /// 评估性能影响
        /// </summary>
        private void EstimatePerformanceImpact(StructureImpactAnalysis analysis)
        {
            var totalNodes = GetTotalNodeCount();
            
            // 基于经验公式估算
            switch (analysis.RecommendedStrategy)
            {
                case RebuildStrategy.IncrementalUpdate:
                    analysis.EstimatedRebuildTimeMs = analysis.AffectedPaths.Count * 0.5; // 0.5ms per affected path
                    analysis.EstimatedMemoryImpactMB = analysis.AffectedPaths.Count * 0.001; // 1KB per affected path
                    break;
                    
                case RebuildStrategy.RegionalRebuild:
                    var regionalNodes = analysis.RebuildRegions.Sum(region => CountNodesInRegion(region));
                    analysis.EstimatedRebuildTimeMs = regionalNodes * 0.2; // 0.2ms per node
                    analysis.EstimatedMemoryImpactMB = regionalNodes * 0.002; // 2KB per node
                    break;
                    
                case RebuildStrategy.FullTreeRebuild:
                    analysis.EstimatedRebuildTimeMs = totalNodes * 0.1; // 0.1ms per node
                    analysis.EstimatedMemoryImpactMB = totalNodes * 0.003; // 3KB per node
                    break;
            }

            // 性能评估等级
            if (analysis.EstimatedRebuildTimeMs < 10)
            {
                analysis.PerformanceImpact = PerformanceImpactLevel.Low;
            }
            else if (analysis.EstimatedRebuildTimeMs < 50)
            {
                analysis.PerformanceImpact = PerformanceImpactLevel.Medium;
            }
            else
            {
                analysis.PerformanceImpact = PerformanceImpactLevel.High;
            }
        }

        /// <summary>
        /// 获取总节点数
        /// </summary>
        private int GetTotalNodeCount()
        {
            return _nodeTree.TotalNodeCount;
        }

        /// <summary>
        /// 计算后代节点数量
        /// </summary>
        private int CountDescendantNodes(PAPath path)
        {
            try
            {
                var node = PropertyAccessor.GetValue<JsonNode>(_asset.Data, path);
                if (node == null)
                {
                    return 0;
                }

                var nodeList = new List<(PAPath path, JsonNode node)>();
                PropertyAccessor.CollectNodes(node, nodeList, path, depth: -1);
                return nodeList.Count - 1; // 排除自身
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 计算区域内的节点数量
        /// </summary>
        private int CountNodesInRegion(PAPath regionPath)
        {
            return CountDescendantNodes(regionPath) + 1; // 包括区域根节点
        }
    }

    /// <summary>
    /// 结构影响分析结果
    /// </summary>
    public class StructureImpactAnalysis
    {
        public StructureChangeSet ChangeSet { get; set; }
        public HashSet<PAPath> AffectedPaths { get; set; } = new();
        public int TotalOperationCount { get; set; }
        public int NodesAdded { get; set; }
        public int NodesRemoved { get; set; }
        public int NodesMoved { get; set; }
        public int ParentChildRelationshipChanges { get; set; }
        public int DescendantNodesAffected { get; set; }
        
        public RebuildStrategy RecommendedStrategy { get; set; }
        public string StrategyReason { get; set; }
        public List<PAPath> RebuildRegions { get; set; } = new();
        
        public double EstimatedRebuildTimeMs { get; set; }
        public double EstimatedMemoryImpactMB { get; set; }
        public PerformanceImpactLevel PerformanceImpact { get; set; }

        public override string ToString()
        {
            return $"Impact: {AffectedPaths.Count} paths, +{NodesAdded}/-{NodesRemoved}/~{NodesMoved} nodes, " +
                   $"Strategy: {RecommendedStrategy}, Est: {EstimatedRebuildTimeMs:F1}ms";
        }
    }

    /// <summary>
    /// 重建策略
    /// </summary>
    public enum RebuildStrategy
    {
        IncrementalUpdate,  // 增量更新
        RegionalRebuild,    // 区域重建
        FullTreeRebuild     // 全树重建
    }

    /// <summary>
    /// 性能影响等级
    /// </summary>
    public enum PerformanceImpactLevel
    {
        Low,     // 低影响
        Medium,  // 中等影响
        High     // 高影响
    }
}
