using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 树结构脏区域 - 扩展 ViewChange 支持树结构脏标记
    /// 专门标记树结构变更区域，区分结构变更和内容变更
    /// </summary>
    public struct TreeStructureDirtyRegion
    {
        /// <summary>
        /// 脏区域的根路径
        /// </summary>
        public PAPath RootPath { get; set; }
        
        /// <summary>
        /// 脏区域的深度（从根路径开始）
        /// </summary>
        public int Depth { get; set; }
        
        /// <summary>
        /// 结构变更类型
        /// </summary>
        public TreeStructureChangeType ChangeType { get; set; }
        
        /// <summary>
        /// 受影响的节点
        /// </summary>
        public JsonNode AffectedNode { get; set; }
        
        /// <summary>
        /// 变更时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// 是否需要重建父子关系
        /// </summary>
        public bool RequiresParentChildRebuild { get; set; }
        
        /// <summary>
        /// 是否需要重建路径信息
        /// </summary>
        public bool RequiresPathRebuild { get; set; }
        
        /// <summary>
        /// 是否需要重建渲染顺序
        /// </summary>
        public bool RequiresRenderOrderRebuild { get; set; }

        /// <summary>
        /// 优先级（用于合并和处理顺序）
        /// </summary>
        public int Priority { get; set; }

        public TreeStructureDirtyRegion(PAPath rootPath, TreeStructureChangeType changeType, JsonNode affectedNode)
        {
            RootPath = rootPath;
            ChangeType = changeType;
            AffectedNode = affectedNode;
            Timestamp = DateTime.Now;
            Depth = CalculateAffectedDepth(changeType);
            
            // 根据变更类型设置重建需求
            RequiresParentChildRebuild = changeType != TreeStructureChangeType.PathOnly;
            RequiresPathRebuild = true;
            RequiresRenderOrderRebuild = changeType == TreeStructureChangeType.Add || 
                                       changeType == TreeStructureChangeType.Move ||
                                       changeType == TreeStructureChangeType.Reorder;
            
            Priority = CalculatePriority(changeType);
        }

        /// <summary>
        /// 计算受影响的深度
        /// </summary>
        private static int CalculateAffectedDepth(TreeStructureChangeType changeType)
        {
            return changeType switch
            {
                TreeStructureChangeType.Add => 1,      // 只影响当前层级
                TreeStructureChangeType.Remove => -1,   // 影响所有子层级
                TreeStructureChangeType.Move => 2,     // 影响源和目标层级
                TreeStructureChangeType.Reorder => 1,  // 只影响当前层级
                TreeStructureChangeType.PathOnly => 0, // 仅路径信息
                _ => 1
            };
        }

        /// <summary>
        /// 计算优先级
        /// </summary>
        private static int CalculatePriority(TreeStructureChangeType changeType)
        {
            return changeType switch
            {
                TreeStructureChangeType.Remove => 1,   // 删除优先级最高
                TreeStructureChangeType.Move => 2,     // 移动次之
                TreeStructureChangeType.Add => 3,      // 添加再次之
                TreeStructureChangeType.Reorder => 4,  // 重排序最低
                TreeStructureChangeType.PathOnly => 5, // 路径更新最低
                _ => 3
            };
        }

        /// <summary>
        /// 检查是否与另一个脏区域重叠
        /// </summary>
        public bool OverlapsWith(TreeStructureDirtyRegion other)
        {
            // 检查路径重叠
            if (RootPath.Equals(other.RootPath))
            {
                return true;
            }

            // 检查父子关系
            if (RootPath.IsChildOf(other.RootPath) || other.RootPath.IsChildOf(RootPath))
            {
                return true;
            }

            // 检查相邻路径（同一父节点下的兄弟节点）
            var thisParent = RootPath.GetParent();
            var otherParent = other.RootPath.GetParent();
            
            return !thisParent.IsEmpty && !otherParent.IsEmpty && thisParent.Equals(otherParent);
        }

        /// <summary>
        /// 合并另一个脏区域
        /// </summary>
        public TreeStructureDirtyRegion MergeWith(TreeStructureDirtyRegion other)
        {
            // 选择影响范围更大的根路径
            var mergedRootPath = RootPath.Depth <= other.RootPath.Depth ? RootPath : other.RootPath;
            
            // 选择更高优先级的变更类型
            var mergedChangeType = Priority <= other.Priority ? ChangeType : other.ChangeType;
            
            // 选择更早的时间戳
            var mergedTimestamp = Timestamp < other.Timestamp ? Timestamp : other.Timestamp;

            var merged = new TreeStructureDirtyRegion(mergedRootPath, mergedChangeType, AffectedNode ?? other.AffectedNode)
            {
                Timestamp = mergedTimestamp,
                Depth = Math.Max(Math.Abs(Depth), Math.Abs(other.Depth)),
                RequiresParentChildRebuild = RequiresParentChildRebuild || other.RequiresParentChildRebuild,
                RequiresPathRebuild = RequiresPathRebuild || other.RequiresPathRebuild,
                RequiresRenderOrderRebuild = RequiresRenderOrderRebuild || other.RequiresRenderOrderRebuild
            };

            return merged;
        }

        /// <summary>
        /// 转换为 ViewChange
        /// </summary>
        public ViewChange ToViewChange()
        {
            var viewChangeType = ChangeType switch
            {
                TreeStructureChangeType.Add => ViewChangeType.NodeCreate,
                TreeStructureChangeType.Remove => ViewChangeType.NodeDelete,
                TreeStructureChangeType.Move => ViewChangeType.EdgeCreate, // 移动会产生新的边
                _ => ViewChangeType.NodeField
            };

            return new ViewChange(viewChangeType, AffectedNode, RootPath);
        }

        public override string ToString()
        {
            var rebuildFlags = new List<string>();
            if (RequiresParentChildRebuild) rebuildFlags.Add("PC");
            if (RequiresPathRebuild) rebuildFlags.Add("Path");
            if (RequiresRenderOrderRebuild) rebuildFlags.Add("Order");
            
            var flagsStr = rebuildFlags.Any() ? $"[{string.Join(",", rebuildFlags)}]" : "";
            
            return $"DirtyRegion: {RootPath} {ChangeType} Depth:{Depth} {flagsStr}";
        }
    }

    /// <summary>
    /// 树结构变更类型
    /// </summary>
    public enum TreeStructureChangeType
    {
        Add,        // 节点添加
        Remove,     // 节点删除
        Move,       // 节点移动
        Reorder,    // 节点重排序
        PathOnly    // 仅路径信息变更
    }

    /// <summary>
    /// 结构脏区域管理器
    /// 管理多个结构脏区域的合并，计算最优的树层次关系重建策略
    /// </summary>
    public class StructureDirtyManager
    {
        private readonly List<TreeStructureDirtyRegion> _dirtyRegions = new();
        private readonly JsonAsset _asset;
        private readonly History _history;
        private readonly object _lockObject = new object();

        /// <summary>
        /// 脏区域合并的最大数量阈值
        /// </summary>
        public int MaxRegionsBeforeMerge { get; set; } = 50;

        /// <summary>
        /// 脏区域的最大存活时间（毫秒）
        /// </summary>
        public int MaxRegionLifetimeMs { get; set; } = 5000;

        public StructureDirtyManager(JsonAsset asset, History history)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        /// <summary>
        /// 添加脏区域
        /// </summary>
        public void AddDirtyRegion(TreeStructureDirtyRegion region)
        {
            lock (_lockObject)
            {
                _dirtyRegions.Add(region);
                
                // 如果脏区域过多，触发合并
                if (_dirtyRegions.Count > MaxRegionsBeforeMerge)
                {
                    MergeDirtyRegions();
                }
            }
        }

        /// <summary>
        /// 添加多个脏区域
        /// </summary>
        public void AddDirtyRegions(IEnumerable<TreeStructureDirtyRegion> regions)
        {
            if (regions == null)
            {
                return;
            }

            lock (_lockObject)
            {
                _dirtyRegions.AddRange(regions);
                
                if (_dirtyRegions.Count > MaxRegionsBeforeMerge)
                {
                    MergeDirtyRegions();
                }
            }
        }

        /// <summary>
        /// 从 NodeOperation 创建脏区域
        /// </summary>
        public void AddFromNodeOperation(NodeOperation nodeOp)
        {
            if (nodeOp == null)
            {
                return;
            }

            var regions = new List<TreeStructureDirtyRegion>();

            switch (nodeOp.Type)
            {
                case OperationType.Create:
                    if (nodeOp.To.HasValue)
                    {
                        regions.Add(new TreeStructureDirtyRegion(
                            nodeOp.To.Value, 
                            TreeStructureChangeType.Add, 
                            nodeOp.Node));
                        
                        // 父节点也受影响
                        var parentPath = nodeOp.To.Value.GetParent();
                        if (!parentPath.IsEmpty)
                        {
                            regions.Add(new TreeStructureDirtyRegion(
                                parentPath, 
                                TreeStructureChangeType.Reorder, 
                                null));
                        }
                    }
                    break;

                case OperationType.Delete:
                    if (nodeOp.From.HasValue)
                    {
                        regions.Add(new TreeStructureDirtyRegion(
                            nodeOp.From.Value, 
                            TreeStructureChangeType.Remove, 
                            nodeOp.Node));
                        
                        // 父节点也受影响
                        var parentPath = nodeOp.From.Value.GetParent();
                        if (!parentPath.IsEmpty)
                        {
                            regions.Add(new TreeStructureDirtyRegion(
                                parentPath, 
                                TreeStructureChangeType.Reorder, 
                                null));
                        }
                    }
                    break;

                case OperationType.Move:
                    if (nodeOp.From.HasValue && nodeOp.To.HasValue)
                    {
                        // 源位置
                        regions.Add(new TreeStructureDirtyRegion(
                            nodeOp.From.Value, 
                            TreeStructureChangeType.Remove, 
                            nodeOp.Node));
                        
                        // 目标位置
                        regions.Add(new TreeStructureDirtyRegion(
                            nodeOp.To.Value, 
                            TreeStructureChangeType.Add, 
                            nodeOp.Node));
                        
                        // 源父节点
                        var sourceParent = nodeOp.From.Value.GetParent();
                        if (!sourceParent.IsEmpty)
                        {
                            regions.Add(new TreeStructureDirtyRegion(
                                sourceParent, 
                                TreeStructureChangeType.Reorder, 
                                null));
                        }
                        
                        // 目标父节点（如果不同）
                        var targetParent = nodeOp.To.Value.GetParent();
                        if (!targetParent.IsEmpty && !targetParent.Equals(sourceParent))
                        {
                            regions.Add(new TreeStructureDirtyRegion(
                                targetParent, 
                                TreeStructureChangeType.Reorder, 
                                null));
                        }
                    }
                    break;
            }

            AddDirtyRegions(regions);
        }

        /// <summary>
        /// 合并脏区域
        /// </summary>
        public void MergeDirtyRegions()
        {
            lock (_lockObject)
            {
                if (_dirtyRegions.Count <= 1)
                {
                    return;
                }

                var merged = new List<TreeStructureDirtyRegion>();
                var processed = new HashSet<int>();

                for (int i = 0; i < _dirtyRegions.Count; i++)
                {
                    if (processed.Contains(i))
                    {
                        continue;
                    }

                    var currentRegion = _dirtyRegions[i];
                    var mergeCandidates = new List<int> { i };

                    // 查找可以合并的区域
                    for (int j = i + 1; j < _dirtyRegions.Count; j++)
                    {
                        if (processed.Contains(j))
                        {
                            continue;
                        }

                        if (currentRegion.OverlapsWith(_dirtyRegions[j]))
                        {
                            mergeCandidates.Add(j);
                        }
                    }

                    // 合并找到的区域
                    if (mergeCandidates.Count > 1)
                    {
                        var mergedRegion = currentRegion;
                        for (int k = 1; k < mergeCandidates.Count; k++)
                        {
                            mergedRegion = mergedRegion.MergeWith(_dirtyRegions[mergeCandidates[k]]);
                        }
                        merged.Add(mergedRegion);
                        
                        foreach (var index in mergeCandidates)
                        {
                            processed.Add(index);
                        }
                    }
                    else
                    {
                        merged.Add(currentRegion);
                        processed.Add(i);
                    }
                }

                _dirtyRegions.Clear();
                _dirtyRegions.AddRange(merged);
            }
        }

        /// <summary>
        /// 清理过期的脏区域
        /// </summary>
        public void CleanupExpiredRegions()
        {
            lock (_lockObject)
            {
                var cutoffTime = DateTime.Now.AddMilliseconds(-MaxRegionLifetimeMs);
                _dirtyRegions.RemoveAll(region => region.Timestamp < cutoffTime);
            }
        }

        /// <summary>
        /// 获取优化的重建指令
        /// </summary>
        public List<TreeRebuildInstruction> GetOptimizedRebuildInstructions()
        {
            lock (_lockObject)
            {
                // 清理过期区域
                CleanupExpiredRegions();
                
                // 合并重叠区域
                MergeDirtyRegions();

                var instructions = new List<TreeRebuildInstruction>();

                // 按优先级排序
                var sortedRegions = _dirtyRegions
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.RootPath.Depth)
                    .ToList();

                foreach (var region in sortedRegions)
                {
                    var instruction = CreateRebuildInstruction(region);
                    if (instruction != null)
                    {
                        instructions.Add(instruction);
                    }
                }

                return instructions;
            }
        }

        /// <summary>
        /// 创建重建指令
        /// </summary>
        private TreeRebuildInstruction CreateRebuildInstruction(TreeStructureDirtyRegion region)
        {
            var scope = DetermineRebuildScope(region);
            
            return new TreeRebuildInstruction
            {
                RootPath = region.RootPath,
                Scope = scope,
                RequiresParentChildRebuild = region.RequiresParentChildRebuild,
                RequiresPathRebuild = region.RequiresPathRebuild,
                RequiresRenderOrderRebuild = region.RequiresRenderOrderRebuild,
                Priority = region.Priority,
                EstimatedNodeCount = EstimateAffectedNodeCount(region.RootPath, scope)
            };
        }

        /// <summary>
        /// 确定重建范围
        /// </summary>
        private RebuildScope DetermineRebuildScope(TreeStructureDirtyRegion region)
        {
            switch (region.ChangeType)
            {
                case TreeStructureChangeType.Add:
                case TreeStructureChangeType.Remove:
                    return RebuildScope.SubTree;
                    
                case TreeStructureChangeType.Move:
                    return RebuildScope.AffectedPaths;
                    
                case TreeStructureChangeType.Reorder:
                    return RebuildScope.CurrentLevel;
                    
                case TreeStructureChangeType.PathOnly:
                    return RebuildScope.PathOnly;
                    
                default:
                    return RebuildScope.SubTree;
            }
        }

        /// <summary>
        /// 估算受影响的节点数量
        /// </summary>
        private int EstimateAffectedNodeCount(PAPath rootPath, RebuildScope scope)
        {
            try
            {
                switch (scope)
                {
                    case RebuildScope.PathOnly:
                        return 1;
                        
                    case RebuildScope.CurrentLevel:
                        // 估算当前层级的兄弟节点数量
                        var parentPath = rootPath.GetParent();
                        if (parentPath.IsEmpty)
                        {
                            return _asset.Data.Nodes?.Count ?? 0;
                        }
                        return EstimateChildrenCount(parentPath);
                        
                    case RebuildScope.SubTree:
                        return EstimateDescendantCount(rootPath);
                        
                    case RebuildScope.AffectedPaths:
                        return EstimateDescendantCount(rootPath) / 2; // 粗略估算
                        
                    default:
                        return 1;
                }
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// 估算子节点数量
        /// </summary>
        private int EstimateChildrenCount(PAPath parentPath)
        {
            // 简化实现，实际可以更精确
            return 5; // 假设平均每个节点有5个子节点
        }

        /// <summary>
        /// 估算后代节点数量
        /// </summary>
        private int EstimateDescendantCount(PAPath rootPath)
        {
            // 简化实现，实际可以通过遍历计算
            var depth = rootPath.Depth;
            return Math.Max(1, (int)Math.Pow(3, Math.Max(0, 5 - depth))); // 估算公式
        }

        /// <summary>
        /// 清除所有脏区域
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _dirtyRegions.Clear();
            }
        }

        /// <summary>
        /// 获取当前脏区域数量
        /// </summary>
        public int DirtyRegionCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _dirtyRegions.Count;
                }
            }
        }

        /// <summary>
        /// 获取状态信息
        /// </summary>
        public string GetStatusInfo()
        {
            lock (_lockObject)
            {
                var activeRegions = _dirtyRegions.Count;
                var oldestTime = _dirtyRegions.Any() ? 
                    (DateTime.Now - _dirtyRegions.Min(r => r.Timestamp)).TotalMilliseconds : 0;
                
                return $"DirtyManager: {activeRegions} regions, oldest: {oldestTime:F0}ms";
            }
        }
    }

    /// <summary>
    /// 树重建指令
    /// </summary>
    public class TreeRebuildInstruction
    {
        public PAPath RootPath { get; set; }
        public RebuildScope Scope { get; set; }
        public bool RequiresParentChildRebuild { get; set; }
        public bool RequiresPathRebuild { get; set; }
        public bool RequiresRenderOrderRebuild { get; set; }
        public int Priority { get; set; }
        public int EstimatedNodeCount { get; set; }

        public override string ToString()
        {
            return $"Rebuild: {RootPath} {Scope} (Est: {EstimatedNodeCount} nodes, Priority: {Priority})";
        }
    }

    /// <summary>
    /// 重建范围
    /// </summary>
    public enum RebuildScope
    {
        PathOnly,       // 仅路径信息
        CurrentLevel,   // 当前层级
        SubTree,        // 子树
        AffectedPaths   // 受影响的路径
    }
}
