using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using TreeNode.Runtime.Property.Exceptions;
using TreeNode.Utility;
using UnityEngine;
using static TreeNode.Runtime.MultiLevelNavigationEngine;
using Debug = UnityEngine.Debug;
using IndexOutOfRangeException = TreeNode.Runtime.Property.Exceptions.IndexOutOfRangeException;

namespace TreeNode.Runtime
{
    public static partial class PropertyAccessor//公共API - 保持向后兼容
    {
        public static T GetValue<T>(object obj, PAPart part)
        {
            if (!part.Valid) { return (T)obj; }
            
            var paPath = new PAPath(part);
            int index = 0;
            return GetValueInternal<T>(obj, ref paPath, ref index);
        }
        public static T GetValue<T>(object obj, PAPath path)
        {
            if (path.IsEmpty)
                return (T)obj;

            int index = 0;
            return GetValueInternal<T>(obj, ref path, ref index);
        }

        public static void SetValue<T>(object obj, PAPart part, T value)
        {
            if (obj.GetType().IsValueType)
            {
                throw new InvalidOperationException("无法修改值类型的根对象");
            }
            
            var paPath = new PAPath(part);
            int index = 0;
            SetValueInternal<T>(obj, ref paPath, ref index, value);
        }
        public static void SetValue<T>(object obj, PAPath path, T value)
        {
            if (path.IsEmpty)
                throw new ArgumentException("路径不能为空");

            int index = 0;
            SetValueInternal<T>(obj, ref path, ref index, value);
        }

        public static void RemoveValue(object obj, PAPath path)
        {
            int index = 0;
            RemoveValueInternal(obj, ref path, ref index);
        }

        public static bool GetValidPath(object obj, string path, out int validLength)
        {
            obj.ThrowIfNull(nameof(obj));
            path.ThrowIfNullOrEmpty(nameof(path));

            var paPath = PAPath.Create(path);
            bool isValid = GetValidPath(obj, paPath, out int validDepth);

            // 将深度转换为字符串长度
            validLength = ConvertDepthToStringLength(paPath, validDepth);

            return isValid;
        }
        public static bool GetValidPath(object obj, PAPath path, out int validDepth)
        {
            obj.ThrowIfNull(nameof(obj));

            var currentObj = obj;

            if (path.IsEmpty)
            {
                validDepth = 0;
                return true;
            }

            for (int i = 0; i < path.Depth; i++)
            {
                var part = path.Parts[i];

                try
                {
                    if (i == path.Depth - 1)
                    {
                        // 最后一个部分，验证成员存在性
                        if (ValidationStrategy.ValidateMemberExists(currentObj, part))
                        {
                            validDepth = path.Depth;
                            return true;
                        }
                        else
                        {
                            validDepth = i;
                            return false;
                        }
                    }
                    else
                    {
                        // 中间部分，尝试获取下一个对象
                        currentObj = NavigationStrategy.NavigateToNext(obj, currentObj, path, i);
                        if (currentObj == null)
                        {
                            validDepth = i;
                            return false;
                        }
                        validDepth = i + 1;
                    }
                }
                catch
                {
                    validDepth = i;
                    return false;
                }
            }

            validDepth = path.Depth;
            return true;
        }
        public static T GetLast<T>(object obj, string path, bool includeEnd, out int index)
        {
            var paPath = PAPath.Create(path);
            return GetLast<T>(obj, paPath, includeEnd, out index);
        }
        public static T GetLast<T>(object obj, PAPath path, bool includeEnd, out int index)
        {
            obj.ThrowIfNull(nameof(obj));

            var currentObj = obj;
            T result = default;
            index = 0;

            int endIndex = includeEnd ? path.Depth : path.Depth - 1;

            for (int i = 0; i < endIndex; i++)
            {
                var part = path.Parts[i];
                var tempPath = new PAPath(part);
                int tempIndex = 0;
                currentObj = GetValueInternal<object>(currentObj, ref tempPath, ref tempIndex);

                if (currentObj is T typedObj)
                {
                    result = typedObj;
                    index = i + 1;
                }
            }

            return result;
        }
        public static object GetParentObject(object obj, PAPath path, out PAPart lastPart)
        {
            obj.ThrowIfNull(nameof(obj));

            if (path.Depth <= 1)
            {
                lastPart = path.FirstPart;
                return obj;
            }

            lastPart = path.LastPart;
            var parentPath = path.GetParent();

            return GetValue<object>(obj, parentPath);
        }
        public static string ExtractParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            int lastDot = path.LastIndexOf('.');
            int lastBracket = path.LastIndexOf('[');

            if (lastDot == -1 && lastBracket == -1)
                return null;

            return path[..Math.Max(lastDot, lastBracket)];
        }


        public static T GetValueInternal<T>(object obj, ref PAPath path, ref int index)
        {
            ref PAPart first = ref path.Parts[index];
            if (index == path.Parts.Length - 1)
            {
                // 单级访问 - 也支持实例创建
                object result = GetOrCreateNextObject(obj, first);
                return (T)result;
            }
            index++;
            
            // 多级访问 - 使用统一的获取或创建方法
            object nextObj = GetOrCreateNextObject(obj, first);
            
            // 使用统一的多层链路处理引擎
            return ProcessMultiLevel_GetValue<T>(nextObj, ref path, ref index);
        }
        public static void SetValueInternal<T>(object obj, ref PAPath path, ref int index, T value)
        {
            Debug.Log($"SetValueInternal<{typeof(T).Name}>({obj}({obj.GetType().Name}),{path},{value})");
            ref PAPart first = ref path.Parts[index];
            Type type = obj.GetType();
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            Debug.Log(typeInfo.Type.Name);
            var memberInfo = typeInfo.GetMember(first.Name);
            Debug.Log(memberInfo?.Name ?? "null");
            
            if (index == path.Parts.Length - 1)
            {
                // 单级设置 - 使用统一的访问器设置策略
                var singleSetter = GetUnifiedSetter<T>(type, first);
                Debug.Log($"singleSetter {singleSetter} ( {obj} ,{value} )");
                singleSetter(obj, value);
                return;
            }
            
            index++;
            // 多级设置 - 使用统一的获取或创建方法
            object nextObj = GetOrCreateNextObject(obj, first);
            
            // 使用统一的多层链路处理引擎
            ProcessMultiLevel_SetValue<T>(nextObj, ref path, ref index, value);
            
            // 处理值类型的特殊情况
            if (nextObj is IPropertyAccessor accessor && 
                memberInfo?.ValueType.IsValueType == true && 
                memberInfo?.MemberType == TypeCacheSystem.MemberType.Property)
            {
                var singleSetter = GetUnifiedSetter<object>(type, first);
                singleSetter(obj, accessor);
            }
        }
        public static void RemoveValueInternal(object obj, ref PAPath path, ref int index)
        {
            ref PAPart first = ref path.Parts[index];
            Type type = obj.GetType();
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(first.Name);
            
            if (index == path.Parts.Length - 1)
            {
                // 单级移除 - 使用统一的访问器设置策略
                var singleSetter = GetUnifiedSetter<object>(type, first);
                singleSetter(obj, default);
                return;
            }
            
            index++;
            // 多级移除 - 使用统一的获取或创建方法
            // 注意：对于Remove操作，通常不需要创建实例，但为了路径一致性可能需要
            object nextObj = GetOrCreateNextObject(obj, first);
            
            // 使用统一的多层链路处理引擎
            ProcessMultiLevel_RemoveValue(nextObj, ref path, ref index);
            
            // 处理值类型的特殊情况
            if (nextObj is IPropertyAccessor accessor && 
                memberInfo?.ValueType.IsValueType == true && 
                memberInfo?.MemberType == TypeCacheSystem.MemberType.Property)
            {
                var singleSetter = GetUnifiedSetter<object>(type, first);
                singleSetter(obj, accessor);
            }
        }
        public static void ValidatePath(object obj, ref PAPath path, ref int index)
        {
            try
            {
                Type type = obj.GetType();
                var typeInfo = TypeCacheSystem.GetTypeInfo(type);
                var memberInfo = typeInfo.GetMember(path.Parts[index].Name);
                if (index == path.Parts.Length - 1)
                {
                    if (memberInfo == null)
                    {
                        index--;
                    }
                    return;
                }
                
                // 使用统一的获取或创建方法
                // 注意：在ValidatePath中，我们不实际设置实例，只是用于验证路径的可达性
                object nextObj = GetOrCreateNextObject(obj, path.Parts[index]);
                if (nextObj == null) { return; }
                index++;
                
                // 使用统一的多层链路处理引擎
                ProcessMultiLevel_ValidatePath(nextObj, ref path, ref index);
            }
            catch
            {
                index--;
            }
        }
        public static void GetAllInPath<T>(object obj, ref PAPath path, ref int index, List<(int depth, T value)> list) where T : class
        {
            try
            {
                var nextObj = NavigationStrategy.NavigateToNext(obj, obj, path, index);
                if (nextObj == null) { index--; return; }
                if (nextObj is T value) { list.Add((index, value)); }
                if (index == path.Parts.Length - 1) { return; }
                index++;
                
                // 使用统一的多层链路处理引擎
                ProcessMultiLevel_GetAllInPath<T>(nextObj, ref path, ref index, list);
            }
            catch
            {
                index--;
            }
        }
        public static void CollectNodes(object obj, List<(PAPath, JsonNode)> listNodes, PAPath parent, int depth = -1)
        {
            if (depth == 0) { return; }
            var type = obj.GetType();
            
            // 使用TypeCacheSystem获取缓存的类型信息
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            
            // 遍历可能包含嵌套JsonNode的成员（性能优化）
            foreach (var memberInfo in typeInfo.GetNestedJsonNodeCandidateMembers())
            {
                try
                {
                    int depth_ = depth;
                    // 使用预编译的Getter委托获取成员值，性能更高
                    var value = memberInfo.Getter(obj);
                    
                    if (value == null)
                    {
                        continue;
                    }
                    
                    var memberPath = parent.Append(memberInfo.Name);
                    
                    // 优先处理JsonNode类型成员
                    if (memberInfo.Category == TypeCacheSystem.MemberCategory.JsonNode && value is JsonNode jsonNode)
                    {
                        listNodes.Add((memberPath, jsonNode));
                        if (depth_ > 0) { depth_--; }
                    }
                    
                    // 处理特殊接口类型 - 使用统一的多层链路处理引擎
                    ProcessMultiLevel_CollectNodes(value, listNodes, memberPath, depth_);
                }
                catch
                {
                    // 跳过无法访问的成员
                    continue;
                }
            }
        }

        #region 统一访问器获取策略

        /// <summary>
        /// 统一的Getter获取策略 - 优先使用TypeCacheSystem，IList索引访问使用扩展方法，其他回退到CacheManager
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Func<object, T> GetUnifiedGetter<T>(Type type, PAPart part)
        {
            // 对于索引访问，优先检查是否为IList类型
            if (part.IsIndex)
            {
                // IList类型使用扩展方法（性能更优，零编译时间）
                //if (typeof(IList).IsAssignableFrom(type))
                //{
                //    return obj =>
                //    {
                //        var list = (IList)obj;
                //        var path = new PAPath(part);
                //        int index = 0;
                //        return list.GetValueInternal<T>(ref path, ref index);
                //    };
                //}
                //else
                {
                    // 其他索引器类型使用CacheManager（支持自定义索引器）
                    return CacheManager.GetOrCreateGetter<T>(type, part);
                }
            }

            // 对于成员访问，优先使用TypeCacheSystem的预编译委托
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(part.Name);
            
            if (memberInfo?.Getter != null)
            {
                // 使用预编译委托（性能最优）
                return obj =>
                {
                    var value = memberInfo.Getter(obj);
                    return (T)value;
                };
            }
            else
            {
                // 回退到CacheManager（用于动态创建访问器）
                return CacheManager.GetOrCreateGetter<T>(type, part);
            }
        }

        /// <summary>
        /// 统一的Setter获取策略 - 优先使用TypeCacheSystem，IList索引访问使用扩展方法，其他回退到CacheManager
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Action<object, T> GetUnifiedSetter<T>(Type type, PAPart part)
        {
            // 对于索引访问，优先检查是否为IList类型
            if (part.IsIndex)
            {
                //// IList类型使用扩展方法（性能更优，零编译时间）
                //if (typeof(IList).IsAssignableFrom(type))
                //{
                //    return (obj, value) =>
                //    {
                //        var list = (IList)obj;
                //        var path = new PAPath(part);
                //        int index = 0;
                //        list.SetValueInternal<T>(ref path, ref index, value);
                //    };
                //}
                //else
                {
                    // 其他索引器类型使用CacheManager（支持自定义索引器）
                    return CacheManager.GetOrCreateSetter<T>(type, part);
                }
            }

            // 对于成员访问，优先使用TypeCacheSystem的预编译委托
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(part.Name);
            
            if (memberInfo?.Setter != null)
            {
                // 使用预编译委托（性能最优）
                return (obj, value) => memberInfo.Setter(obj, value);
            }
            else
            {
                // 回退到CacheManager（用于动态创建访问器）
                return CacheManager.GetOrCreateSetter<T>(type, part);
            }
        }

        #endregion

        #region 实例创建支持

        /// <summary>
        /// 判断一个成员是否需要创建实例
        /// 条件：不是值类型 && 不是JsonNode类型 && 有无参构造函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedCreateInstance(Type memberType)
        {
            if (memberType == null || memberType.IsValueType)
                return false;

            // 检查是否为JsonNode类型
            if (IsJsonNodeType(memberType))
                return false;

            // 使用 TypeCacheSystem 检查是否有无参构造函数
            var typeInfo = TypeCacheSystem.GetTypeInfo(memberType);
            return typeInfo.HasParameterlessConstructor;
        }

        /// <summary>
        /// 创建实例（如果需要且可能）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object CreateInstanceIfNeeded(Type memberType)
        {
            if (!NeedCreateInstance(memberType))
                return null;

            var typeInfo = TypeCacheSystem.GetTypeInfo(memberType);
            return typeInfo.Constructor?.Invoke();
        }

        /// <summary>
        /// 获取或创建下一个对象
        /// 如果对象为null且需要创建实例，则创建并设置回原对象
        /// </summary>
        /// <param name="currentObj">当前对象</param>
        /// <param name="part">路径部分</param>
        /// <param name="shouldCreate">是否应该创建实例（用于区分读取和写入操作）</param>
        /// <returns>下一个对象（可能是现有的或新创建的）</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object GetOrCreateNextObject(object currentObj, PAPart part)
        {
            // 使用统一的访问器获取策略
            var nextGetter = GetUnifiedGetter<object>(currentObj.GetType(), part);
            object nextObj = nextGetter(currentObj);
            
            // 实例创建支持：如果下一个对象为null且需要创建实例，则创建并设置
            if (nextObj == null)
            {
                var type = currentObj.GetType();
                var typeInfo = TypeCacheSystem.GetTypeInfo(type);
                var memberInfo = typeInfo.GetMember(part.Name);
                
                if (memberInfo != null && NeedCreateInstance(memberInfo.ValueType))
                {
                    nextObj = CreateInstanceIfNeeded(memberInfo.ValueType);
                    if (nextObj != null)
                    {
                        // 设置创建的实例回原对象
                        var singleSetter = GetUnifiedSetter<object>(type, part);
                        singleSetter(currentObj, nextObj);
                    }
                }
            }
            
            return nextObj;
        }

        #endregion
    }
}