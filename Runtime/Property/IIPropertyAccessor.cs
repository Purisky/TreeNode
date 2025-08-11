using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Linq;
using TreeNode.Runtime.Property.Exceptions;
using IndexOutOfRangeException = TreeNode.Runtime.Property.Exceptions.IndexOutOfRangeException;

namespace TreeNode.Runtime
{
    public interface IPropertyAccessor
    {
        T GetValueInternal<T>(ref PAPath path, ref int index);
        void SetValueInternal<T>(ref PAPath path, ref int index, T value);
        void RemoveValueInternal(ref PAPath path, ref int index);
        void ValidatePath(ref PAPath path, ref int index);
        void GetAllInPath<T>(ref PAPath path, ref int index, List<(int depth, T value)> list);
        //List<(PAPath, JsonNode)> CollectNodes(List<(PAPath, JsonNode)> list,int depth = -1);
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
            else if (element is IList || element is Array) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            return PropertyAccessor.GetValue<T>(element, ref path, ref index);
        }
        public static void SetValueInternalClass<T, TClass>(this List<TClass> list, ref PAPath path, ref int index, T value) where TClass : class
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (index == path.Parts.Length - 1)
            {
                if (value is TClass classValue)
                {
                    list[first.Index] = classValue;
                    return;
                }
                index--;
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(TClass).Name}");
            }
            index++;
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(ref path, ref index, value);
                return;
            }
            else if (list[first.Index] is IList || list[first.Index] is Array) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            PropertyAccessor.SetValue(list[first.Index], ref path, ref index, value);
        }
        public static void SetValueInternalStruct<T, TStruct>(this List<TStruct> list, ref PAPath path, ref int index, T value) where TStruct : struct
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (index == path.Parts.Length - 1)
            {
                if (value is TStruct structValue)
                {
                    list[first.Index] = structValue;
                    return;
                }
                index--;
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(TStruct).Name}");
            }
            index++;
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
            if (index == path.Parts.Length - 1)
            {
                list.RemoveAt(first.Index);
                return;
            }
            index++;
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.RemoveValueInternal(ref path, ref index);
                return;
            }
            else if (list[first.Index] is IList || list[first.Index] is Array) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            PropertyAccessor.RemoveValue(list[first.Index], ref path, ref index);
        }
        public static void RemoveValueInternalStruct<TStruct>(this List<TStruct> list, ref PAPath path, ref int index) where TStruct : struct
        {
            ref PAPart first = ref list.ValidIndex(ref path, ref index);
            if (index == path.Parts.Length - 1)
            {
                list.RemoveAt(first.Index);
                return;
            }
            index++;
            TStruct @struct = list[first.Index];
            if (@struct is IPropertyAccessor accessor)
            {
                accessor.RemoveValueInternal(ref path, ref index);
                list[first.Index] = (TStruct)accessor;
            }

            PropertyAccessor.RemoveValue(@struct, ref path, ref index);
            list[first.Index] = @struct;
        }
        public static void ValidatePath(this IList list, ref PAPath path, ref int index)
        {
            ref PAPart part = ref list.ValidIndex(ref path, ref index);
            object element = list[part.Index];
            if (index == path.Parts.Length - 1) { return; }
            index++;
            if (element is IPropertyAccessor accessor) { accessor.ValidatePath(ref path, ref index); }
            else if (element is IList || element is Array) { throw new NestedCollectionException(path.GetSubPath(0, index), list.GetType()); }
            else if (element != null) { PropertyAccessor.ValidatePath(element, ref path, ref index); }
        }

        public static void GetAllInPath<T>(this IList list, ref PAPath path, ref int index, List<(int depth, T value)> listValues)
        {
            ref PAPart part = ref list.ValidIndex(ref path, ref index);
            object element = list[part.Index];
            if (element is T value) { listValues.Add((index, value)); }
            if (index == path.Parts.Length - 1) { return; }
            index++;
            if (element is IPropertyAccessor accessor) { accessor.GetAllInPath(ref path, ref index, listValues); }
            else if (element is IList || element is Array) { throw new NestedCollectionException(path.GetSubPath(0,index), list.GetType()); }
            else { PropertyAccessor.GetAllInPath<T>(element, ref path, ref index, listValues); }
        }


    }





}
