using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TreeNode.Utility;

namespace TreeNode.Runtime
{
    /// <summary>
    /// 高性能多层链路处理引擎
    /// 统一处理IPropertyAccessor、IList扩展和静态兜底方法的多层链路导航
    /// </summary>
    public static class MultiLevelNavigationEngine
    {
        #region 类型分发缓存（性能关键）
        
        /// <summary>
        /// 对象类型分发策略缓存
        /// </summary>
        private static readonly ConcurrentDictionary<Type, ObjectNavigationStrategy?> 
            _navigationStrategyCache = new();
        
        /// <summary>
        /// 对象导航策略枚举（优化分支预测）
        /// </summary>
        private enum ObjectNavigationStrategy : byte
        {
            PropertyAccessor = 1,    // IPropertyAccessor实现
            ListExtension = 2        // IList扩展
            // 注意：不需要StaticFallback值，直接使用default分支作为兜底
        }
        
        /// <summary>
        /// 获取对象的导航策略（高性能版本）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ObjectNavigationStrategy? GetNavigationStrategy(object obj, ref PAPath path, ref int index)
        {
            // 空值检查：提供有用的错误信息
            if (obj == null)
            {
                // 直接使用PAPath.GetSubPath计算有效路径
                var validPath = path.GetSubPath(0, index+1);
                
                // 安全获取当前尝试访问的路径部分
                var currentPart =  path.Parts[index+1];
                
                // 安全获取剩余路径
                var remainingPath = index + 2 < path.Parts.Length ? $" -> {path.GetSubPath(index + 2)}": "";
                
                throw new ArgumentNullException(nameof(obj),
                    $"路径对象为空：{validPath} -> {currentPart}(null) {remainingPath}");
            }
            var type = obj.GetType();
            
            // 热路径：先检查缓存
            if (_navigationStrategyCache.TryGetValue(type, out var cached))
            {
                return cached;
            }
            
            // 冷路径：计算并缓存策略
            var strategy = ComputeNavigationStrategy(obj);
            _navigationStrategyCache[type] = strategy;
            return strategy;
        }
        
        /// <summary>
        /// 获取对象的导航策略（简化版本，用于不需要路径信息的场景）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ObjectNavigationStrategy? GetNavigationStrategy(object obj)
        {
            var type = obj.GetType();
            // 热路径：先检查缓存
            if (_navigationStrategyCache.TryGetValue(type, out var cached))
            {
                return cached;
            }
            
            // 冷路径：计算并缓存策略
            var strategy = ComputeNavigationStrategy(obj);
            _navigationStrategyCache[type] = strategy;
            return strategy;
        }
        
        /// <summary>
        /// 计算导航策略（冷路径）
        /// </summary>
        private static ObjectNavigationStrategy? ComputeNavigationStrategy(object obj)
        {
            // 优化：按概率排序检查，IPropertyAccessor最常见
            if (obj is IPropertyAccessor) return ObjectNavigationStrategy.PropertyAccessor;
            if (obj is IList) return ObjectNavigationStrategy.ListExtension;
            return null; // 返回null，让switch使用default分支
        }
        
        #endregion
        
        #region 统一的多层处理方法
        
        /// <summary>
        /// 统一的GetValue多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ProcessMultiLevel_GetValue<T>(object nextObj, ref PAPath path, ref int index)
        {
            // 使用策略分发避免重复类型检查
            return GetNavigationStrategy(nextObj, ref path, ref index) switch
            {
                ObjectNavigationStrategy.PropertyAccessor => ((IPropertyAccessor)nextObj).GetValueInternal<T>(ref path, ref index),
                ObjectNavigationStrategy.ListExtension => ((IList)nextObj).GetValueInternal<T>(ref path, ref index),
                // 静态兜底方法
                _ => PropertyAccessor.GetValueInternal<T>(nextObj, ref path, ref index),
            };
        }
        
        /// <summary>
        /// 统一的SetValue多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessMultiLevel_SetValue<T>(object nextObj, ref PAPath path, ref int index, T value)
        {
            // 空值检查现在在GetNavigationStrategy内部处理
            switch (GetNavigationStrategy(nextObj, ref path, ref index))
            {
                case ObjectNavigationStrategy.PropertyAccessor:
                    ((IPropertyAccessor)nextObj).SetValueInternal(ref path, ref index, value);
                    break;
                    
                case ObjectNavigationStrategy.ListExtension:
                    ((IList)nextObj).SetValueInternal(ref path, ref index, value);
                    break;
                    
                default: // 静态兜底方法
                    PropertyAccessor.SetValueInternal(nextObj, ref path, ref index, value);
                    break;
            }
        }
        
        /// <summary>
        /// 统一的RemoveValue多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessMultiLevel_RemoveValue(object nextObj, ref PAPath path, ref int index)
        {
            // 空值检查现在在GetNavigationStrategy内部处理
            switch (GetNavigationStrategy(nextObj, ref path, ref index))
            {
                case ObjectNavigationStrategy.PropertyAccessor:
                    ((IPropertyAccessor)nextObj).RemoveValueInternal(ref path, ref index);
                    break;
                    
                case ObjectNavigationStrategy.ListExtension:
                    ((IList)nextObj).RemoveValueInternal(ref path, ref index);
                    break;
                    
                default: // 静态兜底方法
                    PropertyAccessor.RemoveValueInternal(nextObj, ref path, ref index);
                    break;
            }
        }
        
        /// <summary>
        /// 统一的ValidatePath多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessMultiLevel_ValidatePath(object nextObj, ref PAPath path, ref int index)
        {
            // 空值检查现在在GetNavigationStrategy内部处理
            switch (GetNavigationStrategy(nextObj, ref path, ref index))
            {
                case ObjectNavigationStrategy.PropertyAccessor:
                    ((IPropertyAccessor)nextObj).ValidatePath(ref path, ref index);
                    break;
                    
                case ObjectNavigationStrategy.ListExtension:
                    ((IList)nextObj).ValidatePath(ref path, ref index);
                    break;
                    
                default: // 静态兜底方法
                    PropertyAccessor.ValidatePath(nextObj, ref path, ref index);
                    break;
            }
        }
        
        /// <summary>
        /// 统一的GetAllInPath多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessMultiLevel_GetAllInPath<T>(object nextObj, ref PAPath path, ref int index, List<(int depth, T value)> list) where T : class
        {
            // 空值检查：直接返回，不抛出异常
            if (nextObj == null)
            {
                return;
            }
            
            switch (GetNavigationStrategy(nextObj, ref path, ref index))
            {
                case ObjectNavigationStrategy.PropertyAccessor:
                    ((IPropertyAccessor)nextObj).GetAllInPath<T>(ref path, ref index, list);
                    break;
                    
                case ObjectNavigationStrategy.ListExtension:
                    ((IList)nextObj).GetAllInPath<T>(ref path, ref index, list);
                    break;
                    
                default: // 静态兜底方法
                    PropertyAccessor.GetAllInPath<T>(nextObj, ref path, ref index, list);
                    break;
            }
        }
        
        /// <summary>
        /// 统一的CollectNodes多层处理
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessMultiLevel_CollectNodes(object nextObj, List<(PAPath, JsonNode)> listNodes, PAPath parent, int depth = -1)
        {
            // 空值检查：直接返回，不抛出异常
            if (nextObj == null)
            {
                return;
            }
            
            switch (GetNavigationStrategy(nextObj))
            {
                case ObjectNavigationStrategy.PropertyAccessor:
                    ((IPropertyAccessor)nextObj).CollectNodes(listNodes, parent, depth);
                    break;
                    
                case ObjectNavigationStrategy.ListExtension:
                    ((IList)nextObj).CollectNodes(listNodes, parent, depth);
                    break;
                    
                default: // 静态兜底方法
                    PropertyAccessor.CollectNodes(nextObj, listNodes, parent, depth);
                    break;
            }
        }
        
        #endregion
        
        #region 性能优化辅助方法
        
        /// <summary>
        /// 预热缓存（在应用启动时调用）
        /// </summary>
        public static void WarmupCache(IEnumerable<Type> commonTypes)
        {
            foreach (var type in commonTypes)
            {
                try
                {
                    var typeInfo = TypeCacheSystem.GetTypeInfo(type);
                    if (typeInfo.HasParameterlessConstructor && typeInfo.Constructor != null)
                    {
                        var instance = typeInfo.Constructor.Invoke();
                        if (instance != null)
                        {
                            GetNavigationStrategy(instance);
                        }
                    }
                }
                catch
                {
                    // 忽略创建失败的类型
                }
            }
        }
        
        /// <summary>
        /// 预热缓存（从已知实例）
        /// </summary>
        public static void WarmupCache(IEnumerable<object> instances)
        {
            foreach (var instance in instances)
            {
                if (instance != null)
                {
                    GetNavigationStrategy(instance);
                }
            }
        }
        
        /// <summary>
        /// 清理缓存（用于内存管理）
        /// </summary>
        public static void ClearCache()
        {
            _navigationStrategyCache.Clear();
        }
        
        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static (int CachedTypes, int PropertyAccessorTypes, int ListTypes, int StaticFallbackTypes) GetCacheStats()
        {
            var propertyAccessorCount = 0;
            var listCount = 0;
            var staticFallbackCount = 0;
            
            foreach (var strategy in _navigationStrategyCache.Values)
            {
                switch (strategy)
                {
                    case ObjectNavigationStrategy.PropertyAccessor:
                        propertyAccessorCount++;
                        break;
                    case ObjectNavigationStrategy.ListExtension:
                        listCount++;
                        break;
                    case null:
                        staticFallbackCount++;
                        break;
                }
            }
            
            return (_navigationStrategyCache.Count, propertyAccessorCount, listCount, staticFallbackCount);
        }
        
        /// <summary>
        /// 获取缓存命中率估算
        /// </summary>
        public static double GetEstimatedHitRate()
        {
            // 基于缓存大小的简单估算，实际应用中可以实现更精确的统计
            var cacheSize = _navigationStrategyCache.Count;
            if (cacheSize == 0) return 0.0;
            
            // 假设缓存命中率随着缓存大小增加而提升，最高95%
            return Math.Min(0.95, 0.5 + (cacheSize * 0.01));
        }
        
        #endregion
    }
}
