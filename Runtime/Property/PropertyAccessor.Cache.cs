using Newtonsoft.Json.Linq;
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

                throw PropertyAccessorErrors.CreateTypeMismatch<T>(targetType);
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
        /// 统一的错误处理工具
        /// </summary>
        private static class PropertyAccessorErrors
        {
            public static InvalidOperationException CreateMemberNotFound(Type type, PAPart part)
            {
                return new InvalidOperationException(
                    $"成员 '{part}' 在类型 '{type.Name}' 中未找到");
            }

            public static InvalidCastException CreateTypeMismatch<T>(Type targetType)
            {
                return new InvalidCastException(
                    $"无法将类型 {typeof(T).Name} 转换为 {targetType.Name}");
            }

            public static InvalidOperationException CreateReadOnlyMember(Type type, PAPart part)
            {
                return new InvalidOperationException(
                    $"成员 '{part}' 在类型 '{type.Name}' 中为只读或不可写");
            }

            public static InvalidOperationException CreateMemberAccessError(Type type, PAPart part, Exception innerException)
            {
                return new InvalidOperationException(
                    $"无法构建成员访问表达式: 类型={type.Name}, 成员={part}, 错误={innerException.Message}", 
                    innerException);
            }

            public static InvalidOperationException CreateSetterError(Type type, PAPart part, Type valueType, Type targetType, Exception innerException)
            {
                return new InvalidOperationException(
                    $"创建Setter失败: 类型={type.Name}, 成员={part}, 值类型={valueType.Name}, 目标类型={targetType.Name}, 错误={innerException.Message}", 
                    innerException);
            }
        }

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

        #region 缓存字段

        // 获取器缓存 - 使用PAPath作为键
        private static readonly ConcurrentDictionary<CacheKey, object> GetterCache = new();

        // 设置器缓存 - 使用PAPath作为键
        private static readonly ConcurrentDictionary<CacheKey, object> SetterCache = new();

        // 无参构造函数检查缓存
        private static readonly ConcurrentDictionary<Type, bool> ConstructorCache = new();

        // 成员信息缓存 - 使用PAPath作为键
        private static readonly ConcurrentDictionary<CacheKey, MemberMetadata> MemberCache = new();

        // 值类型setter缓存 - 使用PAPath作为键
        private static readonly ConcurrentDictionary<CacheKey, object> StructSetterCache = new();

        #endregion

        #region 缓存和创建方法

        /// <summary>
        /// 获取或创建Getter - 使用PAPart
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Func<object, T> GetOrCreateGetter<T>(Type type, PAPart part)
        {
            return CacheManager.GetOrCreateGetter<T>(type, part);
        }

        /// <summary>
        /// 获取或创建Setter - 使用PAPart
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Action<object, T> GetOrCreateSetter<T>(Type type, PAPart part)
        {
            return CacheManager.GetOrCreateSetter<T>(type, part);
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
                throw PropertyAccessorErrors.CreateTypeMismatch<T>(type);

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
                throw PropertyAccessorErrors.CreateMemberAccessError(type, part, ex);
            }
        }

        /// <summary>
        /// 验证写入访问权限
        /// </summary>
        private static void ValidateWriteAccess(Type type, PAPart part)
        {
            if (!ValidationStrategy.CanWriteToMember(type, part))
            {
                throw PropertyAccessorErrors.CreateReadOnlyMember(type, part);
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
        /// 创建无参构造函数
        /// </summary>
        public static T CreateInstance<T>()
        {
            var type = typeof(T);
            if (!ConstructorCache.TryGetValue(type, out var hasConstructor))
            {
                hasConstructor = !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;
                ConstructorCache[type] = hasConstructor;
            }

            if (!hasConstructor)
                throw new InvalidOperationException($"类型 {type.Name} 没有无参构造函数");

            return (T)Activator.CreateInstance(type);
        }

        #endregion

        #region 成员信息管理

        /// <summary>
        /// 获取或创建成员信息 - 使用PAPart
        /// </summary>
        private static MemberMetadata GetOrCreateMemberInfo(Type type, PAPart part)
        {
            var key = new CacheKey(type, new PAPath(part), typeof(object));
            if (MemberCache.TryGetValue(key, out var cached))
                return cached;

            var metadata = CreateMemberMetadata(type, part);
            MemberCache[key] = metadata;
            return metadata;
        }

        /// <summary>
        /// 创建成员元数据（增强Array检测）- 使用PAPart
        /// </summary>
        private static MemberMetadata CreateMemberMetadata(Type type, PAPart part)
        {
            PropertyInfo property = null;
            FieldInfo field = null;
            bool isIndexer = part.IsIndex;
            bool isArray = false;
            string collectionName = null;

            if (part.IsIndex)
            {
                // 直接对当前对象进行索引访问
                isArray = type.IsArray;
            }
            else
            {
                property = type.GetProperty(part.Name);
                if (property == null)
                {
                    field = type.GetField(part.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            return new MemberMetadata(property, field, isIndexer, isArray, collectionName);
        }

        #endregion


    }
}