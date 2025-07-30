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
    /// <summary>
    /// 高性能属性访问器 - 支持通过字符串路径访问对象属性
    /// 重构后采用PAPath作为内部逻辑路径，提升性能和可维护性
    /// 增强了对Array类型的直接支持
    /// </summary>
    public static class PropertyAccessor
    {
        #region 内部数据结构

        /// <summary>
        /// 优化的缓存键结构 - 使用PAPath替代字符串路径
        /// </summary>
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly Type ObjectType;
            public readonly PAPath MemberPath;
            public readonly Type ValueType;
            private readonly int _hashCode;

            public CacheKey(Type objectType, PAPath memberPath, Type valueType)
            {
                ObjectType = objectType;
                MemberPath = memberPath;
                ValueType = valueType;
                _hashCode = HashCode.Combine(objectType, memberPath.GetHashCode(), valueType);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CacheKey other) =>
                ObjectType == other.ObjectType &&
                MemberPath.Equals(other.MemberPath) &&
                ValueType == other.ValueType;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode() => _hashCode;
        }

        /// <summary>
        /// 成员信息缓存结构
        /// </summary>
        private readonly struct MemberMetadata
        {
            public readonly PropertyInfo Property;
            public readonly FieldInfo Field;
            public readonly bool IsProperty;
            public readonly bool IsIndexer;
            public readonly bool IsArray;
            public readonly string CollectionName;
            public readonly bool HasCollectionName;

            public MemberMetadata(PropertyInfo property, FieldInfo field, bool isIndexer, bool isArray, string collectionName)
            {
                Property = property;
                Field = field;
                IsProperty = property != null;
                IsIndexer = isIndexer;
                IsArray = isArray;
                CollectionName = collectionName;
                HasCollectionName = !string.IsNullOrEmpty(collectionName);
            }
        }

        /// <summary>
        /// 值类型Setter委托
        /// </summary>
        private delegate object StructSetter<T>(object structObj, T value);

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

        #region 公共API - 保持向后兼容

        /// <summary>
        /// 获取属性值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>属性值</returns>
        public static T GetValue<T>(object obj, string path)
        {
            return GetValue<T>(obj, PAPath.Create(path));
        }

        /// <summary>
        /// 获取属性值 - 使用PAPath
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        /// <returns>属性值</returns>
        public static T GetValue<T>(object obj, PAPath path)
        {
            if (path.IsEmpty)
                return (T)obj;

            if (path.Depth == 1)
            {
                // 单层路径直接访问，性能优化
                var singleGetter = GetOrCreateGetter<T>(obj.GetType(), path.FirstPart);
                return singleGetter(obj);
            }

            // 多层路径访问
            var parent = GetParentObject(obj, path, out var lastPart);
            var multiGetter = GetOrCreateGetter<T>(parent.GetType(), lastPart);
            return multiGetter(parent);
        }

        /// <summary>
        /// 设置属性值
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="value">要设置的值</param>
        public static void SetValue<T>(object obj, string path, T value)
        {
            SetValue(obj, PAPath.Create(path), value);
        }

        /// <summary>
        /// 设置属性值 - 使用PAPath
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        /// <param name="value">要设置的值</param>
        public static void SetValue<T>(object obj, PAPath path, T value)
        {
            if (path.IsEmpty)
                throw new ArgumentException("路径不能为空");

            if (path.Depth == 1)
            {
                // 单层路径直接设置，性能优化
                if (obj.GetType().IsValueType)
                {
                    throw new InvalidOperationException("无法修改值类型的根对象");
                }
                
                var setter = GetOrCreateSetter<T>(obj.GetType(), path.FirstPart);
                setter(obj, value);
                return;
            }

            // 多层路径设置
            var parent = GetParentObject(obj, path, out var lastPart);
            
            // 处理值类型的特殊情况
            if (parent.GetType().IsValueType)
            {
                HandleValueTypeSet(obj, path, lastPart, value);
            }
            else
            {
                var setter = GetOrCreateSetter<T>(parent.GetType(), lastPart);
                setter(parent, value);
            }
        }

        /// <summary>
        /// 尝试获取属性值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="result">输出结果</param>
        /// <returns>是否成功获取</returns>
        public static bool TryGetValue<T>(object obj, string path, out T result)
        {
            return TryGetValue(obj, PAPath.Create(path), out result);
        }

        /// <summary>
        /// 尝试获取属性值 - 使用PAPath
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        /// <param name="result">输出结果</param>
        /// <returns>是否成功获取</returns>
        public static bool TryGetValue<T>(object obj, PAPath path, out T result)
        {
            try
            {
                result = GetValue<T>(obj, path);
                return result != null;
            }
            catch
            {
                result = default;
                return false;
            }
        }

        /// <summary>
        /// 设置属性值为null或移除列表项
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        public static void SetValueNull(object obj, string path)
        {
            SetValueNull(obj, PAPath.Create(path));
        }

        /// <summary>
        /// 设置属性值为null或移除列表项 - 使用PAPath
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        public static void SetValueNull(object obj, PAPath path)
        {
            var parent = GetParentObject(obj, path, out var lastPart);
            
            if (parent is IList list && lastPart.IsIndex)
            {
                list.RemoveAt(lastPart.Index);
            }
            // Array类型不支持RemoveAt操作，只能设置为null/default
            else if (parent.GetType().IsArray && lastPart.IsIndex)
            {
                var array = (Array)parent;
                var elementType = array.GetType().GetElementType();
                var defaultValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                array.SetValue(defaultValue, lastPart.Index);
            }
            else
            {
                var setter = GetOrCreateSetter<object>(parent.GetType(), lastPart);
                setter(parent, null);
            }
        }

        /// <summary>
        /// 验证路径有效性
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="validLength">有效路径长度</param>
        /// <returns>路径是否完全有效</returns>
        public static bool GetValidPath(object obj, string path, out int validLength)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            
            var paPath = PAPath.Create(path);
            bool isValid = GetValidPath(obj, paPath, out int validDepth);
            
            // 将深度转换为字符串长度
            validLength = ConvertDepthToStringLength(paPath, validDepth);
            
            return isValid;
        }

        /// <summary>
        /// 验证路径有效性 - 使用PAPath
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        /// <param name="validDepth">有效路径深度</param>
        /// <returns>路径是否完全有效</returns>
        public static bool GetValidPath(object obj, PAPath path, out int validDepth)
        {
            obj.ThrowIfNull(nameof(obj));
            
            var currentObj = obj;
            validDepth = 0;
            
            if (path.IsEmpty)
            {
                validDepth = 0;
                return true;
            }

            for (int i = 0; i < path.Depth; i++)
            {
                var part = path.Parts[i];
                
                try
                {
                    if (i == path.Depth - 1)
                    {
                        // 最后一个部分，验证成员存在性
                        if (ValidateMemberExists(currentObj, part))
                        {
                            validDepth = path.Depth;
                            return true;
                        }
                        else
                        {
                            validDepth = i;
                            return false;
                        }
                    }
                    else
                    {
                        // 中间部分，尝试获取下一个对象
                        currentObj = NavigateToNextObject(obj, currentObj, path, i);
                        if (currentObj == null)
                        {
                            validDepth = i;
                            return false;
                        }
                        validDepth = i + 1;
                    }
                }
                catch
                {
                    validDepth = i;
                    return false;
                }
            }
            
            validDepth = path.Depth;
            return true;
        }

        /// <summary>
        /// 查找路径中最后出现指定类型的对象
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="obj">根对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="includeEnd">是否包含路径末尾</param>
        /// <param name="index">找到对象时的路径索引</param>
        /// <returns>找到的对象</returns>
        public static T GetLast<T>(object obj, string path, bool includeEnd, out int index)
        {
            return GetLast<T>(obj, PAPath.Create(path), includeEnd, out index);
        }

        /// <summary>
        /// 查找路径中最后出现指定类型的对象 - 使用PAPath
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="obj">根对象</param>
        /// <param name="path">PAPath路径</param>
        /// <param name="includeEnd">是否包含路径末尾</param>
        /// <param name="index">找到对象时的路径深度</param>
        /// <returns>找到的对象</returns>
        public static T GetLast<T>(object obj, PAPath path, bool includeEnd, out int index)
        {
            obj.ThrowIfNull(nameof(obj));
            
            var currentObj = obj;
            T result = default;
            index = 0;
            
            int endIndex = includeEnd ? path.Depth : path.Depth - 1;
            
            for (int i = 0; i < endIndex; i++)
            {
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), path.Parts[i]);
                currentObj = getter(currentObj);
                
                if (currentObj is T typedObj)
                {
                    result = typedObj;
                    index = i + 1;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 获取父对象
        /// </summary>
        public static object GetParentObject(object obj, string path, out string lastMember)
        {
            var paPath = PAPath.Create(path);
            var parent = GetParentObject(obj, paPath, out var lastPart);
            lastMember = ConvertPartToString(lastPart);
            return parent;
        }

        /// <summary>
        /// 获取父对象 - 使用PAPath
        /// </summary>
        public static object GetParentObject(object obj, PAPath path, out PAPart lastPart)
        {
            obj.ThrowIfNull(nameof(obj));

            if (path.Depth <= 1)
            {
                lastPart = path.FirstPart;
                return obj;
            }

            lastPart = path.LastPart;
            var parentPath = path.GetParent();
            
            return GetValue<object>(obj, parentPath);
        }

        /// <summary>
        /// 获取数组长度
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="arrayPath">数组路径</param>
        /// <returns>数组长度，如果不是数组则返回-1</returns>
        public static int GetArrayLength(object obj, string arrayPath)
        {
            return GetArrayLength(obj, PAPath.Create(arrayPath));
        }

        /// <summary>
        /// 获取数组长度 - 使用PAPath
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="arrayPath">PAPath数组路径</param>
        /// <returns>数组长度，如果不是数组则返回-1</returns>
        public static int GetArrayLength(object obj, PAPath arrayPath)
        {
            try
            {
                var arrayObj = GetValue<object>(obj, arrayPath);
                return arrayObj switch
                {
                    Array array => array.Length,
                    ICollection collection => collection.Count,
                    _ => -1
                };
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 检查指定路径是否为数组类型
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>是否为数组类型</returns>
        public static bool IsArrayPath(object obj, string path)
        {
            return IsArrayPath(obj, PAPath.Create(path));
        }

        /// <summary>
        /// 检查指定路径是否为数组类型 - 使用PAPath
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">PAPath路径</param>
        /// <returns>是否为数组类型</returns>
        public static bool IsArrayPath(object obj, PAPath path)
        {
            try
            {
                var targetObj = GetValue<object>(obj, path);
                return targetObj is Array || targetObj is IList;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 提取父路径
        /// </summary>
        public static string ExtractParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            
            int lastDot = path.LastIndexOf('.');
            int lastBracket = path.LastIndexOf('[');

            if (lastDot == -1 && lastBracket == -1)
                return null;
                
            return path[..Math.Max(lastDot, lastBracket)];
        }

        #endregion

        #region 私有核心方法

        /// <summary>
        /// 处理值类型的设置操作
        /// </summary>
        private static void HandleValueTypeSet<T>(object rootObj, PAPath fullPath, PAPart lastPart, T value)
        {
            var parentPath = fullPath.GetParent();
            var parentObj = GetValue<object>(rootObj, parentPath);
            
            var parentCopy = SetValueOnStructOptimized(parentObj, lastPart, value);
            
            if (!parentPath.IsEmpty)
            {
                SetValue<object>(rootObj, parentPath, parentCopy);
            }
            else
            {
                throw new InvalidOperationException("无法修改值类型的根对象");
            }
        }

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

        #region 表达式构建

        /// <summary>
        /// 构建成员访问表达式 - 使用PAPart
        /// </summary>
        private static Expression BuildMemberAccess(Expression current, PAPart part, ref Type type)
        {
            if (part.IsIndex)
            {
                return BuildIndexerAccess(current, part.Index, ref type);
            }
            else
            {
                return BuildPropertyOrFieldAccess(current, part.Name, ref type);
            }
        }

        /// <summary>
        /// 构建索引器访问表达式（增强Array支持）
        /// </summary>
        private static Expression BuildIndexerAccess(Expression current, int index, ref Type type)
        {
            // 优化：区分Array和普通索引器处理
            if (type.IsArray)
            {
                // 对于数组类型，使用Expression.ArrayAccess，它支持读写操作
                current = Expression.ArrayAccess(current, Expression.Constant(index));
                type = type.GetElementType();
            }
            else
            {
                // 对于其他集合类型，使用索引器
                var indexer = type.GetProperty("Item");
                if (indexer == null)
                {
                    throw new ArgumentException($"类型 {type.Name} 不支持索引器访问");
                }
                current = Expression.MakeIndex(current, indexer, new[] { Expression.Constant(index) });
                type = indexer.PropertyType;
            }

            return current;
        }

        /// <summary>
        /// 构建属性或字段访问表达式
        /// </summary>
        private static Expression BuildPropertyOrFieldAccess(Expression current, string memberName, ref Type type)
        {
            var property = type.GetProperty(memberName);
            if (property != null)
            {
                current = Expression.Property(current, property);
                type = property.PropertyType;
                return current;
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new ArgumentException($"成员 {memberName} 在类型 {type.Name} 中未找到");
            current = Expression.Field(current, field);
            type = field.FieldType;
            return current;
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

        #region 验证方法

        /// <summary>
        /// 验证成员是否存在 - 使用PAPart
        /// </summary>
        private static bool ValidateMemberExists(object obj, PAPart part)
        {
            if (obj == null) return false;

            var metadata = GetOrCreateMemberInfo(obj.GetType(), part);
            
            if (metadata.IsIndexer)
            {
                return ValidateIndexerAccess(obj, part.Index, metadata);
            }
            else
            {
                return metadata.IsProperty && metadata.Property != null ||
                       !metadata.IsProperty && metadata.Field != null;
            }
        }

        /// <summary>
        /// 验证索引器访问（增强Array验证）
        /// </summary>
        private static bool ValidateIndexerAccess(object obj, int index, MemberMetadata metadata)
        {
            object collection = obj;
            Type collectionType = obj.GetType();

            if (collection == null) return false;

            // 优先检查Array类型
            if (collectionType.IsArray && collection is Array array)
            {
                return index >= 0 && index < array.Length;
            }
            // 然后检查IList类型
            else if (collection is IList list)
            {
                return index >= 0 && index < list.Count;
            }
            // 最后检查是否有索引器
            else
            {
                return collectionType.GetProperty("Item") != null;
            }
        }

        #endregion

        #region 导航和错误处理

        /// <summary>
        /// 导航到下一个对象 - 使用PAPath和索引
        /// </summary>
        private static object NavigateToNextObject(object rootObj, object currentObj, PAPath fullPath, int partIndex)
        {
            var part = fullPath.Parts[partIndex];
            var getter = GetOrCreateGetter<object>(currentObj.GetType(), part);
            object nextObj = getter(currentObj);
            
            // 如果对象为null，尝试自动创建
            if (nextObj == null)
            {
                nextObj = TryCreateMissingObject(rootObj, fullPath, partIndex, currentObj.GetType());
            }
            
            return nextObj;
        }

        /// <summary>
        /// 尝试创建缺失的对象 - 使用PAPath
        /// </summary>
        private static object TryCreateMissingObject(object rootObj, PAPath fullPath, int partIndex, Type parentType)
        {
            var part = fullPath.Parts[partIndex];
            var memberType = GetMemberType(parentType, part);
            
            if (memberType != null && 
                !IsJsonNodeType(memberType) && 
                HasValidParameterlessConstructor(memberType))
            {
                var newObj = Activator.CreateInstance(memberType);
                var parentPath = fullPath.GetSubPath(0, partIndex + 1);
                SetValue(rootObj, parentPath, newObj);
                return newObj;
            }
            
            return null;
        }

        /// <summary>
        /// 获取成员类型 - 使用PAPart
        /// </summary>
        private static Type GetMemberType(Type type, PAPart part)
        {
            var metadata = GetOrCreateMemberInfo(type, part);
            return metadata.IsProperty && metadata.Property != null 
                ? metadata.Property.PropertyType 
                : metadata.Field?.FieldType;
        }

        #endregion

        #region 类型检查工具

        /// <summary>
        /// 检查类型是否有有效的无参构造函数
        /// </summary>
        private static bool HasValidParameterlessConstructor(Type type)
        {
            if (ConstructorCache.TryGetValue(type, out bool hasConstructor))
                return hasConstructor;
            
            hasConstructor = !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;
            ConstructorCache[type] = hasConstructor;
            
            return hasConstructor;
        }
        
        /// <summary>
        /// 检查是否为JsonNode类型
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsJsonNodeType(Type type) =>
            type == typeof(JsonNode);

        #endregion

        #region 值类型处理

        /// <summary>
        /// 优化的值类型setter - 使用PAPart
        /// </summary>
        private static object SetValueOnStructOptimized<T>(object structObj, PAPart part, T value)
        {
            var structType = structObj.GetType();
            var key = new CacheKey(structType, new PAPath( part ), typeof(T));
            
            if (StructSetterCache.TryGetValue(key, out var cached))
            {
                var structSetter = (StructSetter<T>)cached;
                return structSetter(structObj, value);
            }

            var setter = CreateStructSetter<T>(structType, part);
            StructSetterCache[key] = setter;
            
            return setter(structObj, value);
        }

        /// <summary>
        /// 创建值类型setter委托 - 使用PAPart
        /// </summary>
        private static StructSetter<T> CreateStructSetter<T>(Type structType, PAPart part)
        {
            var structParam = Expression.Parameter(typeof(object), "structObj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var structVariable = Expression.Variable(structType, "structCopy");
            
            var assignStruct = Expression.Assign(structVariable, Expression.Convert(structParam, structType));
            
            Expression setValueExpression;
            
            if (part.IsIndex)
            {
                setValueExpression = CreateIndexerSetExpression<T>(structType, part.Index, structVariable, valueParam);
            }
            else
            {
                setValueExpression = CreatePropertyFieldSetExpression<T>(structType, part.Name, structVariable, valueParam);
            }
            
            var returnExpression = Expression.Convert(structVariable, typeof(object));
            
            var blockExpression = Expression.Block(
                new[] { structVariable },
                assignStruct,
                setValueExpression,
                returnExpression
            );
            
            return Expression.Lambda<StructSetter<T>>(blockExpression, structParam, valueParam).Compile();
        }

        /// <summary>
        /// 创建索引器设置表达式（增强Array支持）
        /// </summary>
        private static Expression CreateIndexerSetExpression<T>(Type structType, int index, 
            ParameterExpression structVariable, ParameterExpression valueParam)
        {
            if (structType.IsArray)
            {
                // 对数组使用ArrayIndex优化
                var indexAccess = Expression.ArrayAccess(structVariable, Expression.Constant(index));
                return Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
            }
            else
            {
                // 对其他集合使用索引器
                var indexer = structType.GetProperty("Item");
                if (indexer == null)
                {
                    throw new ArgumentException($"类型 {structType.Name} 不支持索引器");
                }
                var indexAccess = Expression.MakeIndex(structVariable, indexer, new[] { Expression.Constant(index) });
                return Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
            }
        }

        /// <summary>
        /// 创建属性/字段设置表达式
        /// </summary>
        private static Expression CreatePropertyFieldSetExpression<T>(Type structType, string memberName,
            ParameterExpression structVariable, ParameterExpression valueParam)
        {
            var property = structType.GetProperty(memberName);
            if (property != null && property.CanWrite)
            {
                var propertyAccess = Expression.Property(structVariable, property);
                return Expression.Assign(propertyAccess, Expression.Convert(valueParam, property.PropertyType));
            }
            
            var field = structType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var fieldAccess = Expression.Field(structVariable, field);
                return Expression.Assign(fieldAccess, Expression.Convert(valueParam, field.FieldType));
            }
            
            throw new ArgumentException($"成员 {memberName} 在类型 {structType.Name} 中未找到或不可写");
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 将深度转换为字符串长度
        /// </summary>
        private static int ConvertDepthToStringLength(PAPath path, int validDepth)
        {
            if (validDepth <= 0) return 0;
            if (validDepth >= path.Depth) return path.OriginalPath.Length;
            
            // 构建有效部分的字符串长度
            var subPath = path.GetSubPath(0, validDepth);
            return subPath.OriginalPath.Length;
        }

        /// <summary>
        /// 将PAPart转换为字符串
        /// </summary>
        private static string ConvertPartToString(PAPart part)
        {
            return part.IsIndex ? $"[{part.Index}]" : part.Name;
        }

        #endregion
    }
}
