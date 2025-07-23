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
    /// 重构后采用模块化设计提升可维护性和性能
    /// 增强了对Array类型的直接支持
    /// </summary>
    public static class PropertyAccessor
    {
        #region 内部数据结构

        /// <summary>
        /// 优化的缓存键结构
        /// </summary>
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly Type ObjectType;
            public readonly string MemberPath;
            public readonly Type ValueType;
            private readonly int _hashCode;

            public CacheKey(Type objectType, string memberPath, Type valueType)
            {
                ObjectType = objectType;
                MemberPath = memberPath;
                ValueType = valueType;
                _hashCode = HashCode.Combine(objectType, memberPath, valueType);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CacheKey other) =>
                ObjectType == other.ObjectType &&
                MemberPath == other.MemberPath &&
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

        // 获取器缓存
        private static readonly ConcurrentDictionary<CacheKey, object> GetterCache = new();
        
        // 设置器缓存  
        private static readonly ConcurrentDictionary<CacheKey, object> SetterCache = new();
        
        // 路径分割缓存
        private static readonly ConcurrentDictionary<string, string[]> PathCache = new();
        
        // 无参构造函数检查缓存
        private static readonly ConcurrentDictionary<Type, bool> ConstructorCache = new();
        
        // 成员信息缓存
        private static readonly ConcurrentDictionary<CacheKey, MemberMetadata> MemberCache = new();
        
        // 值类型setter缓存
        private static readonly ConcurrentDictionary<CacheKey, object> StructSetterCache = new();

        #endregion

        #region 公共API

        /// <summary>
        /// 获取属性值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>属性值</returns>
        public static T GetValue<T>(object obj, string path)
        {
            var parent = GetParentObject(obj, path, out var lastMember);
            var getter = GetOrCreateGetter<T>(parent.GetType(), lastMember);
            return getter(parent);
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
            var parent = GetParentObject(obj, path, out var lastMember);
            
            // 处理值类型的特殊情况
            if (parent.GetType().IsValueType)
            {
                HandleValueTypeSet(obj, path, lastMember, value);
            }
            else
            {
                var setter = GetOrCreateSetter<T>(parent.GetType(), lastMember);
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
            try
            {
                var getter = GetOrCreateGetter<T>(obj.GetType(), path);
                result = getter(obj);
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
            var parent = GetParentObject(obj, path, out var lastMember);
            
            if (parent is IList list && IsIndexerAccess(lastMember))
            {
                var index = ExtractIndexFromPath(lastMember);
                list.RemoveAt(index);
            }
            // Array类型不支持RemoveAt操作，只能设置为null/default
            else if (parent.GetType().IsArray && IsIndexerAccess(lastMember))
            {
                var index = ExtractIndexFromPath(lastMember);
                var array = (Array)parent;
                var elementType = array.GetType().GetElementType();
                var defaultValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                array.SetValue(defaultValue, index);
            }
            else
            {
                var setter = GetOrCreateSetter<object>(parent.GetType(), lastMember);
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
            
            var currentObj = obj;
            var segments = SplitPath(path);
            validLength = 0;
            
            if (segments.Length == 0)
            {
                validLength = path.Length;
                return true;
            }

            int currentPos = 0;
            
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                int segmentStart = (i == 0) ? 0 : path.IndexOf(segment, currentPos);
                currentPos = segmentStart + segment.Length;

                try
                {
                    if (i == segments.Length - 1)
                    {
                        // 最后一个段，验证成员存在性
                        if (ValidateMemberExists(currentObj, segment))
                        {
                            validLength = path.Length;
                            return true;
                        }
                        else
                        {
                            validLength = HandleInvalidFinalSegment(currentObj, segment, segmentStart);
                            return false;
                        }
                    }
                    else
                    {
                        // 中间段，尝试获取下一个对象
                        currentObj = NavigateToNextObject(obj, currentObj, path, segment, currentPos);
                        if (currentObj == null)
                        {
                            validLength = currentPos;
                            return false;
                        }
                    }
                }
                catch
                {
                    validLength = HandleNavigationError(currentObj, segment, segmentStart);
                    return false;
                }
                
                if (i < segments.Length - 1)
                {
                    currentPos++;
                }
            }
            
            validLength = path.Length;
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
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            
            var currentObj = obj;
            var segments = SplitPath(path);
            T result = default;
            index = 0;
            int currentPos = 0;
            
            int endIndex = includeEnd ? segments.Length : segments.Length - 1;
            
            for (int i = 0; i < endIndex; i++)
            {
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), segments[i]);
                currentObj = getter(currentObj);
                currentPos += segments[i].Length + 1;
                
                if (currentObj is T typedObj)
                {
                    result = typedObj;
                    index = currentPos;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 获取父对象
        /// </summary>
        public static object GetParentObject(object obj, string path, out string lastMember)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));

            string parentPath = ExtractParentPath(path);
            if (parentPath == null)
            {
                lastMember = path;
                return obj;
            }

            lastMember = path[parentPath.Length..].TrimStart('.');
            var currentObj = obj;
            var segments = SplitPath(parentPath);

            for (int i = 0; i < segments.Length; i++)
            {
                TryGetValue(currentObj, segments[i], out currentObj);
                currentObj.ThrowIfNull(string.Join('.', segments[..i]));
            }

            return currentObj;
        }

        /// <summary>
        /// 获取数组长度
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="arrayPath">数组路径</param>
        /// <returns>数组长度，如果不是数组则返回-1</returns>
        public static int GetArrayLength(object obj, string arrayPath)
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

        #endregion

        #region 私有核心方法

        /// <summary>
        /// 处理值类型的设置操作
        /// </summary>
        private static void HandleValueTypeSet<T>(object rootObj, string fullPath, string memberName, T value)
        {
            var parentCopy = SetValueOnStructOptimized(GetParentFromFullPath(rootObj, fullPath, memberName), memberName, value);
            
            string parentPath = fullPath[..^(memberName.Length + 1)];
            if (!string.IsNullOrEmpty(parentPath))
            {
                SetValue<object>(rootObj, parentPath, parentCopy);
            }
            else
            {
                throw new InvalidOperationException("无法修改值类型的根对象");
            }
        }

        /// <summary>
        /// 从完整路径获取父对象
        /// </summary>
        private static object GetParentFromFullPath(object rootObj, string fullPath, string memberName)
        {
            string parentPath = fullPath[..^(memberName.Length + 1)];
            return string.IsNullOrEmpty(parentPath) ? rootObj : GetValue<object>(rootObj, parentPath);
        }

        #endregion

        #region 缓存和创建方法

        /// <summary>
        /// 获取或创建Getter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Func<object, T> GetOrCreateGetter<T>(Type type, string memberName)
        {
            var key = new CacheKey(type, memberName, typeof(T));
            if (GetterCache.TryGetValue(key, out var cached))
                return (Func<object, T>)cached;
            
            var getter = CreateGetter<T>(type, memberName);
            GetterCache[key] = getter;
            return getter;
        }

        /// <summary>
        /// 获取或创建Setter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Action<object, T> GetOrCreateSetter<T>(Type type, string memberName)
        {
            var key = new CacheKey(type, memberName, typeof(T));
            if (SetterCache.TryGetValue(key, out var cached))
                return (Action<object, T>)cached;
            
            var setter = CreateSetter<T>(type, memberName);
            SetterCache[key] = setter;
            return setter;
        }

        /// <summary>
        /// 创建Getter表达式
        /// </summary>
        private static Func<object, T> CreateGetter<T>(Type type, string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
                return obj => (T)obj;

            var param = Expression.Parameter(typeof(object), "obj");
            var current = Expression.Convert(param, type);
            var target = BuildMemberAccess(current, memberName, ref type);

            if (!typeof(T).IsAssignableFrom(type))
                throw new InvalidCastException($"类型不匹配: 期望 {typeof(T).Name}, 实际 {type.Name}");

            var finalConvert = Expression.Convert(target, typeof(T));
            return Expression.Lambda<Func<object, T>>(finalConvert, param).Compile();
        }

        /// <summary>
        /// 创建Setter表达式
        /// </summary>
        private static Action<object, T> CreateSetter<T>(Type type, string memberName)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);
            Expression target;
            Type targetType;

            try
            {
                targetType = type;
                target = BuildMemberAccess(current, memberName, ref targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法构建成员访问表达式: 类型={type.Name}, 成员={memberName}, 错误={ex.Message}", ex);
            }

            // 检查是否可以写入
            if (!CanWriteToMember(type, memberName))
            {
                throw new InvalidOperationException($"成员 {memberName} 在类型 {type.Name} 中为只读或不可写");
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
                    valueExpression = valueParam;
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
                throw new InvalidOperationException($"创建Setter失败: 类型={type.Name}, 成员={memberName}, 值类型={typeof(T).Name}, 目标类型={targetType.Name}, 错误={ex.Message}", ex);
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
        /// 检查成员是否可写
        /// </summary>
        private static bool CanWriteToMember(Type type, string memberName)
        {
            if (IsIndexerAccess(memberName))
            {
                var (collectionName, _) = ParseIndexerAccess(memberName);
                
                if (!string.IsNullOrEmpty(collectionName))
                {
                    // 检查集合属性/字段
                    var property = type.GetProperty(collectionName);
                    if (property != null)
                    {
                        // 集合本身存在且索引器可写（数组或IList）
                        return property.PropertyType.IsArray || typeof(IList).IsAssignableFrom(property.PropertyType);
                    }
                    
                    var field = type.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && !field.IsInitOnly)
                    {
                        return field.FieldType.IsArray || typeof(IList).IsAssignableFrom(field.FieldType);
                    }
                    
                    return false;
                }
                else
                {
                    // 直接对类型进行索引访问
                    return type.IsArray || typeof(IList).IsAssignableFrom(type);
                }
            }
            else
            {
                // 普通属性/字段访问
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    return property.CanWrite;
                }
                
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
        /// 构建成员访问表达式
        /// </summary>
        private static Expression BuildMemberAccess(Expression current, string memberName, ref Type type)
        {
            if (IsIndexerAccess(memberName))
            {
                return BuildIndexerAccess(current, memberName, ref type);
            }
            else
            {
                return BuildPropertyOrFieldAccess(current, memberName, ref type);
            }
        }

        /// <summary>
        /// 构建索引器访问表达式（增强Array支持）
        /// </summary>
        private static Expression BuildIndexerAccess(Expression current, string memberName, ref Type type)
        {
            var (collectionName, index) = ParseIndexerAccess(memberName);

            if (!string.IsNullOrEmpty(collectionName))
            {
                // 先访问集合属性/字段
                current = BuildPropertyOrFieldAccess(current, collectionName, ref type);
            }

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

        #region 路径处理工具

        /// <summary>
        /// 分割路径
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string[] SplitPath(string path) =>
            PathCache.GetOrAdd(path, p => p.Split('.'));

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

        /// <summary>
        /// 检查是否为索引器访问
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIndexerAccess(string memberName) =>
            memberName.EndsWith("]");

        /// <summary>
        /// 从路径中提取索引
        /// </summary>
        private static int ExtractIndexFromPath(string memberName)
        {
            int start = memberName.IndexOf('[') + 1;
            int end = memberName.IndexOf(']');
            return int.Parse(memberName.Substring(start, end - start));
        }

        /// <summary>
        /// 解析索引器访问
        /// </summary>
        private static (string collectionName, int index) ParseIndexerAccess(string memberName)
        {
            int bracketStart = memberName.IndexOf('[');
            int bracketEnd = memberName.IndexOf(']');
            
            string collectionName = memberName[..bracketStart];
            int index = int.Parse(memberName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1));
            
            return (collectionName, index);
        }

        #endregion

        #region 成员信息管理

        /// <summary>
        /// 获取或创建成员信息
        /// </summary>
        private static MemberMetadata GetOrCreateMemberInfo(Type type, string memberName)
        {
            var key = new CacheKey(type, memberName, typeof(object));
            if (MemberCache.TryGetValue(key, out var cached))
                return cached;

            var metadata = CreateMemberMetadata(type, memberName);
            MemberCache[key] = metadata;
            return metadata;
        }

        /// <summary>
        /// 创建成员元数据（增强Array检测）
        /// </summary>
        private static MemberMetadata CreateMemberMetadata(Type type, string memberName)
        {
            PropertyInfo property = null;
            FieldInfo field = null;
            bool isIndexer = false;
            bool isArray = false;
            string collectionName = null;

            if (IsIndexerAccess(memberName))
            {
                var (name, _) = ParseIndexerAccess(memberName);
                collectionName = name;
                isIndexer = true;

                if (!string.IsNullOrEmpty(collectionName))
                {
                    property = type.GetProperty(collectionName);
                    if (property == null)
                    {
                        field = type.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    
                    // 检查是否为数组类型
                    var memberType = property?.PropertyType ?? field?.FieldType;
                    isArray = memberType?.IsArray == true;
                }
                else
                {
                    // 直接对当前对象进行索引访问
                    isArray = type.IsArray;
                }
            }
            else
            {
                property = type.GetProperty(memberName);
                if (property == null)
                {
                    field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            return new MemberMetadata(property, field, isIndexer, isArray, collectionName);
        }

        #endregion

        #region 验证方法

        /// <summary>
        /// 验证成员是否存在
        /// </summary>
        private static bool ValidateMemberExists(object obj, string memberName)
        {
            if (obj == null) return false;

            var metadata = GetOrCreateMemberInfo(obj.GetType(), memberName);
            
            if (metadata.IsIndexer)
            {
                return ValidateIndexerAccess(obj, memberName, metadata);
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
        private static bool ValidateIndexerAccess(object obj, string memberName, MemberMetadata metadata)
        {
            var (_, index) = ParseIndexerAccess(memberName);
            
            object collection = obj;
            Type collectionType = obj.GetType();

            if (metadata.HasCollectionName)
            {
                try
                {
                    if (metadata.IsProperty && metadata.Property != null)
                    {
                        collection = metadata.Property.GetValue(obj);
                        collectionType = metadata.Property.PropertyType;
                    }
                    else if (metadata.Field != null)
                    {
                        collection = metadata.Field.GetValue(obj);
                        collectionType = metadata.Field.FieldType;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

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
        /// 导航到下一个对象
        /// </summary>
        private static object NavigateToNextObject(object rootObj, object currentObj, string fullPath, string segment, int currentPos)
        {
            var getter = GetOrCreateGetter<object>(currentObj.GetType(), segment);
            object nextObj = getter(currentObj);
            
            // 如果对象为null，尝试自动创建
            if (nextObj == null)
            {
                nextObj = TryCreateMissingObject(rootObj, fullPath, segment, currentPos, currentObj.GetType());
            }
            
            return nextObj;
        }

        /// <summary>
        /// 尝试创建缺失的对象
        /// </summary>
        private static object TryCreateMissingObject(object rootObj, string fullPath, string memberName, int currentPos, Type parentType)
        {
            var memberType = GetMemberType(parentType, memberName);
            
            if (memberType != null && 
                !IsJsonNodeType(memberType) && 
                HasValidParameterlessConstructor(memberType))
            {
                var newObj = Activator.CreateInstance(memberType);
                SetValue(rootObj, fullPath[..currentPos], newObj);
                return newObj;
            }
            
            return null;
        }

        /// <summary>
        /// 获取成员类型
        /// </summary>
        private static Type GetMemberType(Type type, string memberName)
        {
            var metadata = GetOrCreateMemberInfo(type, memberName);
            return metadata.IsProperty && metadata.Property != null 
                ? metadata.Property.PropertyType 
                : metadata.Field?.FieldType;
        }

        /// <summary>
        /// 处理无效的最终段
        /// </summary>
        private static int HandleInvalidFinalSegment(object currentObj, string segment, int segmentStart)
        {
            if (!IsIndexerAccess(segment)) 
                return segmentStart;

            var (collectionName, _) = ParseIndexerAccess(segment);
            if (!string.IsNullOrEmpty(collectionName))
            {
                var type = currentObj.GetType();
                var property = type.GetProperty(collectionName);
                var field = type.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (property != null || field != null)
                {
                    return segmentStart + segment.IndexOf('[');
                }
            }
            
            return segmentStart;
        }

        /// <summary>
        /// 处理导航错误
        /// </summary>
        private static int HandleNavigationError(object currentObj, string segment, int segmentStart)
        {
            if (!IsIndexerAccess(segment))
                return segmentStart;

            var (collectionName, _) = ParseIndexerAccess(segment);
            if (string.IsNullOrEmpty(collectionName))
                return segmentStart;

            try
            {
                var type = currentObj.GetType();
                var property = type.GetProperty(collectionName);
                var field = type.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if ((property != null || field != null))
                {
                    var collection = property?.GetValue(currentObj) ?? field?.GetValue(currentObj);
                    if (collection != null)
                    {
                        var memberType = property?.PropertyType ?? field?.FieldType;
                        bool isCollection = typeof(IList).IsAssignableFrom(memberType) || memberType.IsArray;
                        
                        if (isCollection)
                        {
                            return segmentStart + segment.IndexOf('[');
                        }
                    }
                }
            }
            catch
            {
                // 忽略异常
            }
            
            return segmentStart;
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
        /// 优化的值类型setter
        /// </summary>
        private static object SetValueOnStructOptimized<T>(object structObj, string memberName, T value)
        {
            var structType = structObj.GetType();
            var key = new CacheKey(structType, memberName, typeof(T));
            
            if (StructSetterCache.TryGetValue(key, out var cached))
            {
                var structSetter = (StructSetter<T>)cached;
                return structSetter(structObj, value);
            }

            var setter = CreateStructSetter<T>(structType, memberName);
            StructSetterCache[key] = setter;
            
            return setter(structObj, value);
        }

        /// <summary>
        /// 创建值类型setter委托
        /// </summary>
        private static StructSetter<T> CreateStructSetter<T>(Type structType, string memberName)
        {
            var structParam = Expression.Parameter(typeof(object), "structObj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var structVariable = Expression.Variable(structType, "structCopy");
            
            var assignStruct = Expression.Assign(structVariable, Expression.Convert(structParam, structType));
            
            Expression setValueExpression;
            
            if (IsIndexerAccess(memberName))
            {
                setValueExpression = CreateIndexerSetExpression<T>(structType, memberName, structVariable, valueParam);
            }
            else
            {
                setValueExpression = CreatePropertyFieldSetExpression<T>(structType, memberName, structVariable, valueParam);
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
        private static Expression CreateIndexerSetExpression<T>(Type structType, string memberName, 
            ParameterExpression structVariable, ParameterExpression valueParam)
        {
            var (collectionName, index) = ParseIndexerAccess(memberName);
            
            if (string.IsNullOrEmpty(collectionName))
                throw new ArgumentException("值类型不支持直接索引访问");
            
            var property = structType.GetProperty(collectionName);
            if (property != null)
            {
                var arrayAccess = Expression.Property(structVariable, property);
                return CreateIndexAccessExpression<T>(arrayAccess, index, valueParam);
            }
            
            var field = structType.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var arrayAccess = Expression.Field(structVariable, field);
                return CreateIndexAccessExpression<T>(arrayAccess, index, valueParam);
            }
            
            throw new ArgumentException($"集合成员 {collectionName} 在类型 {structType.Name} 中未找到");
        }

        /// <summary>
        /// 创建索引访问表达式（区分Array和索引器）
        /// </summary>
        private static Expression CreateIndexAccessExpression<T>(Expression arrayAccess, int index, ParameterExpression valueParam)
        {
            if (arrayAccess.Type.IsArray)
            {
                // 对数组使用ArrayIndex优化
                var indexAccess = Expression.ArrayAccess(arrayAccess, Expression.Constant(index));
                return Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
            }
            else
            {
                // 对其他集合使用索引器
                var indexer = arrayAccess.Type.GetProperty("Item");
                if (indexer == null)
                {
                    throw new ArgumentException($"类型 {arrayAccess.Type.Name} 不支持索引器");
                }
                var indexAccess = Expression.MakeIndex(arrayAccess, indexer, new[] { Expression.Constant(index) });
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

        //#region 兼容性方法

        ///// <summary>
        ///// 兼容原有的PopPath方法
        ///// </summary>
        //[Obsolete("使用 ExtractParentPath 替代")]
        //public static string PopPath(string path) => ExtractParentPath(path);

        ///// <summary>
        ///// 兼容原有的TryGetParent方法
        ///// </summary>
        //[Obsolete("使用 GetParentObject 替代")]
        //public static object TryGetParent(object obj, string path, out string last) =>
        //    GetParentObject(obj, path, out last);

        //#endregion
    }
}
