using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class History
    {
        const int MaxStep = 20; // 增加历史步骤数量
        const int MaxMemoryUsageMB = 50; // 最大内存使用限制(MB)
        const int OperationMergeWindowMs = 500; // 操作合并时间窗口(毫秒)

        // 内存管理
        private readonly Dictionary<string, WeakReference> _nodeCache = new Dictionary<string, WeakReference>();
        private long _lastGCTime = DateTime.Now.Ticks;
        private const long GCIntervalTicks = TimeSpan.TicksPerMinute * 5; // 每5分钟检查一次GC

        // 性能优化相关 - 移除不必要的锁
        private PerformanceStats _performanceStats = new PerformanceStats();

        /// <summary>
        /// 更新性能统计
        /// </summary>
        private void UpdatePerformanceStats()
        {
            _performanceStats.TotalSteps = Steps.Count;
            _performanceStats.RedoSteps = RedoSteps.Count;
            _performanceStats.CachedOperations = _nodeCache.Count;
        }

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public PerformanceStats GetPerformanceStats()
        {
            UpdatePerformanceStats();
            return _performanceStats.Clone();
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

            _performanceStats.MemoryUsageMB = memoryUsage;

            if (memoryUsage > MaxMemoryUsageMB)
            {
                Debug.LogWarning($"History memory usage ({memoryUsage}MB) exceeds limit ({MaxMemoryUsageMB}MB)");
                // 触发更积极的内存清理
                PerformAggressiveCleanup();
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
