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
        public static T GetValue<T>(object obj, string path)
        {
            return GetValue<T>(obj, PAPath.Create(path));
        }
        public static T GetValue<T>(object obj, PAPart part)
        {
            if (!part.Valid) { return (T)obj; }
            Type type = obj.GetType();
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            var memberInfo = typeInfo.GetMember(part.Name);
            return (T)memberInfo.Getter(obj);
        }




        public static T GetValue<T>(object obj, PAPath path)
        {
            if (path.IsEmpty)
                return (T)obj;

            if (path.Depth == 1)
            {
                return GetValue<T>(obj, path.FirstPart);
            }
            // 多层路径访问
            var parent = GetParentObject(obj, path, out var lastPart);
            var multiGetter = GetOrCreateGetter<T>(parent.GetType(), lastPart);
            return multiGetter(parent);
        }

        public static void SetValue<T>(object obj, PAPart part, T value)
        {
            if (obj.GetType().IsValueType)
            {
                throw new InvalidOperationException("无法修改值类型的根对象");
            }
            var setter = GetOrCreateSetter<T>(obj.GetType(), part);
            setter(obj, value);
            return;
        }
        public static void SetValue<T>(object obj, string path, T value)
        {
            SetValue(obj, PAPath.Create(path), value);
        }
        public static void SetValue<T>(object obj, PAPath path, T value)
        {
            if (path.IsEmpty)
                throw new ArgumentException("路径不能为空");

            if (path.Depth == 1)
            {
                SetValue(obj, path.FirstPart, value);
                return;
            }

            // 多层路径设置
            var parent = GetParentObject(obj, path, out var lastPart);

            // 处理值类型的特殊情况
            if (parent.GetType().IsValueType)
            {
                HandleValueTypeSet(obj, path, lastPart, value);
            }
            else
            {
                var setter = GetOrCreateSetter<T>(parent.GetType(), lastPart);
                setter(parent, value);
            }
        }

        public static void RemoveValue(object obj, string path)
        {
            RemoveValue(obj, PAPath.Create(path));
        }
        public static void RemoveValue(object obj, PAPath path)
        {
            var parent = GetParentObject(obj, path, out var lastPart);

            if (parent is IList list && lastPart.IsIndex)
            {
                list.RemoveAt(lastPart.Index);
            }
            else
            {
                var setter = GetOrCreateSetter<object>(parent.GetType(), lastPart);
                setter(parent, null);
            }
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
            return GetLast<T>(obj, PAPath.Create(path), includeEnd, out index);
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
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), path.Parts[i]);
                currentObj = getter(currentObj);

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
                var singleGetter = GetOrCreateGetter<T>(obj.GetType(), first);
                return singleGetter(obj); 
            }
            index++;
            object nextObj = GetOrCreateGetter<object>(obj.GetType(), first)(obj);
            
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
            Debug.Log(memberInfo.Name);
            if (index == path.Parts.Length - 1)
            {
                var singleSetter = CacheManager.GetOrCreateSetter<T>(type, first);
                Debug.Log($"singleSetter {singleSetter} ( {obj} ,{value} )");
                singleSetter(obj,value);
                return;
            }
            index++;
            object nextObj = GetOrCreateGetter<object>(type, first)(obj);
            
            // 使用统一的多层链路处理引擎
            ProcessMultiLevel_SetValue<T>(nextObj, ref path, ref index, value);
            
            // 处理值类型的特殊情况
            if (nextObj is IPropertyAccessor accessor && 
                memberInfo.ValueType.IsValueType && 
                memberInfo.MemberType == TypeCacheSystem.MemberType.Property)
            {
                var singleSetter = CacheManager.GetOrCreateSetter<object>(type, first);
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
                var singleSetter = CacheManager.GetOrCreateSetter<object>(type, first);
                singleSetter(obj, default);
                return;
            }
            index++;
            object nextObj = GetOrCreateGetter<object>(type, first)(obj);
            
            // 使用统一的多层链路处理引擎
            ProcessMultiLevel_RemoveValue(nextObj, ref path, ref index);
            
            // 处理值类型的特殊情况
            if (nextObj is IPropertyAccessor accessor && 
                memberInfo.ValueType.IsValueType && 
                memberInfo.MemberType == TypeCacheSystem.MemberType.Property)
            {
                var singleSetter = CacheManager.GetOrCreateSetter<object>(type, first);
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
                
                var nextObj = memberInfo.Getter(obj);
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
    }
}