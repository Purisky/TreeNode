using System;
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
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            object parent = GetParent(obj, path, out var last);
            var getter_ = GetOrCreateGetter<T>(parent.GetType(), last);
            return getter_(parent);
        }

        public static void SetValue<T>(object obj, string path, T value)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));

            object parent = GetParent(obj, path, out var last);
            var setter = GetOrCreateSetter<T>(parent.GetType(), last);
            setter(parent, value);
        }
        static object GetParent(object obj, string path,out string last)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));
            var currentObj = obj;
            var members = PathCache.GetOrAdd(path, p => p.Split('.'));
            foreach (var member in members[..^1])
            {
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), member);
                currentObj = getter(currentObj);
            }
            last = members[^1];
            return currentObj;
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
