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

        private static readonly ConcurrentDictionary<AccessorKey, object> GetterCache = new();
        private static readonly ConcurrentDictionary<AccessorKey, object> SetterCache = new();
        private static readonly ConcurrentDictionary<string, string[]> PathCache = new();
        public static T GetValue<T>(object obj, string path)
        {
            object parent = TryGetParent(obj, path, out var last);
            var getter_ = GetOrCreateGetter<T>(parent.GetType(), last);
            return getter_(parent);
        }
        public static void SetValue<T>(object obj, string path, T value)
        {
            object parent = TryGetParent(obj, path, out var last);
            var setter = GetOrCreateSetter<T>(parent.GetType(), last);
            setter(parent, value);
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
                        
                        if (nextObj == null)
                        {
                            index = currentPosition;
                            return false;
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

        private static bool TryValidateMember(object obj, string memberName)
        {
            if (obj == null)
            {
                return false;
            }
                
            Type type = obj.GetType();
                
            if (memberName.EndsWith("]"))
            {
                var indexStart = memberName.IndexOf('[');
                var indexEnd = memberName.IndexOf(']');
                var arrayName = memberName[..indexStart];
                
                // Try to parse the index
                if (!int.TryParse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1), out int indexValue))
                {
                    return false; // Invalid index format
                }
                
                // Handle direct indexing or property with indexer
                object collection = obj;
                
                // If this is a property or field with an indexer
                if (!string.IsNullOrEmpty(arrayName))
                {
                    // Get the property or field first
                    var arrayProperty = type.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        try
                        {
                            collection = arrayProperty.GetValue(obj);
                            type = arrayProperty.PropertyType;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    else
                    {
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (arrayField == null)
                        {
                            return false; // Property/field doesn't exist
                        }
                        
                        try
                        {
                            collection = arrayField.GetValue(obj);
                            type = arrayField.FieldType;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                
                // If the collection is null, the member isn't valid
                if (collection == null)
                {
                    return false;
                }
                
                // Check if it's a collection type and the index is in bounds
                if (collection is IList list)
                {
                    return indexValue >= 0 && indexValue < list.Count;
                }
                else if (type.IsArray)
                {
                    var array = collection as Array;
                    if (array != null)
                    {
                        return indexValue >= 0 && indexValue < array.Length;
                    }
                    return false;
                }
                else
                {
                    // Check if type has an Item indexer property
                    var indexerProperty = type.GetProperty("Item");
                    if (indexerProperty == null)
                    {
                        return false; // No indexer
                    }
                    
                    // We can't easily check if the index is valid without knowing the specific collection implementation
                    // Just return true if the indexer exists
                    return true;
                }
            }
            else
            {
                // Check if the property exists and can be accessed
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    return true;
                }
                
                // Check if the field exists
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return field != null;
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
