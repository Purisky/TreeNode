﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    public static partial class PropertyAccessor
    {
        #region 类型转换工具类

        /// <summary>
        /// 统一的类型转换工具
        /// </summary>
        private static class TypeConverter
        {
            /// <summary>
            /// 创建类型转换表达式
            /// </summary>
            public static Expression CreateConversion<T>(ParameterExpression valueParam, Type targetType)
            {
                // 如果目标类型与T相同，直接使用
                if (typeof(T) == targetType)
                    return valueParam;

                // 如果T是object，需要运行时类型转换
                if (typeof(T) == typeof(object))
                    return CreateRuntimeTypeConversion(valueParam, targetType);

                // 如果目标类型可以从T赋值，进行转换
                if (targetType.IsAssignableFrom(typeof(T)))
                    return Expression.Convert(valueParam, targetType);

                // 如果T可以赋值给目标类型，进行转换
                if (typeof(T).IsAssignableFrom(targetType))
                    return Expression.Convert(valueParam, targetType);

                // 如果两者都是数值类型，尝试转换
                if (IsNumericType(typeof(T)) && IsNumericType(targetType))
                    return Expression.Convert(valueParam, targetType);

                // 如果目标是object类型，装箱
                if (targetType == typeof(object))
                    return Expression.Convert(valueParam, typeof(object));

                throw new InvalidCastException($"无法将类型 {typeof(T).Name} 转换为 {targetType.Name}");
            }

            /// <summary>
            /// 创建运行时类型转换表达式
            /// </summary>
            private static Expression CreateRuntimeTypeConversion(ParameterExpression valueParam, Type targetType)
            {
                var changeTypeMethod = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) });
                var convertCall = Expression.Call(changeTypeMethod, valueParam, Expression.Constant(targetType));

                return targetType.IsValueType 
                    ? Expression.Convert(convertCall, targetType)
                    : Expression.Convert(convertCall, targetType);
            }
        }

        #endregion

        #region 错误处理工具类

        /// <summary>
        #endregion

        #region 缓存管理器

        /// <summary>
        /// 统一的缓存管理器
        /// </summary>
        private static class CacheManager
        {
            public static Func<object, T> GetOrCreateGetter<T>(Type type, PAPart part)
            {
                var key = new CacheKey(type, new PAPath(part), typeof(T));
                if (GetterCache.TryGetValue(key, out var cached))
                    return (Func<object, T>)cached;

                var getter = CreateGetter<T>(type, part);
                GetterCache[key] = getter;
                return getter;
            }

            public static Action<object, T> GetOrCreateSetter<T>(Type type, PAPart part)
            {
                var key = new CacheKey(type, new PAPath(part), typeof(T));
                if (SetterCache.TryGetValue(key, out var cached))
                    return (Action<object, T>)cached;

                var setter = CreateSetter<T>(type, part);
                SetterCache[key] = setter;
                return setter;
            }
        }

        #endregion

        #region 缓存字段 - 部分迁移到 TypeCacheSystem

        // 获取器缓存 - 保留用于索引访问
        private static readonly ConcurrentDictionary<CacheKey, object> GetterCache = new();

        // 设置器缓存 - 保留用于索引访问
        private static readonly ConcurrentDictionary<CacheKey, object> SetterCache = new();

        // 值类型setter缓存 - 保留用于复杂值类型操作
        private static readonly ConcurrentDictionary<CacheKey, object> StructSetterCache = new();

        #endregion

        #region 缓存和创建方法

        /// <summary>
        /// 获取或创建Getter - 使用TypeCacheSystem
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Func<object, T> GetOrCreateGetter<T>(Type type, PAPart part)
        {
            // 对于索引访问，仍使用原有机制
            if (part.IsIndex)
            {
                return CacheManager.GetOrCreateGetter<T>(type, part);
            }

            // 对于成员访问，使用TypeCacheSystem的预编译委托
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(part.Name);
            
            if (memberInfo?.Getter == null)
            {
                // 如果成员不存在或没有Getter，回退到原有机制
                return CacheManager.GetOrCreateGetter<T>(type, part);
            }

            // 包装TypeCacheSystem的Getter
            return obj =>
            {
                var value = memberInfo.Getter(obj);
                return (T)value;
            };
        }

        /// <summary>
        /// 获取或创建Setter - 使用TypeCacheSystem
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Action<object, T> GetOrCreateSetter<T>(Type type, PAPart part)
        {
            // 对于索引访问，仍使用原有机制
            if (part.IsIndex)
            {
                return CacheManager.GetOrCreateSetter<T>(type, part);
            }

            // 对于成员访问，使用TypeCacheSystem的预编译委托
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(part.Name);
            
            if (memberInfo?.Setter == null)
            {
                // 如果成员不存在或没有Setter，回退到原有机制
                return CacheManager.GetOrCreateSetter<T>(type, part);
            }

            // 包装TypeCacheSystem的Setter
            return (obj, value) => memberInfo.Setter(obj, value);
        }

        /// <summary>
        /// 创建Getter表达式 - 使用PAPart
        /// </summary>
        private static Func<object, T> CreateGetter<T>(Type type, PAPart part)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var current = Expression.Convert(param, type);
            var target = BuildMemberAccess(current, part, ref type);

            if (!typeof(T).IsAssignableFrom(type))
                throw new InvalidCastException($"无法将类型 {type.Name} 转换为 {typeof(T).Name}");

            var finalConvert = Expression.Convert(target, typeof(T));
            return Expression.Lambda<Func<object, T>>(finalConvert, param).Compile();
        }

        /// <summary>
        /// 创建Setter表达式
        /// </summary>
        private static Action<object, T> CreateSetter<T>(Type type, PAPart part)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);

            // 构建目标访问表达式
            var (target, targetType) = BuildMemberAccessTarget(current, part, type);
            
            // 验证可写性
            ValidateWriteAccess(type, part);
            
            // 创建类型转换表达式
            var valueExpression = TypeConverter.CreateConversion<T>(valueParam, targetType);
            
            // 创建赋值表达式
            var assignExpr = Expression.Assign(target, valueExpression);
            
            return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
        }

        /// <summary>
        /// 构建成员访问目标
        /// </summary>
        private static (Expression target, Type targetType) BuildMemberAccessTarget(Expression current, PAPart part, Type type)
        {
            try
            {
                var targetType = type;
                var target = BuildMemberAccess(current, part, ref targetType);
                return (target, targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法构建成员访问表达式: 类型={type.Name}, 成员={part}, 错误={ex.Message}", ex);
            }
        }

        /// <summary>
        /// 验证写入访问权限
        /// </summary>
        private static void ValidateWriteAccess(Type type, PAPart part)
        {
            if (!ValidationStrategy.CanWriteToMember(type, part))
            {
                throw new InvalidOperationException($"成员 '{part}' 在类型 '{type.Name}' 中为只读或不可写");
            }
        }

        /// <summary>
        /// 创建运行时类型转换表达式
        /// </summary>
        private static Expression CreateRuntimeTypeConversion(ParameterExpression valueParam, Type targetType)
        {
            return TypeConverter.CreateConversion<object>(valueParam, targetType);
        }

        /// <summary>
        /// 创建无参构造函数 - 使用TypeCacheSystem
        /// </summary>
        public static T CreateInstance<T>()
        {
            var type = typeof(T);
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            
            if (!typeInfo.HasParameterlessConstructor || typeInfo.Constructor == null)
            {
                throw new InvalidOperationException($"类型 {type.Name} 没有无参构造函数");
            }

            return (T)typeInfo.Constructor();
        }

        #endregion

        #region 成员信息管理 - 使用 TypeCacheSystem

        /// <summary>
        /// 获取成员信息 - 直接使用 TypeCacheSystem
        /// </summary>
        private static TypeCacheSystem.UnifiedMemberInfo GetMemberInfo(Type type, PAPart part)
        {
            if (part.IsIndex)
            {
                // 索引访问不使用 TypeCacheSystem
                return null;
            }

            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            return typeInfo.GetMember(part.Name);
        }

        #endregion

        #region 缓存管理方法

        /// <summary>
        /// 清理指定类型的缓存 - 与 TypeCacheSystem 保持同步
        /// </summary>
        public static void ClearTypeCache(Type type)
        {
            // 清理 TypeCacheSystem 缓存
            TypeCacheSystem.ClearTypeInfo(type);
            
            // 清理本地缓存中相关的条目
            var keysToRemove = new List<CacheKey>();
            
            foreach (var key in GetterCache.Keys)
            {
                if (key.ObjectType == type)
                    keysToRemove.Add(key);
            }
            
            foreach (var key in keysToRemove)
            {
                GetterCache.TryRemove(key, out _);
                SetterCache.TryRemove(key, out _);
                StructSetterCache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 清理所有缓存 - 与 TypeCacheSystem 保持同步
        /// </summary>
        public static void ClearAllCache()
        {
            // 清理 TypeCacheSystem 缓存
            TypeCacheSystem.ClearAllCache();
            
            // 清理本地缓存
            GetterCache.Clear();
            SetterCache.Clear();
            StructSetterCache.Clear();
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static (int getter, int setter, int struct_setter, int type_cache_count, int type_member_count) GetCacheStats()
        {
            return (
                GetterCache.Count,
                SetterCache.Count,
                StructSetterCache.Count,
                TypeCacheSystem.CacheStats.CachedTypeCount,
                TypeCacheSystem.CacheStats.TotalMemberCount
            );
        }

        #endregion


    }
}