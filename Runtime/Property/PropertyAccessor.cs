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
        public static bool GetValidPath(object obj, string path, out int index, out object validPathObject)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            var currentObj = obj;
            validPathObject = obj; // Start with the root object
            var members = path.Split('.');
            index = 0;
            
            if (members.Length == 0)
            {
                return true; // Empty path after splitting is still valid
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
                            try
                            {
                                var getter = GetOrCreateGetter<object>(currentType, member);
                                object memberValue = getter(currentObj);
                                validPathObject = memberValue; }
                            catch (Exception)
                            {
                                validPathObject = currentObj;
                            }
                            
                            // Path is fully valid
                            index = path.Length;
                            return true;
                        }
                        else
                        {
                            Debug.Log($"GetValidPath: Last member '{member}' is NOT valid");
                            // Last part is invalid
                            // If the member contains an indexer, check if the base property exists
                            if (member.Contains('['))
                            {
                                int bracketIndex = member.IndexOf('[');
                                string baseName = member.Substring(0, bracketIndex);
                                Debug.Log($"GetValidPath: Member contains indexer, base name: '{baseName}', bracket index: {bracketIndex}");
                                
                                // If base property exists but index is invalid
                                bool baseExists = false;
                                if (!string.IsNullOrEmpty(baseName))
                                {
                                    var prop = currentType.GetProperty(baseName);
                                    if (prop != null)
                                    {
                                        baseExists = true;
                                        Debug.Log($"GetValidPath: Base property '{baseName}' exists with type {prop.PropertyType.Name}");
                                    }
                                    else
                                    {
                                        var field = currentType.GetField(baseName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (field != null)
                                        {
                                            baseExists = true;
                                            Debug.Log($"GetValidPath: Base field '{baseName}' exists with type {field.FieldType.Name}");
                                        }
                                        else
                                        {
                                            Debug.Log($"GetValidPath: Base name '{baseName}' does not exist as property or field");
                                        }
                                    }
                                }
                                
                                if (baseExists)
                                {
                                    // Try to get the collection object
                                    try
                                    {
                                        var collectionGetter = GetOrCreateGetter<object>(currentType, baseName);
                                        validPathObject = collectionGetter(currentObj);
                                        Debug.Log($"GetValidPath: Got collection object of type: {(validPathObject != null ? validPathObject.GetType().Name : "null")}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"GetValidPath: Exception getting collection object: {ex.Message}");
                                        validPathObject = lastValidObj;
                                    }
                                    
                                    // Set index to the start of the invalid bracket
                                    index = memberStartPosition + bracketIndex;
                                    Debug.Log($"GetValidPath: Invalid index, returning false with index {index}");
                                    return false;
                                }
                            }
                            
                            // The whole member is invalid
                            validPathObject = lastValidObj;
                            index = memberStartPosition;
                            Debug.Log($"GetValidPath: Invalid member, returning false with index {index}");
                            return false;
                        }
                    }
                    
                    // Not the last part, we need to get the value and continue validation
                    Debug.Log($"GetValidPath: Getting value for member '{member}'");
                    try
                    {
                        var getter = GetOrCreateGetter<object>(currentType, member);
                        object nextObj = getter(currentObj);
                        
                        if (nextObj == null)
                        {
                            // We have a null value in the middle of the path
                            Debug.Log($"GetValidPath: Member '{member}' returned null, path is invalid");
                            validPathObject = lastValidObj;
                            index = currentPosition;
                            return false;
                        }
                        
                        Debug.Log($"GetValidPath: Successfully got value for '{member}', type: {nextObj.GetType().Name}");
                        
                        // Successfully got this part of the path
                        lastValidObj = currentObj;  // Save the parent object
                        lastValidPosition = currentPosition;
                        currentObj = nextObj;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"GetValidPath: Exception accessing member '{member}': {ex.Message}");
                        // Check if the exception is due to an invalid index
                        if (member.Contains('['))
                        {
                            int bracketIndex = member.IndexOf('[');
                            string baseName = member.Substring(0, bracketIndex);
                            Debug.Log($"GetValidPath: Exception with indexed member, base name: '{baseName}'");
                            
                            // If base property exists but index is invalid
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
                                        Debug.Log($"GetValidPath: Base property '{baseName}' exists with type {baseType.Name}");
                                    }
                                    else
                                    {
                                        var field = currentType.GetField(baseName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (field != null)
                                        {
                                            baseExists = true;
                                            baseType = field.FieldType;
                                            baseObj = field.GetValue(currentObj);
                                            Debug.Log($"GetValidPath: Base field '{baseName}' exists with type {baseType.Name}");
                                        }
                                        else
                                        {
                                            Debug.Log($"GetValidPath: Base name '{baseName}' does not exist as property or field");
                                        }
                                    }
                                }
                                catch (Exception innerEx)
                                {
                                    Debug.LogWarning($"GetValidPath: Exception accessing base property/field '{baseName}': {innerEx.Message}");
                                }
                                
                                if (baseExists && baseObj != null)
                                {
                                    bool isCollection = typeof(IList).IsAssignableFrom(baseType) || baseType.IsArray;
                                    Debug.Log($"GetValidPath: Base object is{(isCollection ? "" : " NOT")} a collection");
                                    
                                    if (isCollection)
                                    {
                                        // Return the collection object as the valid path object
                                        validPathObject = baseObj;
                                        // Set index to the start of the invalid bracket
                                        index = memberStartPosition + bracketIndex;
                                        Debug.Log($"GetValidPath: Invalid index on collection, returning false with index {index}");
                                        return false;
                                    }
                                }
                            }
                        }
                        
                        // The whole member is invalid
                        validPathObject = lastValidObj;
                        index = memberStartPosition;
                        Debug.Log($"GetValidPath: Member access failed, returning false with index {index}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // If accessing the member throws an exception, the path is invalid at this point
                    Debug.LogWarning($"GetValidPath: Unexpected exception: {ex.Message}");
                    validPathObject = lastValidObj;
                    index = memberStartPosition;
                    Debug.Log($"GetValidPath: Unexpected exception, returning false with index {index}");
                    return false;
                }
                
                // If we're not at the end, add a dot to the position for the next iteration
                if (i < members.Length - 1)
                {
                    currentPosition++;
                    Debug.Log($"GetValidPath: Moving to next member, position: {currentPosition}");
                }
            }
            
            // If we got here, the whole path is valid
            validPathObject = currentObj;
            Debug.Log($"GetValidPath: Complete path is valid, final object type: {currentObj.GetType().Name}");
            return true;
        }
        
        /// <summary>
        /// Validates a path through an object hierarchy.
        /// /// <param name="obj">The root object to validate path from</param>
        /// <param name="path">The path to validate</param>
        /// <param name="index">The index in the path string where validation stopped (used to separate valid and invalid parts)</param>
        /// <returns>True if the entire path is valid, false otherwise</returns>
        public static bool GetValidPath(object obj, string path, out int index)
        {
            return GetValidPath(obj, path, out index, out _);
        }

        private static bool TryValidateMember(object obj, string memberName)
        {
            if (obj == null)
            {
                Debug.Log($"TryValidateMember: Object is null, cannot validate member '{memberName}'");
                return false;
            }
                
            Type type = obj.GetType();
            Debug.Log($"TryValidateMember: Validating member '{memberName}' on object of type {type.Name}");
                
            if (memberName.EndsWith("]"))
            {
                Debug.Log($"TryValidateMember: Member '{memberName}' is an indexed property/field");
                var indexStart = memberName.IndexOf('[');
                var indexEnd = memberName.IndexOf(']');
                var arrayName = memberName[..indexStart];
                
                Debug.Log($"TryValidateMember: Base name: '{arrayName}', index expression: '{memberName.Substring(indexStart + 1, indexEnd - indexStart - 1)}'");
                
                // Try to parse the index
                if (!int.TryParse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1), out int indexValue))
                {
                    Debug.Log($"TryValidateMember: Failed to parse index as integer");
                    return false; // Invalid index format
                }
                
                Debug.Log($"TryValidateMember: Index value: {indexValue}");
                
                // Handle direct indexing or property with indexer
                object collection = obj;
                
                // If this is a property or field with an indexer
                if (!string.IsNullOrEmpty(arrayName))
                {
                    Debug.Log($"TryValidateMember: Accessing collection property/field '{arrayName}'");
                    // Get the property or field first
                    var arrayProperty = type.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        Debug.Log($"TryValidateMember: Found property '{arrayName}' of type {arrayProperty.PropertyType.Name}");
                        try
                        {
                            collection = arrayProperty.GetValue(obj);
                            Debug.Log($"TryValidateMember: Got collection value: {(collection != null ? "not null" : "null")}");
                            type = arrayProperty.PropertyType;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"TryValidateMember: Exception getting property value: {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (arrayField == null)
                        {
                            Debug.Log($"TryValidateMember: Neither property nor field '{arrayName}' exists on type {type.Name}");
                            return false; // Property/field doesn't exist
                        }
                        
                        Debug.Log($"TryValidateMember: Found field '{arrayName}' of type {arrayField.FieldType.Name}");
                        try
                        {
                            collection = arrayField.GetValue(obj);
                            Debug.Log($"TryValidateMember: Got collection value: {(collection != null ? "not null" : "null")}");
                            type = arrayField.FieldType;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"TryValidateMember: Exception getting field value: {ex.Message}");
                            return false;
                        }
                    }
                }
                
                // If the collection is null, the member isn't valid
                if (collection == null)
                {
                    Debug.Log("TryValidateMember: Collection is null, cannot validate index");
                    return false;
                }
                
                // Check if it's a collection type and the index is in bounds
                if (collection is IList list)
                {
                    Debug.Log($"TryValidateMember: Collection is IList with Count = {list.Count}");
                    bool isValid = indexValue >= 0 && indexValue < list.Count;
                    Debug.Log($"TryValidateMember: Index {indexValue} is {(isValid ? "valid" : "out of bounds")}");
                    return isValid;
                }
                else if (type.IsArray)
                {
                    var array = collection as Array;
                    if (array != null)
                    {
                        Debug.Log($"TryValidateMember: Collection is Array with Length = {array.Length}");
                        bool isValid = indexValue >= 0 && indexValue < array.Length;
                        Debug.Log($"TryValidateMember: Index {indexValue} is {(isValid ? "valid" : "out of bounds")}");
                        return isValid;
                    }
                    Debug.Log("TryValidateMember: Failed to cast collection to Array");
                    return false;
                }
                else
                {
                    // Check if type has an Item indexer property
                    var indexerProperty = type.GetProperty("Item");
                    if (indexerProperty == null)
                    {
                        Debug.Log($"TryValidateMember: Type {type.Name} does not have an indexer property");
                        return false; // No indexer
                    }
                    
                    Debug.Log($"TryValidateMember: Type {type.Name} has an indexer property, cannot validate bounds");
                    // We can't easily check if the index is valid without knowing the specific collection implementation
                    // Just return true if the indexer exists
                    return true;
                }
            }
            else
            {
                Debug.Log($"TryValidateMember: Member '{memberName}' is a regular property/field");
                // Check if the property exists and can be accessed
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    Debug.Log($"TryValidateMember: Property '{memberName}' exists on type {type.Name}");
                    return true;
                }
                
                // Check if the field exists
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool exists = field != null;
                Debug.Log($"TryValidateMember: Field '{memberName}' {(exists ? "exists" : "does not exist")} on type {type.Name}");
                return exists;
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
