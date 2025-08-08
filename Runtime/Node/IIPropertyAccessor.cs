using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TreeNode.Runtime
{
    public interface IPropertyAccessor
    {
        T GetValueInternal<T>(PAPath path);
        void SetValueInternal<T>(PAPath path, T value);
    }

    public static class PropertyAccessorExtensions
    {
        public static T GetValueInternal<T>(this IList list, PAPath path)
        {
            PAPart first = path.FirstPart;
            if (!first.IsIndex) { throw new NotSupportedException($"Non-index access not supported by {list.GetType().Name}"); }
            if (first.Index < 0 || first.Index >= list.Count) { throw new IndexOutOfRangeException($"Index {first.Index} out of range for list of size {list.Count}"); }
            object element = list[first.Index];
            if (path.Parts.Length == 1)
            {
                if (element is T value)
                {
                    return value;
                }
                throw new InvalidCastException($"Cannot cast element of type {element?.GetType().Name ?? "null"} to {typeof(T).Name}");
            }
            if (element is IPropertyAccessor accessor)
            {
                return accessor.GetValueInternal<T>(path.SkipFirst);
            }
            return PropertyAccessor.GetValue<T>(element, path.SkipFirst);
        }
        public static void SetValueInternalClass<T,TClass>(this List<TClass> list, PAPath path, T value) where TClass:class
        {
            PAPart first = path.FirstPart;
            if (!first.IsIndex) { throw new NotSupportedException($"Non-index access not supported by {list.GetType().Name}"); }
            if (first.Index < 0 || first.Index >= list.Count) { throw new IndexOutOfRangeException($"Index {first.Index} out of range for list of size {list.Count}"); }
            if (path.Parts.Length == 1)
            {
                if (value is TClass classValue)
                {
                    list[first.Index] = classValue;
                    return;
                }
                if (value == null)
                {
                    list.RemoveAt(first.Index);
                    return;
                }
                throw new InvalidCastException($"Cannot cast value of type {value?.GetType().Name ?? "null"} to {typeof(TClass).Name}");
            }
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(path.SkipFirst, value);
                return;
            }


            PropertyAccessor.SetValue(list[first.Index], path.SkipFirst, value);
        }
        public static void SetValueInternalStruct<T, TStruct>(this List<TStruct> list, PAPath path, T value) where TStruct : struct
        {
            PAPart first = path.FirstPart;
            if (!first.IsIndex) { throw new NotSupportedException($"Non-index access not supported by {list.GetType().Name}"); }
            if (first.Index < 0 || first.Index >= list.Count) { throw new IndexOutOfRangeException($"Index {first.Index} out of range for list of size {list.Count}"); }
            if (path.Parts.Length == 1)
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
                accessor.SetValueInternal(path.SkipFirst, value);
                list[first.Index] = (TStruct)accessor;
                return;
            }
            PropertyAccessor.SetValue(@struct, path.SkipFirst, value);
            list[first.Index] = @struct;
        }


    }


}
