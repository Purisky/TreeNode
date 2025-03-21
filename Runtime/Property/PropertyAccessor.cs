using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using TreeNode.Utility;

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
        private static readonly ConcurrentDictionary<Type, Delegate> TemplateCache = new();
        private static readonly ConcurrentDictionary<string, string[]> PathCache = new();
        private static int cacheUsageCount = 0;
        private const int CacheUsageThreshold = 1000;

        public static void ClearCache()
        {
            GetterCache.Clear();
            SetterCache.Clear();
            TemplateCache.Clear();
            PathCache.Clear();
            cacheUsageCount = 0;
        }

        public static T GetValue<T>(object obj, string path)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));

            var getter = GetOrCreateGetter<T>(obj.GetType(), path);
            IncrementCacheUsage();
            return getter(obj);
        }

        public static void SetValue<T>(object obj, string path, T value)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            var setter = GetOrCreateSetter<T>(obj.GetType(), path);
            IncrementCacheUsage();
            setter(obj, value);
        }

        private static void IncrementCacheUsage()
        {
            cacheUsageCount++;
            if (cacheUsageCount >= CacheUsageThreshold)
            {
                ClearCache();
            }
        }

        private static Func<object, T> GetOrCreateGetter<T>(Type type, string propertyPath)
        {
            var key = new AccessorKey(type, propertyPath.GetHashCode(), typeof(T));
            if (GetterCache.TryGetValue(key, out var cachedGetter))
            {
                return (Func<object, T>)cachedGetter;
            }

            if (TemplateCache.TryGetValue(type, out var template))
            {
                return (Func<object, T>)template;
            }

            var getter = CreateGetter<T>(type, propertyPath);
            GetterCache[key] = getter;

            if (propertyPath == string.Empty)
            {
                TemplateCache[type] = getter;
            }

            return getter;
        }

        private static Action<object, T> GetOrCreateSetter<T>(Type type, string propertyPath)
        {
            var key = new AccessorKey(type, propertyPath.GetHashCode(), typeof(T));
            if (SetterCache.TryGetValue(key, out var cachedSetter))
            {
                return (Action<object, T>)cachedSetter;
            }

            var setter = CreateSetter<T>(type, propertyPath);
            SetterCache[key] = setter;
            return setter;
        }

        private static Func<object, T> CreateGetter<T>(Type type, string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return obj => (T)obj;
            }

            var param = Expression.Parameter(typeof(object), "obj");
            var current = Expression.Convert(param, type);

            var members = PathCache.GetOrAdd(propertyPath, path => path.Split('.'));
            var expressionCache = new List<Expression>(members.Length);

            if (members.Length == 1 && !members[0].Contains("["))
            {
                string memberName = members[0];
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    var propertyExpr = Expression.Property(current, property);
                    var converted = Expression.Convert(propertyExpr, typeof(T));
                    return Expression.Lambda<Func<object, T>>(converted, param).Compile();
                }

                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    var fieldExpr = Expression.Field(current, field);
                    var converted = Expression.Convert(fieldExpr, typeof(T));
                    return Expression.Lambda<Func<object, T>>(converted, param).Compile();
                }
            }

            foreach (var memberName in members)
            {
                if (memberName.EndsWith("]"))
                {
                    var indexStart = memberName.IndexOf('[');
                    var indexEnd = memberName.IndexOf(']');
                    var arrayName = memberName[..indexStart];
                    var index = int.Parse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1));

                    var arrayProperty = type.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        current = Expression.Convert(Expression.Property(current, arrayProperty), arrayProperty.PropertyType);
                        type = arrayProperty.PropertyType;
                    }
                    else
                    {
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? throw new ArgumentException($"Member {arrayName} not found on type {type.Name}");
                        current = Expression.Convert(Expression.Field(current, arrayField), arrayField.FieldType);
                        type = arrayField.FieldType;
                    }

                    var indexer = type.GetProperty("Item")
                        ?? throw new ArgumentException($"Type {type.Name} does not have an indexer");
                    current = Expression.Convert(Expression.MakeIndex(current, indexer, new[] { Expression.Constant(index) }), indexer.PropertyType);
                    type = indexer.PropertyType;
                }
                else
                {
                    var property = type.GetProperty(memberName);
                    if (property != null)
                    {
                        current = Expression.Convert(Expression.Property(current, property), property.PropertyType);
                        type = property.PropertyType;
                    }
                    else
                    {
                        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? throw new ArgumentException($"Member {memberName} not found on type {type.Name}");
                        current = Expression.Convert(Expression.Field(current, field), field.FieldType);
                        type = field.FieldType;
                    }
                }
                expressionCache.Add(current);
            }

            if (typeof(T) != typeof(object) && typeof(T) != type)
            {
                throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {type.Name}");
            }

            var finalConvert = Expression.Convert(current, typeof(T));
            return Expression.Lambda<Func<object, T>>(finalConvert, param).Compile();
        }

        private static Action<object, T> CreateSetter<T>(Type type, string propertyPath)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);

            var members = PathCache.GetOrAdd(propertyPath, path => path.Split('.'));
            var expressionCache = new List<Expression>(members.Length);


            if (members.Length == 1 && !members[0].Contains("["))
            {
                string memberName = members[0];
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    if (typeof(T) != property.PropertyType)
                    {
                        throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {property.PropertyType.Name}");
                    }

                    var propertyExpr = Expression.Property(current, property);
                    var assignExpr = Expression.Assign(propertyExpr, Expression.Convert(valueParam, property.PropertyType));
                    return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
                }

                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    if (typeof(T) != field.FieldType)
                    {
                        throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {field.FieldType.Name}");
                    }

                    var fieldExpr = Expression.Field(current, field);
                    var assignExpr = Expression.Assign(fieldExpr, Expression.Convert(valueParam, field.FieldType));
                    return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
                }
            }

            for (int i = 0; i < members.Length - 1; i++)
            {
                var memberName = members[i];

                if (memberName.EndsWith("]"))
                {
                    var indexStart = memberName.IndexOf('[');
                    var indexEnd = memberName.IndexOf(']');
                    var arrayName = memberName.Substring(0, indexStart);
                    var index = int.Parse(memberName.Substring(indexStart + 1, indexEnd - indexStart - 1));

                    var arrayProperty = type.GetProperty(arrayName);
                    if (arrayProperty != null)
                    {
                        current = Expression.Convert(Expression.Property(current, arrayProperty), arrayProperty.PropertyType);
                        type = arrayProperty.PropertyType;
                    }
                    else
                    {
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? throw new ArgumentException($"Member {arrayName} not found on type {type.Name}");
                        current = Expression.Convert(Expression.Field(current, arrayField), arrayField.FieldType);
                        type = arrayField.FieldType;
                    }

                    var indexer = type.GetProperty("Item")
                        ?? throw new ArgumentException($"Type {type.Name} does not have an indexer");
                    current = Expression.Convert(Expression.MakeIndex(current, indexer, new[] { Expression.Constant(index) }), indexer.PropertyType);
                    type = indexer.PropertyType;
                }
                else
                {
                    var property = type.GetProperty(memberName);
                    if (property != null)
                    {
                        current = Expression.Convert(Expression.Property(current, property), property.PropertyType);
                        type = property.PropertyType;
                    }
                    else
                    {
                        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? throw new ArgumentException($"Member {memberName} not found on type {type.Name}");
                        current = Expression.Convert(Expression.Field(current, field), field.FieldType);
                        type = field.FieldType;
                    }
                }
                expressionCache.Add(current);
            }

            var lastMemberName = members[^1];
            var lastProperty = type.GetProperty(lastMemberName);
            if (lastProperty != null)
            {
                if (typeof(T) != lastProperty.PropertyType)
                {
                    throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {lastProperty.PropertyType.Name}");
                }

                var propertyExpr = Expression.Property(current, lastProperty);
                var assignExpr = Expression.Assign(propertyExpr, Expression.Convert(valueParam, lastProperty.PropertyType));
                return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
            }
            else
            {
                var lastField = type.GetField(lastMemberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new ArgumentException($"Member {lastMemberName} not found on type {type.Name}");
                if (typeof(T) != lastField.FieldType)
                {
                    throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {lastField.FieldType.Name}");
                }

                var fieldExpr = Expression.Field(current, lastField);
                var assignExpr = Expression.Assign(fieldExpr, Expression.Convert(valueParam, lastField.FieldType));
                return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
            }
        }
    }
}