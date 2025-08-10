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
            var key = new CacheKey(type, new PAPath(part), typeof(T));
            if (GetterCache.TryGetValue(key, out var cached))
                return (Func<object, T>)cached;

            var getter = CreateGetter<T>(type, part);
            GetterCache[key] = getter;
            return getter;
        }

        /// <summary>
        /// 获取或创建Setter - 使用PAPart
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Action<object, T> GetOrCreateSetter<T>(Type type, PAPart part)
        {
            var key = new CacheKey(type, new PAPath(part), typeof(T));
            if (SetterCache.TryGetValue(key, out var cached))
                return (Action<object, T>)cached;

            var setter = CreateSetter<T>(type, part);
            SetterCache[key] = setter;
            return setter;
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
                throw new InvalidCastException($"类型不匹配: 期望 {typeof(T).Name}, 实际 {type.Name}");

            var finalConvert = Expression.Convert(target, typeof(T));
            return Expression.Lambda<Func<object, T>>(finalConvert, param).Compile();
        }

        /// <summary>
        /// 创建Setter表达式 - 使用PAPart
        /// </summary>
        private static Action<object, T> CreateSetter<T>(Type type, PAPart part)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);
            Expression target;
            Type targetType;

            try
            {
                targetType = type;
                target = BuildMemberAccess(current, part, ref targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法构建成员访问表达式: 类型={type.Name}, 成员={part}, 错误={ex.Message}", ex);
            }

            // 检查是否可以写入
            if (!CanWriteToMember(type, part))
            {
                throw new InvalidOperationException($"成员 {part} 在类型 {type.Name} 中为只读或不可写");
            }

            try
            {
                Expression valueExpression;

                // 如果目标类型与T相同，直接使用
                if (typeof(T) == targetType)
                {
                    valueExpression = valueParam;
                }
                // 如果T是object，需要运行时类型转换
                else if (typeof(T) == typeof(object))
                {
                    // 对于object类型的输入值，在运行时进行类型转换
                    valueExpression = CreateRuntimeTypeConversion(valueParam, targetType);
                }
                // 如果目标类型可以从T赋值，进行转换
                else if (targetType.IsAssignableFrom(typeof(T)))
                {
                    valueExpression = Expression.Convert(valueParam, targetType);
                }
                // 如果T可以赋值给目标类型，进行转换
                else if (typeof(T).IsAssignableFrom(targetType))
                {
                    valueExpression = Expression.Convert(valueParam, targetType);
                }
                // 如果两者都是数值类型，尝试转换
                else if (IsNumericType(typeof(T)) && IsNumericType(targetType))
                {
                    valueExpression = Expression.Convert(valueParam, targetType);
                }
                // 如果目标是object类型，装箱
                else if (targetType == typeof(object))
                {
                    valueExpression = Expression.Convert(valueParam, typeof(object));
                }
                else
                {
                    throw new InvalidCastException($"无法将类型 {typeof(T).Name} 转换为 {targetType.Name}");
                }
                var assignExpr = Expression.Assign(target, valueExpression);
                return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建Setter失败: 类型={type.Name}, 成员={part}, 值类型={typeof(T).Name}, 目标类型={targetType.Name}, 错误={ex.Message}", ex);
            }
        }

        /// <summary>
        /// 创建运行时类型转换表达式
        /// </summary>
        private static Expression CreateRuntimeTypeConversion(ParameterExpression valueParam, Type targetType)
        {
            // 使用Convert.ChangeType进行运行时类型转换
            var changeTypeMethod = typeof(Convert).GetMethod("ChangeType", new[] { typeof(object), typeof(Type) });
            var convertCall = Expression.Call(changeTypeMethod, valueParam, Expression.Constant(targetType));

            // 如果目标类型是值类型，需要拆箱
            if (targetType.IsValueType)
            {
                return Expression.Convert(convertCall, targetType);
            }
            else
            {
                return Expression.Convert(convertCall, targetType);
            }
        }

        /// <summary>
        /// 检查成员是否可写 - 使用PAPart
        /// </summary>
        private static bool CanWriteToMember(Type type, PAPart part)
        {
            if (part.IsIndex)
            {
                // 直接对类型进行索引访问
                return type.IsArray || typeof(IList).IsAssignableFrom(type);
            }
            else
            {
                // 普通属性/字段访问
                var property = type.GetProperty(part.Name);
                if (property != null)
                {
                    return property.CanWrite;
                }

                var field = type.GetField(part.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return field != null && !field.IsInitOnly;
            }
        }

        /// <summary>
        /// 检查是否为数值类型
        /// </summary>
        private static bool IsNumericType(Type type)
        {
            if (type.IsEnum) return false;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
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