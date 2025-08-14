using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using TreeNode.Runtime.Property.Exceptions;
using static TreeNode.Runtime.TypeCacheSystem;
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
            if (first.Index < 0 || first.Index >= list.Count) { index--; throw new IndexOutOfRangeException(path, list.GetType(), first.Index, list.Count); }
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
            if (element is IPropertyAccessor accessor)
            {
                return accessor.GetValueInternal<T>(ref path, ref index);
            }
            else if (element is ICollection) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            return PropertyAccessor.GetValueInternal<T>(element, ref path, ref index);
        }
        public static void SetValueInternal<T>(this IList list, ref PAPath path, ref int index, T value)
        {
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
            if (item is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(ref path, ref index, value);
                list[first.Index] = accessor;
                return;
            }
            PropertyAccessor.SetValueInternal(item, ref path, ref index, value);
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
            if (item is IPropertyAccessor accessor)
            {
                accessor.RemoveValueInternal(ref path, ref index);
                list[first.Index] = accessor;
                return;
            }
            PropertyAccessor.RemoveValueInternal(item, ref path, ref index);
            list[first.Index] = item;
        }
        public static void ValidatePath(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart part = ref list.ValidIndex(ref path, ref index);
            object element = list[part.Index];
            if (index == path.Parts.Length - 1) { return; }
            index++;
            if (element is IPropertyAccessor accessor) { accessor.ValidatePath(ref path, ref index); }
            else if (element is ICollection) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            else if (element != null) { PropertyAccessor.ValidatePath(element, ref path, ref index); }
        }
        public static void GetAllInPath<T>(this IList list, ref PAPath path, ref int index, List<(int depth, T value)> listValues) where T : class
        {
            ref PAPart part = ref list.ValidIndex(ref path, ref index);
            object element = list[part.Index];
            if (element is T value) { listValues.Add((index, value)); }
            if (index == path.Parts.Length - 1) { return; }
            index++;
            if (element is IPropertyAccessor accessor) { accessor.GetAllInPath(ref path, ref index, listValues); }
            else if (element is ICollection) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            else { PropertyAccessor.GetAllInPath<T>(element, ref path, ref index, listValues); }
        }

        public static void CollectNodes(this IList list, List<(PAPath, JsonNode)> listNodes, PAPath parent, int depth = -1)
        {

            if (depth == 0) { return; }
            if (depth > 0) { depth--; }
            for (int i = 0; i < list.Count; i++)
            {
                PAPath next = parent.Append(i);
                if (list[i] is JsonNode node)
                {
                    listNodes.Add((next, node));
                }
                if (list[i] is IPropertyAccessor accessor) { accessor.CollectNodes(listNodes, next, depth); }
                else if (list[i] is ICollection) { throw new NestedCollectionException(parent, list.GetType()); }
                else { PropertyAccessor.CollectNodes(list[i], listNodes, next, depth); }
            }
        }
    }





}
