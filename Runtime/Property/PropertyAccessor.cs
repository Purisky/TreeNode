using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    public static class PropertyAccessor
    {
        private readonly struct AccessorKey : IEquatable<AccessorKey>
        {
            public readonly Type Type;
            public readonly int PathHash;
            public readonly Type ValueType;

            public AccessorKey(Type type, int pathHash, Type valueType)
            {
                Type = type;
                PathHash = pathHash;
                ValueType = valueType;
            }

            public bool Equals(AccessorKey other) =>
                Type == other.Type &&
                PathHash == other.PathHash &&
                ValueType == other.ValueType;

            public override int GetHashCode() =>
                HashCode.Combine(Type, PathHash, ValueType);
        }

        // 缓存结构体用于存储成员信息
        private readonly struct MemberInfo
        {
            public readonly PropertyInfo Property;
            public readonly FieldInfo Field;
            public readonly bool IsProperty;
            public readonly bool IsIndexer;
            public readonly string ArrayName;
            public readonly bool HasArrayName;

            public MemberInfo(PropertyInfo property, FieldInfo field, bool isIndexer, string arrayName)
            {
                Property = property;
                Field = field;
                IsProperty = property != null;
                IsIndexer = isIndexer;
                ArrayName = arrayName;
                HasArrayName = !string.IsNullOrEmpty(arrayName);
            }
        }

        private static readonly ConcurrentDictionary<AccessorKey, object> GetterCache = new();
        private static readonly ConcurrentDictionary<AccessorKey, object> SetterCache = new();
        private static readonly ConcurrentDictionary<string, string[]> PathCache = new();
        // Cache to store types that have parameterless constructors
        private static readonly ConcurrentDictionary<Type, bool> HasParameterlessConstructorCache = new();
        // 缓存成员信息以避免重复反射
        private static readonly ConcurrentDictionary<AccessorKey, MemberInfo> MemberInfoCache = new();
        // 缓存值类型setter委托
        private static readonly ConcurrentDictionary<AccessorKey, object> StructSetterCache = new();
        
        public static T GetValue<T>(object obj, string path)
        {
            object parent = TryGetParent(obj, path, out var last);
            var getter_ = GetOrCreateGetter<T>(parent.GetType(), last);
            return getter_(parent);
        }

        public static void SetValue<T>(object obj, string path, T value)
        {
            object parent = TryGetParent(obj, path, out var last);
            // 如果parent是值类型，需要特殊处理
            if (parent.GetType().IsValueType)
            {
                // 使用缓存的高性能setter
                object parentCopy = SetValueOnStructOptimized(parent, last, value);
                // 将修改后的副本设置回原始位置
                string parentPath = path[..^(last.Length + 1)];
                if (!string.IsNullOrEmpty(parentPath))
                {
                    SetValue<object>(obj, parentPath, parentCopy);
                }
                else
                {
                    // 如果没有父路径，说明要修改的就是根对象本身
                    throw new InvalidOperationException("Cannot modify root object when it's a value type");
                }
            }
            else
            {
                var setter = GetOrCreateSetter<T>(parent.GetType(), last);
                setter(parent, value);
            }
        }
        
        public static void SetValueNull(object obj, string path)
        {
            object parent = TryGetParent(obj, path, out var last);
            if (parent is IList list)
            {
                int index = int.Parse(last[1..^1]);
                list.RemoveAt(index);
            }
            else
            {
                var setter = GetOrCreateSetter<object>(parent.GetType(), last);
                setter(parent, null);
            }
        }

        /// <summary>
        /// Get the parent object of the path
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="path"></param>
        /// <param name="last"></param>
        /// <returns>return obj if path has no parent</returns>
        public static object TryGetParent(object obj, string path, out string last)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            string parentPath = PopPath(path);
            if (parentPath == null) { last = path; return obj; }
            last = path[parentPath.Length..].TrimStart('.');
            var currentObj = obj;
            var members = PathCache.GetOrAdd(parentPath, p => p.Split('.'));
            for (int i = 0; i < members.Length; i++)
            {
                TryGetValue(currentObj, members[i], out currentObj);
                currentObj.ThrowIfNull(string.Join('.', members[..i]));
            }
            return currentObj;
        }
        
        public static bool TryGetValue<T>(object obj, string path, out T ret)
        {
            var getter = GetOrCreateGetter<T>(obj.GetType(), path);
            ret = getter(obj);
            return ret != null;
        }

        public static T GetLast<T>(object obj, string path, bool includeEnd, out int index)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            var currentObj = obj;
            var members = PathCache.GetOrAdd(path, p => p.Split('.'));
            T result = default;
            index = 0;
            int indexcount = 0;
            for (int i = 0; i < members.Length - 1; i++)
            {
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), members[i]);
                currentObj = getter(currentObj);
                indexcount += members[i].Length + 1;
                if (currentObj is T t)
                {
                    result = t;
                    index = indexcount;
                }
            }
            if (includeEnd)
            {
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), members[^1]);
                currentObj = getter(currentObj);
                indexcount += members[^1].Length + 1;
                if (currentObj is T t)
                {
                    result = t;
                    index = indexcount;
                }
            }
            return result;
        }
        
        /// <summary>
        /// Checks if a type has a parameterless constructor and is not abstract
        /// </summary>
        private static bool HasValidParameterlessConstructor(Type type)
        {
            if (HasParameterlessConstructorCache.TryGetValue(type, out bool hasConstructor))
            {
                return hasConstructor;
            }
            
            // Check if type is abstract
            if (type.IsAbstract)
            {
                HasParameterlessConstructorCache[type] = false;
                return false;
            }
            
            // Check for parameterless constructor
            var constructor = type.GetConstructor(Type.EmptyTypes);
            hasConstructor = constructor != null;
            HasParameterlessConstructorCache[type] = hasConstructor;
            
            return hasConstructor;
        }
        
        /// <summary>
        /// Checks if a type inherits from JsonNode
        /// </summary>
        private static bool IsJsonNodeType(Type type)
        {
            return type ==typeof(JsonNode);
        }

        /// <summary>
        /// Validates a path through an object hierarchy.
        /// /// <param name="obj">The root object to validate path from</param>
        /// <param name="path">The path to validate</param>
        /// <param name="index">The index in the path string where validation stopped (used to separate valid and invalid parts)</param>
        /// <param name="validPathObject">The object at the valid part of the path</param>
        /// <returns>True if the entire path is valid, false otherwise</returns>
        public static bool GetValidPath(object obj, string path, out int index)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            var currentObj = obj;
            var members = path.Split('.');
            index = 0;
            
            if (members.Length == 0)
            {
                return true;
            }
            int currentPosition = 0;
            object lastValidObj = obj;
            int lastValidPosition = 0;
            
            for (int i = 0; i < members.Length; i++)
            {
                string member = members[i];

                int memberStartPosition = (i == 0) ? 0 : path.IndexOf(member, currentPosition);
                currentPosition = memberStartPosition + member.Length;

                try
                {
                    Type currentType = currentObj.GetType();
                    if (i == members.Length - 1)
                    {
                        if (TryValidateMember(currentObj, member))
                        {
                            index = path.Length;
                            return true;
                        }
                        else
                        {
                            if (member.Contains('['))
                            {
                                int bracketIndex = member.IndexOf('[');
                                string baseName = member.Substring(0, bracketIndex);
                                bool baseExists = false;
                                if (!string.IsNullOrEmpty(baseName))
                                {
                                    var prop = currentType.GetProperty(baseName);
                                    if (prop != null)
                                    {
                                        baseExists = true;
                                    }
                                    else
                                    {
                                        var field = currentType.GetField(baseName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (field != null)
                                        {
                                            baseExists = true;
                                        }
                                    }
                                }
                                
                                if (baseExists)
                                {
                                    index = memberStartPosition + bracketIndex;
                                    return false;
                                }
                            }
                            index = memberStartPosition;
                            return false;
                        }
                    }
                    try
                    {
                        var getter = GetOrCreateGetter<object>(currentType, member);
                        object nextObj = getter(currentObj);
                        
                        // Check if the object is null and can be automatically created
                        if (nextObj == null)
                        {
                            // Get the field or property type
                            Type memberType = null;
                            var property = currentType.GetProperty(member);
                            if (property != null)
                            {
                                memberType = property.PropertyType;
                            }
                            else 
                            {
                                var field = currentType.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (field != null)
                                {
                                    memberType = field.FieldType;
                                }
                            }
                            Debug.Log($"Member: {member}, Type: {memberType?.Name ?? "null"}");

                            // Check if the type can be initialized
                            if (memberType != null && 
                                !IsJsonNodeType(memberType) && 
                                HasValidParameterlessConstructor(memberType))
                            {
                                nextObj = Activator.CreateInstance(memberType);
                                SetValue(obj, path[..currentPosition], nextObj);
                                //var setter = GetOrCreateSetter<object>(currentType, member);
                                //setter(currentObj, nextObj);
                            }
                            else
                            {
                                index = currentPosition;
                                return false;
                            }
                        }
                        
                        lastValidObj = currentObj;
                        lastValidPosition = currentPosition;
                        currentObj = nextObj;
                    }
                    catch
                    {
                        if (member.Contains('['))
                        {
                            int bracketIndex = member.IndexOf('[');
                            string baseName = member.Substring(0, bracketIndex);
                            if (!string.IsNullOrEmpty(baseName))
                            {
                                bool baseExists = false;
                                Type baseType = null;
                                object baseObj = null;
                                
                                try
                                {
                                    var prop = currentType.GetProperty(baseName);
                                    if (prop != null)
                                    {
                                        baseExists = true;
                                        baseType = prop.PropertyType;
                                        baseObj = prop.GetValue(currentObj);
                                    }
                                    else
                                    {
                                        var field = currentType.GetField(baseName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (field != null)
                                        {
                                            baseExists = true;
                                            baseType = field.FieldType;
                                            baseObj = field.GetValue(currentObj);
                                        }
                                    }
                                }
                                catch
                                {
                                }
                                
                                if (baseExists && baseObj != null)
                                {
                                    bool isCollection = typeof(IList).IsAssignableFrom(baseType) || baseType.IsArray;
                                    
                                    if (isCollection)
                                    {
                                        index = memberStartPosition + bracketIndex;
                                        return false;
                                    }
                                }
                            }
                        }
                        index = memberStartPosition;
                        return false;
                    }
                }
                catch
                {
                    index = memberStartPosition;
                    return false;
                }
                
                if (i < members.Length - 1)
                {
                    currentPosition++;
                }
            }
            return true;
        }

        /// <summary>
        /// 获取或创建缓存的成员信息
        /// </summary>
        private static MemberInfo GetOrCreateMemberInfo(Type type, string memberName)
        {
            var key = new AccessorKey(type, memberName.GetHashCode(), typeof(object));
            if (MemberInfoCache.TryGetValue(key, out var cachedInfo))
            {
                return cachedInfo;
            }

            PropertyInfo property = null;
            FieldInfo field = null;
            bool isIndexer = false;
            string arrayName = null;

            if (memberName.EndsWith("]"))
            {
                // 处理索引器
                var indexStart = memberName.IndexOf('[');
                arrayName = memberName[..indexStart];
                isIndexer = true;

                if (!string.IsNullOrEmpty(arrayName))
                {
                    property = type.GetProperty(arrayName);
                    if (property == null)
                    {
                        field = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
            }
            else
            {
                // 处理普通属性或字段
                property = type.GetProperty(memberName);
                if (property == null)
                {
                    field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            var memberInfo = new MemberInfo(property, field, isIndexer, arrayName);
            MemberInfoCache[key] = memberInfo;
            return memberInfo;
        }

        /// <summary>
        /// 优化的TryValidateMember方法，使用缓存避免重复反射
        /// </summary>
        private static bool TryValidateMemberOptimized(object obj, string memberName)
        {
            if (obj == null)
            {
                return false;
            }

            Type type = obj.GetType();
            var memberInfo = GetOrCreateMemberInfo(type, memberName);

            if (memberInfo.IsIndexer)
            {
                // 处理索引器访问
                if (!int.TryParse(memberName.Substring(memberName.IndexOf('[') + 1, memberName.IndexOf(']') - memberName.IndexOf('[') - 1), out int indexValue))
                {
                    return false;
                }

                object collection = obj;
                Type collectionType = type;

                if (memberInfo.HasArrayName)
                {
                    if (memberInfo.IsProperty && memberInfo.Property != null)
                    {
                        try
                        {
                            collection = memberInfo.Property.GetValue(obj);
                            collectionType = memberInfo.Property.PropertyType;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else if (memberInfo.Field != null)
                    {
                        try
                        {
                            collection = memberInfo.Field.GetValue(obj);
                            collectionType = memberInfo.Field.FieldType;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                if (collection == null)
                {
                    return false;
                }

                if (collection is IList list)
                {
                    return indexValue >= 0 && indexValue < list.Count;
                }
                else if (collectionType.IsArray)
                {
                    var array = collection as Array;
                    return array != null && indexValue >= 0 && indexValue < array.Length;
                }
                else
                {
                    var indexerProperty = collectionType.GetProperty("Item");
                    return indexerProperty != null;
                }
            }
            else
            {
                // 普通属性或字段
                return memberInfo.IsProperty && memberInfo.Property != null || 
                       !memberInfo.IsProperty && memberInfo.Field != null;
            }
        }

        private static Func<object, T> GetOrCreateGetter<T>(Type type, string memberName)
        {
            var key = new AccessorKey(type, memberName.GetHashCode(), typeof(T));
            if (GetterCache.TryGetValue(key, out var cachedGetter))
            {
                return (Func<object, T>)cachedGetter;
            }
            var getter = CreateGetter<T>(type, memberName);
            GetterCache[key] = getter;
            return getter;
        }

        private static Action<object, T> GetOrCreateSetter<T>(Type type, string memberName)
        {
            var key = new AccessorKey(type, memberName.GetHashCode(), typeof(T));
            if (SetterCache.TryGetValue(key, out var cachedSetter))
            {
                return (Action<object, T>)cachedSetter;
            }

            var setter = CreateSetter<T>(type, memberName);
            SetterCache[key] = setter;
            return setter;
        }

        private static Func<object, T> CreateGetter<T>(Type type, string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                return obj => (T)obj;
            }

            var param = Expression.Parameter(typeof(object), "obj");
            var current = Expression.Convert(param, type);

            var target = ProcessMemberAccess(current, memberName, ref type);

            if (!typeof(T).IsAssignableFrom(type))
            {
                throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {type.Name}");
            }

            var finalConvert = Expression.Convert(target, typeof(T));
            return Expression.Lambda<Func<object, T>>(finalConvert, param).Compile();
        }

        private static Action<object, T> CreateSetter<T>(Type type, string memberName)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);

            var target = ProcessMemberAccess(current, memberName, ref type);

            if (!typeof(T).IsAssignableFrom(type))
            {
                throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {type.Name}");
            }

            var assignExpr = Expression.Assign(target, Expression.Convert(valueParam, type));
            return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
        }

        // 定义struct setter委托类型
        private delegate object StructSetter<T>(object structObj, T value);

        /// <summary>
        /// 创建高性能的struct setter委托
        /// </summary>
        private static StructSetter<T> CreateStructSetter<T>(Type structType, string memberName)
        {
            var structParam = Expression.Parameter(typeof(object), "structObj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var structVariable = Expression.Variable(structType, "structCopy");
            
            // 将object转换为具体的struct类型
            var assignStruct = Expression.Assign(structVariable, Expression.Convert(structParam, structType));
            
            Expression setValueExpression;
            
            if (memberName.EndsWith("]"))
            {
                // 处理索引器情况
                var indexStart = memberName.IndexOf('[');
                var indexEnd = memberName.IndexOf(']');
                var arrayName = memberName[..indexStart];
                var index = int.Parse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1));
                
                if (!string.IsNullOrEmpty(arrayName))
                {
                    var property = structType.GetProperty(arrayName);
                    if (property != null)
                    {
                        var arrayAccess = Expression.Property(structVariable, property);
                        var indexAccess = Expression.MakeIndex(arrayAccess, arrayAccess.Type.GetProperty("Item"), 
                            new[] { Expression.Constant(index) });
                        setValueExpression = Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
                    }
                    else
                    {
                        var field = structType.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            var arrayAccess = Expression.Field(structVariable, field);
                            var indexAccess = Expression.MakeIndex(arrayAccess, arrayAccess.Type.GetProperty("Item"), 
                                new[] { Expression.Constant(index) });
                            setValueExpression = Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
                        }
                        else
                        {
                            throw new ArgumentException($"Array member {arrayName} not found on type {structType.Name}");
                        }
                    }
                }
                else
                {
                    throw new ArgumentException("Direct indexing on struct not supported");
                }
            }
            else
            {
                // 处理普通属性或字段
                var property = structType.GetProperty(memberName);
                if (property != null && property.CanWrite)
                {
                    var propertyAccess = Expression.Property(structVariable, property);
                    setValueExpression = Expression.Assign(propertyAccess, Expression.Convert(valueParam, property.PropertyType));
                }
                else
                {
                    var field = structType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        var fieldAccess = Expression.Field(structVariable, field);
                        setValueExpression = Expression.Assign(fieldAccess, Expression.Convert(valueParam, field.FieldType));
                    }
                    else
                    {
                        throw new ArgumentException($"Member {memberName} not found on type {structType.Name}");
                    }
                }
            }
            
            // 返回修改后的struct
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
        /// 优化GetValidPath方法中的反射调用
        /// </summary>
        private static Type GetMemberTypeOptimized(Type type, string memberName)
        {
            var memberInfo = GetOrCreateMemberInfo(type, memberName);
            
            if (memberInfo.IsProperty && memberInfo.Property != null)
            {
                return memberInfo.Property.PropertyType;
            }
            else if (memberInfo.Field != null)
            {
                return memberInfo.Field.FieldType;
            }
            
            return null;
        }

        /// <summary>
        /// 在值类型上设置属性值，返回修改后的副本
        /// </summary>
        private static object SetValueOnStruct<T>(object structObj, string memberName, T value)
        {
            Type structType = structObj.GetType();
            
            if (memberName.EndsWith("]"))
            {
                // 处理数组或索引器访问
                var indexStart = memberName.IndexOf('[');
                var indexEnd = memberName.IndexOf(']');
                var arrayName = memberName[..indexStart];
                var index = int.Parse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1));

                if (!string.IsNullOrEmpty(arrayName))
                {
                    // 获取数组/集合属性
                    var arrayProperty = structType.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        var collection = arrayProperty.GetValue(structObj);
                        if (collection is IList list)
                        {
                            list[index] = value;
                        }
                        else if (collection is Array array)
                        {
                            array.SetValue(value, index);
                        }
                        return structObj;
                    }
                    else
                    {
                        var arrayField = structType.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (arrayField != null)
                        {
                            var collection = arrayField.GetValue(structObj);
                            if (collection is IList list)
                            {
                                list[index] = value;
                            }
                            else if (collection is Array array)
                            {
                                array.SetValue(value, index);
                            }
                            return structObj;
                        }
                    }
                }
            }
            else
            {
                // 处理普通属性或字段
                var property = structType.GetProperty(memberName);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(structObj, value);
                    return structObj;
                }
                else
                {
                    var field = structType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(structObj, value);
                        return structObj;
                    }
                }
            }
            
            throw new ArgumentException($"Member {memberName} not found or not writable on type {structType.Name}");
        }

        /// <summary>
        /// 优化的TryValidateMember方法，使用缓存避免重复反射
        /// </summary>
        private static bool TryValidateMember(object obj, string memberName)
        {
            // 使用优化版本替代原来的实现
            return TryValidateMemberOptimized(obj, memberName);
        }

        /// <summary>
        /// 优化版本的值类型setter - 使用Expression树创建高性能委托
        /// </summary>
        private static object SetValueOnStructOptimized<T>(object structObj, string memberName, T value)
        {
            Type structType = structObj.GetType();
            var key = new AccessorKey(structType, memberName.GetHashCode(), typeof(T));
            
            // 尝试从缓存中获取优化的setter
            if (StructSetterCache.TryGetValue(key, out var cachedSetter))
            {
                var structSetter = (StructSetter<T>)cachedSetter;
                return structSetter(structObj, value);
            }

            // 创建高性能的struct setter委托
            var structSetterDelegate = CreateStructSetter<T>(structType, memberName);
            StructSetterCache[key] = structSetterDelegate;
            
            return structSetterDelegate(structObj, value);
        }

        public static string PopPath(string path)
        {
            if (string.IsNullOrEmpty(path)) { return path; }
            int lastDotIndex = path.LastIndexOf('.');
            int lastBracketIndex = path.LastIndexOf('[');

            if (lastDotIndex == -1 && lastBracketIndex == -1)
            {
                return null;
            }
            return path[..Math.Max(lastDotIndex, lastBracketIndex)];
        }

        private static Expression ProcessMemberAccess(Expression current, string memberName, ref Type type)
        {
            if (memberName.EndsWith("]"))
            {
                var indexStart = memberName.IndexOf('[');
                var indexEnd = memberName.IndexOf(']');
                var arrayName = memberName[..indexStart];
                var index = int.Parse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1));

                if (!string.IsNullOrEmpty(arrayName))
                {
                    var arrayProperty = type.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        current = Expression.Property(current, arrayProperty);
                        type = arrayProperty.PropertyType;
                    }
                    else
                    {
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? throw new ArgumentException($"Member {arrayName} not found on type {type.Name}");
                        current = Expression.Field(current, arrayField);
                        type = arrayField.FieldType;
                    }
                }

                var indexer = type.GetProperty("Item")
                    ?? throw new ArgumentException($"Type {type.Name} does not have an indexer");
                current = Expression.MakeIndex(current, indexer, new[] { Expression.Constant(index) });
                type = indexer.PropertyType;
            }
            else
            {
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    current = Expression.Property(current, property);
                    type = property.PropertyType;
                }
                else
                {
                    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new ArgumentException($"Member {memberName} not found on type {type.Name}");
                    current = Expression.Field(current, field);
                    type = field.FieldType;
                }
            }

            return current;
        }
    }
}
