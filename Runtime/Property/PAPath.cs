using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TreeNode.Runtime
{
    /// <summary>
    /// 高性能字段路径结构 - 支持路径解析、缓存和快速访问
    /// 提供与PropertyAccessor兼容的高性能路径操作
    /// </summary>
    public struct PAPath : IEquatable<PAPath>
    {
        #region 字段

        public PAPart[] Parts;
        private readonly string _originalPath;
        private readonly int _hashCode;

        #endregion



        #region 静态缓存

        // 路径解析缓存
        private static readonly ConcurrentDictionary<string, PAPath> PathCache = new();
        
        // 路径分割缓存
        private static readonly ConcurrentDictionary<string, string[]> SegmentCache = new();

        #endregion

        #region 构造函数





        /// <summary>
        /// 从字符串路径构造PAPath
        /// </summary>
        /// <param name="path">字符串路径</param>
        public PAPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {

                Parts = new PAPart[0];
                _originalPath = string.Empty;
                _hashCode = 0;
                return;
            }

            _originalPath = path;
            Parts = ParsePathToParts(path);
            _hashCode = ComputeHashCode(path);
        }

        /// <summary>
        /// 从路径部分数组构造PAPath
        /// </summary>
        /// <param name="parts">路径部分数组</param>
        public PAPath(PAPart[] parts)
        {
            Parts = parts ?? new PAPart[0];
            _originalPath = PartsToString(Parts);
            _hashCode = ComputeHashCode(_originalPath);
        }
        public PAPath(PAPart part)
        {
            Parts =  new PAPart[] { part };
            _originalPath = PartsToString(Parts);
            _hashCode = ComputeHashCode(_originalPath);
        }






        #endregion

        #region 公共属性
        public readonly bool Root=> Parts != null && Parts.Length == 1 && Parts[0].IsIndex;
        public readonly bool ItemOfCollection => Valid && Parts[^1].IsIndex;
        public readonly bool Valid => Parts != null && Parts.Length > 0;
        public readonly bool ExistParent => Parts != null && Parts.Length > 1;
        /// <summary>
        /// 路径是否为空
        /// </summary>
        public readonly bool IsEmpty => Parts == null || Parts.Length == 0;
        /// <summary>
        /// 路径深度
        /// </summary>
        public readonly int Depth => Parts?.Length ?? 0;

        /// <summary>
        /// 原始路径字符串
        /// </summary>
        public readonly string OriginalPath => _originalPath ?? string.Empty;

        /// <summary>
        /// 是否只包含字段访问（不包含索引器）
        /// </summary>
        public readonly bool IsFieldsOnly
        {
            get
            {
                if (Parts == null) return true;
                foreach (var part in Parts)
                {
                    if (part.IsIndex) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 是否包含索引器访问
        /// </summary>
        public readonly bool HasIndexer
        {
            get
            {
                if (Parts == null) return false;
                foreach (var part in Parts)
                {
                    if (part.IsIndex) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 最后一个路径部分
        /// </summary>
        public readonly PAPart LastPart => Valid ? Parts[^1] : PAPart._;

        /// <summary>
        /// 第一个路径部分
        /// </summary>
        public readonly PAPart FirstPart => Valid ? Parts[0] : PAPart._;
        //public readonly PAPath SkipFirst => GetSubPath(1);
        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 从字符串创建PAPath（使用缓存）
        /// </summary>
        /// <param name="path">字符串路径</param>
        /// <returns>PAPath实例</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PAPath Create(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new PAPath();

            return PathCache.GetOrAdd(path, p => new PAPath(p));
        }

        /// <summary>
        /// 从多个路径部分创建PAPath
        /// </summary>
        /// <param name="parts">路径部分</param>
        /// <returns>PAPath实例</returns>
        public static PAPath Create(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return new PAPath();

            var pathParts = new PAPart[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                pathParts[i] = PAPart.FromString(parts[i]);
            }

            return new PAPath(pathParts);
        }

        /// <summary>
        /// 创建字段访问路径
        /// </summary>
        /// <param name="fieldName">字段名</param>
        /// <returns>PAPath实例</returns>
        public static PAPath Field(string fieldName) => Create(fieldName);

        /// <summary>
        /// 创建索引访问路径
        /// </summary>
        /// <param name="index">索引值</param>
        /// <returns>PAPath实例</returns>
        public static PAPath Index(int index) => new PAPath(new[] { PAPart.FromIndex(index) });

        /// <summary>
        /// 创建字段+索引访问路径
        /// </summary>
        /// <param name="fieldName">字段名</param>
        /// <param name="index">索引值</param>
        /// <returns>PAPath实例</returns>
        public static PAPath FieldIndex(string fieldName, int index) => 
            Create($"{fieldName}[{index}]");

        #endregion

        #region 路径操作方法

        /// <summary>
        /// 添加字段访问
        /// </summary>
        /// <param name="fieldName">字段名</param>
        /// <returns>新的PAPath</returns>
        public readonly PAPath AppendField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return this;

            var newParts = new PAPart[Depth + 1];
            if (Parts != null)
                Array.Copy(Parts, newParts, Parts.Length);
            
            newParts[Depth] = PAPart.FromString(fieldName);
            return new PAPath(newParts);
        }

        /// <summary>
        /// 添加索引访问
        /// </summary>
        /// <param name="index">索引值</param>
        /// <returns>新的PAPath</returns>
        public readonly PAPath AppendIndex(int index)
        {
            var newParts = new PAPart[Depth + 1];
            if (Parts != null)
                Array.Copy(Parts, newParts, Parts.Length);
            
            newParts[Depth] = PAPart.FromIndex(index);
            return new PAPath(newParts);
        }

        /// <summary>
        /// 组合另一个路径
        /// </summary>
        /// <param name="other">要组合的路径</param>
        /// <returns>新的PAPath</returns>
        public readonly PAPath Combine(PAPath other)
        {
            if (other.IsEmpty) return this;
            if (IsEmpty) return other;

            var newParts = new PAPart[Depth + other.Depth];
            Array.Copy(Parts, newParts, Parts.Length);
            Array.Copy(other.Parts, 0, newParts, Parts.Length, other.Parts.Length);
            
            return new PAPath(newParts);
        }

        /// <summary>
        /// 获取父级路径
        /// </summary>
        /// <returns>父级路径，如果没有则返回空路径</returns>
        public readonly PAPath GetParent()
        {
            if (Depth <= 1) return new PAPath();

            var parentParts = new PAPart[Depth - 1];
            Array.Copy(Parts, parentParts, parentParts.Length);
            return new PAPath(parentParts);
        }

        /// <summary>
        /// 获取指定深度的子路径
        /// </summary>
        /// <param name="startIndex">起始索引</param>
        /// <param name="count">部分数量</param>
        /// <returns>子路径</returns>
        public readonly PAPath GetSubPath(int startIndex, int count = -1)
        {
            if (Parts == null || startIndex >= Parts.Length || startIndex < 0)
                return new PAPath();

            if (count < 0)
                count = Parts.Length - startIndex;

            count = Math.Min(count, Parts.Length - startIndex);
            if (count <= 0)
                return new PAPath();

            var subParts = new PAPart[count];
            Array.Copy(Parts, startIndex, subParts, 0, count);
            return new PAPath(subParts);
        }

        #endregion

        #region 性能优化方法

        /// <summary>
        /// 快速访问 - 直接使用PropertyAccessor获取值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <returns>属性值</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T GetValue<T>(object obj)
        {
            return PropertyAccessor.GetValue<T>(obj, this);
        }

        /// <summary>
        /// 快速设置 - 直接使用PropertyAccessor设置值
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="value">要设置的值</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void SetValue<T>(object obj, T value)
        {
            PropertyAccessor.SetValue(obj, this, value);
        }

        /// <summary>
        /// 安全获取值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>属性值或默认值</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T GetValueOrDefault<T>(object obj, T defaultValue = default)
        {
            try
            {
                return GetValue<T>(obj);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 安全设置值
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="obj">目标对象</param>
        /// <param name="value">要设置的值</param>
        /// <returns>是否设置成功</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TrySetValue<T>(object obj, T value)
        {
            try
            {
                SetValue(obj, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 解析字符串路径为路径部分数组
        /// </summary>
        private static PAPart[] ParsePathToParts(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new PAPart[0];

            var segments = SegmentCache.GetOrAdd(path, SplitPathString);
            var parts = new List<PAPart>();

            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment)) continue;

                // 检查是否包含索引器
                if (segment.Contains('[') && segment.Contains(']'))
                {
                    var indexStart = segment.IndexOf('[');
                    if (indexStart > 0)
                    {
                        // 先添加字段部分
                        var fieldName = segment.Substring(0, indexStart);
                        parts.Add(PAPart.FromString(fieldName));
                    }

                    // 解析所有索引器
                    var remaining = segment.Substring(indexStart);
                    while (remaining.Contains('[') && remaining.Contains(']'))
                    {
                        var start = remaining.IndexOf('[');
                        var end = remaining.IndexOf(']');
                        var indexStr = remaining.Substring(start + 1, end - start - 1);
                        
                        if (int.TryParse(indexStr, out int index))
                        {
                            parts.Add(PAPart.FromIndex(index));
                        }

                        remaining = remaining.Substring(end + 1);
                    }
                }
                else
                {
                    parts.Add(PAPart.FromString(segment));
                }
            }

            return parts.ToArray();
        }

        /// <summary>
        /// 分割路径字符串
        /// </summary>
        private static string[] SplitPathString(string path)
        {
            return path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// 将路径部分数组转换为字符串
        /// </summary>
        private static string PartsToString(PAPart[] parts)
        {
            if (parts == null || parts.Length == 0)
                return string.Empty;

            var result = new System.Text.StringBuilder();
            bool needsDot = false;
            foreach (var part in parts)
            {
                if (part.IsIndex)
                {
                    result.Append($"[{part.Index}]");
                }
                else
                {
                    if (needsDot) result.Append('.');
                    result.Append(part.Name);
                    
                }
                needsDot = true;
            }

            return result.ToString();
        }

        /// <summary>
        /// 计算哈希码
        /// </summary>
        private static int ComputeHashCode(string path)
        {
            return path?.GetHashCode() ?? 0;
        }
        #endregion

        #region 相等性和哈希

        public readonly bool Equals(PAPath other)
        {
            return string.Equals(OriginalPath, other.OriginalPath, StringComparison.Ordinal);
        }

        public override readonly bool Equals(object obj)
        {
            return obj is PAPath other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return _hashCode;
        }

        public static bool operator ==(PAPath left, PAPath right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PAPath left, PAPath right)
        {
            return !left.Equals(right);
        }

        #endregion

        #region 类型转换

        /// <summary>
        /// 隐式转换从字符串到PAPath
        /// </summary>
        public static implicit operator PAPath(string path)
        {
            return Create(path);
        }

        /// <summary>
        /// 隐式转换从PAPath到字符串
        /// </summary>
        public static implicit operator string(PAPath path)
        {
            return path.OriginalPath;
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public static void ClearCache()
        {
            PathCache.Clear();
            SegmentCache.Clear();
        }

        #endregion

        #region ToString

        public override readonly string ToString()
        {
            return OriginalPath;
        }

        #endregion

        #region 路径比较方法

        /// <summary>
        /// 检查当前路径是否以指定路径开头
        /// </summary>
        /// <param name="prefix">前缀路径</param>
        /// <returns>如果当前路径以指定路径开头则返回true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool StartsWith(PAPath prefix)
        {
            // 空路径或无效路径处理
            if (prefix.IsEmpty) return true;
            if (IsEmpty) return false;
            if (prefix.Depth > Depth) return false;

            // 逐个比较路径部分
            for (int i = 0; i < prefix.Depth; i++)
            {
                if (!Parts[i].Equals(prefix.Parts[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 检查当前路径是否以指定路径结尾
        /// </summary>
        /// <param name="suffix">后缀路径</param>
        /// <returns>如果当前路径以指定路径结尾则返回true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool EndsWith(PAPath suffix)
        {
            // 空路径或无效路径处理
            if (suffix.IsEmpty) return true;
            if (IsEmpty) return false;
            if (suffix.Depth > Depth) return false;

            // 从末尾开始逐个比较路径部分
            int startIndex = Depth - suffix.Depth;
            for (int i = 0; i < suffix.Depth; i++)
            {
                if (!Parts[startIndex + i].Equals(suffix.Parts[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 检查当前路径是否包含指定路径（作为连续的子路径）
        /// </summary>
        /// <param name="subPath">要检查的子路径</param>
        /// <returns>如果当前路径包含指定子路径则返回true</returns>
        public readonly bool Contains(PAPath subPath)
        {
            // 空路径或无效路径处理
            if (subPath.IsEmpty) return true;
            if (IsEmpty) return false;
            if (subPath.Depth > Depth) return false;

            // 在当前路径的所有可能位置查找子路径
            for (int startIndex = 0; startIndex <= Depth - subPath.Depth; startIndex++)
            {
                bool found = true;
                for (int i = 0; i < subPath.Depth; i++)
                {
                    if (!Parts[startIndex + i].Equals(subPath.Parts[i]))
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return true;
            }

            return false;
        }

        /// <summary>
        /// 检查当前路径是否以指定字符串路径开头
        /// </summary>
        /// <param name="prefix">前缀路径字符串</param>
        /// <returns>如果当前路径以指定路径开头则返回true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool StartsWith(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            return StartsWith(PAPath.Create(prefix));
        }

        /// <summary>
        /// 检查当前路径是否以指定字符串路径结尾
        /// </summary>
        /// <param name="suffix">后缀路径字符串</param>
        /// <returns>如果当前路径以指定路径结尾则返回true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool EndsWith(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return true;
            return EndsWith(PAPath.Create(suffix));
        }

        /// <summary>
        /// 检查当前路径是否包含指定字符串路径（作为连续的子路径）
        /// </summary>
        /// <param name="subPath">要检查的子路径字符串</param>
        /// <returns>如果当前路径包含指定子路径则返回true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Contains(string subPath)
        {
            if (string.IsNullOrEmpty(subPath)) return true;
            return Contains(PAPath.Create(subPath));
        }

        #endregion

        public readonly static PAPath Empty = new ();
        public readonly static PAPath Position =  new("Position");

    }

    /// <summary>
    /// 路径部分结构 - 表示路径中的单个部分（字段名或索引）
    /// </summary>
    public readonly struct PAPart : IEquatable<PAPart>
    {
        #region 字段

        public readonly int Index;
        public readonly string Name;

        #endregion

        #region 属性

        /// <summary>
        /// 是否为索引访问
        /// </summary>
        public readonly bool IsIndex => Name == null;
        public readonly bool Valid => !IsIndex|| Index >= 0;
        
        public static PAPart _ => new (-1);
        #endregion

        #region 构造函数

        /// <summary>
        /// 创建字段访问部分
        /// </summary>
        private PAPart(string name)
        {
            Name = name;
            Index = 0;
        }

        /// <summary>
        /// 创建索引访问部分
        /// </summary>
        private PAPart(int index)
        {
            Name = null;
            Index = index;
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 从字符串创建路径部分
        /// </summary>
        public static PAPart FromString(string name) => new PAPart(name);

        /// <summary>
        /// 从索引创建路径部分
        /// </summary>
        public static PAPart FromIndex(int index) => new PAPart(index);

        #endregion

        #region 相等性和哈希

        public readonly bool Equals(PAPart other)
        {
            return Index == other.Index && string.Equals(Name, other.Name);
        }

        public override readonly bool Equals(object obj)
        {
            return obj is PAPart other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Index, Name);
        }

        public static bool operator ==(PAPart left, PAPart right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PAPart left, PAPart right)
        {
            return !left.Equals(right);
        }

        #endregion

        #region ToString

        public override readonly string ToString() => 
            IsIndex ? $"[{Index}]" : $".{Name}";

        #endregion
    }
}
