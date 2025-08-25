using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TreeNode.Utility;
using static TreeNode.Runtime.TypeCacheSystem;
using static TreeNode.Runtime.MultiLevelNavigationEngine;
using IndexOutOfRangeException = TreeNode.Runtime.Property.Exceptions.IndexOutOfRangeException;

namespace TreeNode.Runtime
{
    public interface IPropertyAccessor
    {
        T GetValueInternal<T>(ref PAPath path, ref int index);
        void SetValueInternal<T>(ref PAPath path, ref int index, T value);
        void RemoveValueInternal(ref PAPath path, ref int index);
        /// <summary>
        /// 判断路径是否存在,末端可能为空
        /// </summary>
        /// <param name="path"></param>
        /// <param name="index"></param>
        void ValidatePath(ref PAPath path, ref int index);
        void GetAllInPath<T>(ref PAPath path, ref int index, List<(int depth, T value)> list) where T : class;
        void CollectNodes(List<(PAPath, JsonNode)> list, PAPath parent, int depth = -1);
        TypeReflectionInfo TypeInfo { get; }
    }

    public static class PropertyAccessorExtensions
    {
        static ref PAPart ValidIndex(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart first = ref path.Parts[index];
            if (!first.IsIndex) { index--; throw new NotSupportedException($"Non-index access not supported by {list.GetType().Name}"); }
            if (first.Index < 0 || first.Index >= list.Count)
            {
                index--;
                var remainingPath = index + 2 < path.Parts.Length ? $" -> {path.GetSubPath(index + 2)}" : "";
                throw new ArgumentException($"路径对象为空：{path.GetSubPath(0, index + 1)} -> {path.Parts[index + 1]}(null){remainingPath}");
                //throw new IndexOutOfRangeException(path, list.GetType(), first.Index, list.Count);
            }
            return ref first;
        }
        public static T GetValueInternal<T>(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            object element = list[first.Index];
            if (index == path.Parts.Length - 1)
            {
                if (element is T value)
                {
                    return value;
                }
                index--;
                throw new InvalidCastException($"Cannot cast element of type {element?.GetType().Name ?? "null"} to {typeof(T).Name}");
            }
            index++;
            return ProcessMultiLevel_GetValue<T>(element, ref path, ref index);
        }
        public static void SetValueInternal<T>(this IList list, ref PAPath path, ref int index, T value)
        {
            Debug.Log($"List.SetValueInternal:{path} ref {index} value:{value}");
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (index == path.Parts.Length - 1)
            {
                if (value is T structValue)
                {
                    list[first.Index] = structValue;
                    return;
                }
                index--;
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(T).Name}");
            }
            index++;
            object item = list[first.Index];
            ProcessMultiLevel_SetValue(item, ref path, ref index, value);
            list[first.Index] = item;
        }
        public static void RemoveValueInternal(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (index == path.Parts.Length - 1)
            {
                list.RemoveAt(first.Index);
                return;
            }
            index++;
            object item = list[first.Index];
            ProcessMultiLevel_RemoveValue(item, ref path, ref index);
            list[first.Index] = item;
        }
        public static void ValidatePath(this IList list, ref PAPath path, ref int index)
        {
            //Debug.Log($"List.ValidatePath:{path} ref {index}");
            ref PAPart part = ref path.Parts[index];
            if (!part.IsIndex) { index--; return; }
            if (part.Index < 0) { index--; return; }
            if (index == path.Parts.Length - 1)
            {
                if (part.Index > list.Count) { index--;return; }
                return;
            }
            if (part.Index >= list.Count) {
                
                index--; //throw new IndexOutOfRangeException(path, list.GetType(), part.Index, list.Count);
                return;
            }
            object element = list[part.Index];
            index++;
            ProcessMultiLevel_ValidatePath(element, ref path, ref index);
        }
        public static void GetAllInPath<T>(this IList list, ref PAPath path, ref int index, List<(int depth, T value)> listValues) where T : class
        {
            ref PAPart part = ref list.ValidIndex(ref path, ref index);
            object element = list[part.Index];
            if (element is T value) { listValues.Add((index, value)); }
            if (index == path.Parts.Length - 1) { return; }
            index++;
            ProcessMultiLevel_GetAllInPath(element, ref path, ref index, listValues);
        }

        public static void CollectNodes(this IList list, List<(PAPath, JsonNode)> listNodes, PAPath parent, int depth = -1)
        {
            if (depth == 0) { return; }
            for (int i = 0; i < list.Count; i++)
            {
                int depth_ = depth;
                PAPath next = parent.Append(i);
                if (list[i] is JsonNode node)
                {
                    listNodes.Add((next, node));
                    if (depth_ > 0) { depth_--; }
                }
                ProcessMultiLevel_CollectNodes(list[i], listNodes, next, depth_);
            }
        }
    }





}
