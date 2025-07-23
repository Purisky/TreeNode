using System;
using System.Collections.Generic;
using System.Linq;

namespace TreeNode.Runtime.Property.Extensions
{
    /// <summary>
    /// PropertyAccessor扩展方法
    /// </summary>
    public static class PropertyAccessorExtensions
    {
        /// <summary>
        /// 批量获取属性值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="paths">属性路径列表</param>
        /// <returns>属性值列表</returns>
        public static List<T> GetValues<T>(this object obj, params string[] paths)
        {
            return paths.Select(path => PropertyAccessor.GetValue<T>(obj, path)).ToList();
        }

        /// <summary>
        /// 批量设置属性值
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="pathValuePairs">路径-值对</param>
        public static void SetValues<T>(this object obj, params (string path, T value)[] pathValuePairs)
        {
            foreach (var (path, value) in pathValuePairs)
            {
                PropertyAccessor.SetValue(obj, path, value);
            }
        }

        /// <summary>
        /// 批量设置属性值（字典形式）
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="pathValues">路径值字典</param>
        public static void SetValues<T>(this object obj, Dictionary<string, T> pathValues)
        {
            foreach (var kvp in pathValues)
            {
                PropertyAccessor.SetValue(obj, kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// 检查路径是否存在
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <returns>路径是否存在</returns>
        public static bool HasPath(this object obj, string path)
        {
            return PropertyAccessor.GetValidPath(obj, path, out int validLength) && validLength == path.Length;
        }

        /// <summary>
        /// 获取对象的所有属性路径
        /// </summary>
        /// <param name="obj">目标对象</param>
        /// <param name="maxDepth">最大深度</param>
        /// <returns>属性路径列表</returns>
        public static List<string> GetAllPaths(this object obj, int maxDepth = 3)
        {
            var paths = new List<string>();
            CollectPaths(obj, "", 0, maxDepth, paths, new HashSet<object>());
            return paths;
        }

        /// <summary>
        /// 递归收集属性路径
        /// </summary>
        private static void CollectPaths(object obj, string currentPath, int depth, int maxDepth, 
            List<string> paths, HashSet<object> visited)
        {
            if (obj == null || depth >= maxDepth || visited.Contains(obj))
                return;

            visited.Add(obj);
            var type = obj.GetType();

            // 收集属性
            foreach (var property in type.GetProperties())
            {
                if (!property.CanRead) continue;

                var propertyPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";
                paths.Add(propertyPath);

                try
                {
                    var value = property.GetValue(obj);
                    if (value != null && !IsSimpleType(property.PropertyType))
                    {
                        CollectPaths(value, propertyPath, depth + 1, maxDepth, paths, visited);
                    }
                }
                catch
                {
                    // 忽略访问异常
                }
            }

            // 收集字段
            foreach (var field in type.GetFields())
            {
                var fieldPath = string.IsNullOrEmpty(currentPath) ? field.Name : $"{currentPath}.{field.Name}";
                paths.Add(fieldPath);

                try
                {
                    var value = field.GetValue(obj);
                    if (value != null && !IsSimpleType(field.FieldType))
                    {
                        CollectPaths(value, fieldPath, depth + 1, maxDepth, paths, visited);
                    }
                }
                catch
                {
                    // 忽略访问异常
                }
            }

            visited.Remove(obj);
        }

        /// <summary>
        /// 检查是否为简单类型
        /// </summary>
        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || 
                   type.IsEnum || 
                   type == typeof(string) || 
                   type == typeof(DateTime) || 
                   type == typeof(decimal) ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        /// <summary>
        /// 安全获取属性值，返回默认值而不抛出异常
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>属性值或默认值</returns>
        public static T GetValueOrDefault<T>(this object obj, string path, T defaultValue = default)
        {
            try
            {
                return PropertyAccessor.GetValue<T>(obj, path);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 安全设置属性值，不抛出异常
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="path">属性路径</param>
        /// <param name="value">要设置的值</param>
        /// <returns>是否设置成功</returns>
        public static bool TrySetValue<T>(this object obj, string path, T value)
        {
            try
            {
                PropertyAccessor.SetValue(obj, path, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 复制属性值到另一个对象
        /// </summary>
        /// <param name="source">源对象</param>
        /// <param name="target">目标对象</param>
        /// <param name="paths">要复制的属性路径</param>
        public static void CopyPropertiesTo(this object source, object target, params string[] paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    var value = PropertyAccessor.GetValue<object>(source, path);
                    PropertyAccessor.SetValue(target, path, value);
                }
                catch
                {
                    // 忽略复制失败的属性
                }
            }
        }
    }
}