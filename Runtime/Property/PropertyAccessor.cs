using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace TreeNode.Runtime
{
    public static class PropertyAccessor
    {
        private static readonly ConcurrentDictionary<string, Delegate> _getterCache = new();
        private static readonly ConcurrentDictionary<string, Delegate> _setterCache = new();

        public static T GetValue<T>(object obj, string propertyPath)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(propertyPath)) throw new ArgumentException("Property path cannot be null or empty");

            var getter = GetOrCreateGetter<T>(obj.GetType(), propertyPath);
            return getter(obj);
        }

        public static void SetValue<T>(object obj, string propertyPath, T value)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(propertyPath)) throw new ArgumentException("Property path cannot be null or empty");

            var setter = GetOrCreateSetter<T>(obj.GetType(), propertyPath);
            setter(obj, value);
        }

        internal static T GetValueInternal<T>(object obj, string propertyPath)
        {
            // 内部方法，跳过null检查等验证
            var getter = GetOrCreateGetter<T>(obj.GetType(), propertyPath);
            return getter(obj);
        }

        internal static void SetValueInternal<T>(object obj, string propertyPath, T value)
        {
            // 内部方法，跳过null检查等验证
            var setter = GetOrCreateSetter<T>(obj.GetType(), propertyPath);
            setter(obj, value);
        }

        private static Func<object, T> GetOrCreateGetter<T>(Type type, string propertyPath)
        {
            var cacheKey = $"{type.FullName}.{propertyPath}";
            if (_getterCache.TryGetValue(cacheKey, out var cachedGetter))
            {
                return (Func<object, T>)cachedGetter;
            }

            var getter = CreateGetter<T>(type, propertyPath);
            _getterCache[cacheKey] = getter;
            return getter;
        }

        private static Action<object, T> GetOrCreateSetter<T>(Type type, string propertyPath)
        {
            var cacheKey = $"{type.FullName}.{propertyPath}";
            if (_setterCache.TryGetValue(cacheKey, out var cachedSetter))
            {
                return (Action<object, T>)cachedSetter;
            }

            var setter = CreateSetter<T>(type, propertyPath);
            _setterCache[cacheKey] = setter;
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

            if (typeof(T) != type)
                throw new InvalidCastException($"Type mismatch: expected {typeof(T).Name}, actual {type.Name}");

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
                var property = type.GetProperty(members[i]);
                if (property != null)
                {
                    current = Expression.Convert(Expression.Property(current, property), property.PropertyType);
                    type = property.PropertyType;
                }
                else
                {
                    var field = type.GetField(members[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                        throw new ArgumentException($"Member {members[i]} not found on type {type.Name}");

                    current = Expression.Convert(Expression.Field(current, field), field.FieldType);
                    type = field.FieldType;
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
