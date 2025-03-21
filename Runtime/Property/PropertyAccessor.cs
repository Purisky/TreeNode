using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace TreeNode.Runtime
{
    public static class PropertyAccessor
    {
        private static readonly Dictionary<string, object> GetterCache = new();
        private static readonly Dictionary<string, object> SetterCache = new();
        public static T GetValue<T>(object obj, PPath pPath)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (!pPath.Valid) throw new ArgumentException("Property path cannot be null or empty");

            object current = obj;
            for (int i = 0; i < pPath.Paths.Length; i++)
            {
                PathPart part = pPath.Paths[i];
                if (part.IsIndex)
                {
                    current = getIndexValue(part, current);
                }
                else
                {
                    current = GetValueWithoutIndex<object>(current, part.Path);
                }
            }

            return (T)current;
        }

        private static T GetValueWithoutIndex<T>(object obj, string path)
        {
            return GetValueInternal<T>(obj, path);
        }




        public static object getIndexValue(PathPart part, object current_)
        {
            if (current_ is System.Collections.IList list)
            {
                if (part.Index < 0 || part.Index >= list.Count)
                    throw new IndexOutOfRangeException($"Index {part.Index} is out of range for list of size {list.Count}");
                return list[part.Index];
            }
            else if (current_ is System.Array array)
            {
                if (part.Index < 0 || part.Index >= array.Length)
                    throw new IndexOutOfRangeException($"Index {part.Index} is out of range for array of size {array.Length}");
                return array.GetValue(part.Index);
            }
            throw new InvalidOperationException("Cannot access index on non-indexable object");
        }
        public static T GetLast<T>(object obj, string propertyPath)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(propertyPath)) throw new ArgumentException("Property path cannot be null or empty");

            var members = propertyPath.Split('.');
            var current = obj;
            var currentType = obj.GetType();
            T last = default;
            if (current is T v)
            {
                last = v;
            }
            for (int i = 0; i < members.Length; i++)
            {
                var memberName = members[i];
                var next = GetValueInternal<object>(current, memberName);
                current = next ?? throw new NullReferenceException($"Member {memberName} is null on type {currentType.Name}");
                if (current is T v_)
                {
                    last = v_;
                }
                currentType = current.GetType();
            }
            return last;
        }
        public static void SetValue<T>(object obj, PPath pPath, T value)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (!pPath.Valid) throw new ArgumentException("Property path cannot be null or empty");
            object parent = GetValue<object>(obj, pPath.Pop(out PathPart last));
            if (last.IsIndex)
            {
                if (obj is System.Collections.IList list)
                {
                    if (last.Index < 0 || last.Index >= list.Count)
                        throw new IndexOutOfRangeException($"Index {last.Index} is out of range for list of size {list.Count}");
                    list[last.Index] = value;
                }
                else if (obj is System.Array array)
                {
                    if (last.Index < 0 || last.Index >= array.Length)
                        throw new IndexOutOfRangeException($"Index {last.Index} is out of range for array of size {array.Length}");
                    array.SetValue(value, last.Index);
                }
                else
                {
                    throw new InvalidOperationException("Cannot access index on non-indexable object");
                }
            }
            else
            {

                var setter = GetOrCreateSetter<T>(parent.GetType(), last.Path);
                setter(parent, value);
            }
        }




        internal static T GetValueInternal<T>(object obj, string propertyPath)
        {
            // 内部方法，跳过null检查等验证
            var getter = GetOrCreateGetter<T>(obj.GetType(), propertyPath);
            return getter(obj);
        }
        private static Func<object, T> GetOrCreateGetter<T>(Type type, string propertyPath)
        {
            var key = $"{type.FullName}.{propertyPath}.{typeof(T).FullName}";
            if (GetterCache.TryGetValue(key, out var cachedGetter))
            {
                return (Func<object, T>)cachedGetter;
            }

            var getter = CreateGetter<T>(type, propertyPath);
            GetterCache[key] = getter;
            return getter;
        }

        private static Action<object, T> GetOrCreateSetter<T>(Type type, string propertyPath)
        {
            var key = $"{type.FullName}.{propertyPath}.{typeof(T).FullName}";
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
            var param = Expression.Parameter(typeof(object), "obj");
            var current = Expression.Convert(param, type);

            foreach (var memberName in propertyPath.Split('.'))
            {
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    current = Expression.Convert(Expression.Property(current, property), property.PropertyType);
                    type = property.PropertyType;
                }
                else
                {
                    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                        throw new ArgumentException($"Member {memberName} not found on type {type.Name}");

                    current = Expression.Convert(Expression.Field(current, field), field.FieldType);
                    type = field.FieldType;
                }
            }

            if (typeof(T) != typeof(object) && typeof(T) != type)
            {
                throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {type.Name}");

            }
            var convert = Expression.Convert(current, typeof(T));
            return Expression.Lambda<Func<object, T>>(convert, param).Compile();
        }

        private static Action<object, T> CreateSetter<T>(Type type, string propertyPath)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var current = Expression.Convert(param, type);

            var members = propertyPath.Split('.');
            for (int i = 0; i < members.Length - 1; i++)
            {
                var memberName = members[i];
                
                // 处理数组/列表索引
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
                        var arrayField = type.GetField(arrayName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (arrayField == null)
                            throw new ArgumentException($"Member {arrayName} not found on type {type.Name}");

                        current = Expression.Convert(Expression.Field(current, arrayField), arrayField.FieldType);
                        type = arrayField.FieldType;
                    }

                    // 处理索引访问
                    var indexer = type.GetProperty("Item");
                    if (indexer == null)
                        throw new ArgumentException($"Type {type.Name} does not have an indexer");

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
                        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field == null)
                            throw new ArgumentException($"Member {memberName} not found on type {type.Name}");

                        current = Expression.Convert(Expression.Field(current, field), field.FieldType);
                        type = field.FieldType;
                    }
                }
            }

            var lastMemberName = members[members.Length - 1];
            var lastProperty = type.GetProperty(lastMemberName);
            if (lastProperty != null)
            {
                if (typeof(T) != lastProperty.PropertyType)
                    throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {lastProperty.PropertyType.Name}");

                var propertyExpr = Expression.Property(current, lastProperty);
                var assignExpr = Expression.Assign(propertyExpr, Expression.Convert(valueParam, lastProperty.PropertyType));
                return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
            }
            else
            {
                var lastField = type.GetField(lastMemberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lastField == null)
                    throw new ArgumentException($"Member {lastMemberName} not found on type {type.Name}");

                if (typeof(T) != lastField.FieldType)
                    throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {lastField.FieldType.Name}");

                var fieldExpr = Expression.Field(current, lastField);
                var assignExpr = Expression.Assign(fieldExpr, Expression.Convert(valueParam, lastField.FieldType));
                return Expression.Lambda<Action<object, T>>(assignExpr, param, valueParam).Compile();
            }
        }
    }
}
