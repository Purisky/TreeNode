using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TreeNode.Runtime
{
    public interface IPropertyAccessor
    {
        T GetValueInternal<T>(ref PAPath path, ref int index);
        void SetValueInternal<T>(ref PAPath path, ref int index, T value);
        void RemoveValueInternal(ref PAPath path, ref int index);
        void ValidatePath(ref PAPath path, ref int index);
        //List<(int depth,T value)> GetAllInPath<T>(ref PAPath path, ref int index);
        //List<(PAPath, JsonNode)> CollectNodes(List<(PAPath, JsonNode)> list);
    }

    public static class PropertyAccessorExtensions
    {
        static ref PAPart ValidIndex(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart first = ref path.Parts[index++];
            if (!first.IsIndex) { throw new NotSupportedException($"Non-index access not supported by {list.GetType().Name}"); }
            if (first.Index < 0 || first.Index >= list.Count) { throw new IndexOutOfRangeException($"Index {first.Index} out of range for list of size {list.Count}"); }
            return ref first;
        }
        public static T GetValueInternal<T>(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            object element = list[first.Index];
            if (path.Parts.Length == index)
            {
                if (element is T value)
                {
                    return value;
                }
                throw new InvalidCastException($"Cannot cast element of type {element?.GetType().Name ?? "null"} to {typeof(T).Name}");
            }
            if (element is IPropertyAccessor accessor)
            {
                return accessor.GetValueInternal<T>(ref path,ref index);
            }
            return PropertyAccessor.GetValue<T>(element, ref path, ref index);
        }
        public static void SetValueInternalClass<T, TClass>(this List<TClass> list, ref PAPath path, ref int index, T value) where TClass : class
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (path.Parts.Length == index)
            {
                if (value is TClass classValue)
                {
                    list[first.Index] = classValue;
                    return;
                }
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(TClass).Name}");
            }
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(ref path, ref index, value);
                return;
            }
            PropertyAccessor.SetValue(list[first.Index], ref path, ref index, value);
        }
        public static void SetValueInternalStruct<T, TStruct>(this List<TStruct> list, ref PAPath path, ref int index, T value) where TStruct : struct
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (path.Parts.Length == index)
            {
                if (value is TStruct structValue)
                {
                    list[first.Index] = structValue;
                    return;
                }
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(TStruct).Name}");
            }
            TStruct @struct = list[first.Index];
            if (@struct is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(ref path, ref index, value);
                list[first.Index] = (TStruct)accessor;
                return;
            }
            PropertyAccessor.SetValue(@struct, ref path, ref index, value);
            list[first.Index] = @struct;
        }
        public static void RemoveValueInternalClass<TClass>(this List<TClass> list, ref PAPath path, ref int index) where TClass : class
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (path.Parts.Length == index)
            {
                list.RemoveAt(first.Index);
                return;
            }
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.RemoveValueInternal(ref path, ref index);
                return;
            }
            PropertyAccessor.RemoveValue(list[first.Index], ref path, ref index);
        }
        public static void RemoveValueInternalStruct<TStruct>(this List<TStruct> list, ref PAPath path, ref int index) where TStruct : struct
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (path.Parts.Length == index)
            {
                list.RemoveAt(first.Index);
                return;
            }
            TStruct @struct = list[first.Index];
            if (@struct is IPropertyAccessor accessor)
            {
                accessor.RemoveValueInternal(ref path, ref index);
                list[first.Index] = (TStruct)accessor; 
            }
            PropertyAccessor.RemoveValue(@struct, ref path, ref index);
            list[first.Index] = @struct; 
        }

        //递归向下查找,直到找不到合法的路径对象
        public static void ValidatePath(this IList list, ref PAPath path, ref int index)
        {
            if (index >= path.Parts.Length)
            {
                return;
            }
            
            ref PAPart part = ref path.Parts[index];
            if (!part.IsIndex || part.Index < 0 || part.Index >= list.Count)
            {
                return;
            }
            
            index++;
            
            object element = list[part.Index];
            if (element is IPropertyAccessor accessor)
            {
                accessor.ValidatePath(ref path, ref index);
            }
            else if (element != null)
            {
                PropertyAccessor.ValidatePath(element, ref path, ref index);
            }
        }



    }





}
