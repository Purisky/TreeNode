using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TreeNode.Runtime
{
    public interface IPropertyAccessor
    {
        T GetValueInternal<T>(PAPath path);
        bool TryGetValueInternal<T>(ref PAPath path,int index, out T value);
        void SetValueInternal<T>(PAPath path, T value);

        //void RemoveValue(PAPath path);
        //bool ValidatePath(PAPath path, out int validDepth);
        //(int depth,T value) GetAllInPath<T>(PAPath path);
        //List<(PAPath, JsonNode)> CollectNodes();


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

 
        public static bool TryGetValueInternal<T>(this IList list, ref PAPath path,int index, out T value)
        {
            value = default;
            ref PAPart first =ref path.Parts[index];
            if (!first.IsIndex || first.Index < 0 || first.Index >= list.Count) { return false; }
            object element = list[first.Index];
            if (path.Parts.Length == index + 1)
            {
                if (element is T castValue)
                {
                    value = castValue;
                    return true;
                }
                return false;
            }
            if (element is IPropertyAccessor accessor)
            {
                return accessor.TryGetValueInternal<T>(ref path, index + 1, out value);
            }
            return PropertyAccessor.TryGetValue<T>(element, path.SkipFirst, out value);
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

        public static bool TrySetValueInternalClass<T, TClass>(this List<TClass> list, PAPath path, T value) where TClass : class
        {
            PAPart first = path.FirstPart;
            if (!first.IsIndex || first.Index < 0 || first.Index >= list.Count) { return false; }
            if (path.Parts.Length == 1)
            {
                if (value is TClass classValue)
                {
                    list[first.Index] = classValue;
                    return true;
                }
                if (value == null)
                {
                    list.RemoveAt(first.Index);
                    return true;
                }
                return false;
            }
            if (list[first.Index] is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(path.SkipFirst, value);
                return true;
            }
            try
            {
                PropertyAccessor.SetValue(list[first.Index], path.SkipFirst, value);
                return true;
            }
            catch
            {
                return false;
            }
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
        public static bool TrySetValueInternalStruct<T, TStruct>(this List<TStruct> list, PAPath path, T value) where TStruct : struct
        {
            PAPart first = path.FirstPart;
            if (!first.IsIndex || first.Index < 0 || first.Index >= list.Count) { return false; }
            if (path.Parts.Length == 1)
            {
                if (value is TStruct structValue)
                {
                    list[first.Index] = structValue;
                    return true;
                }
                return false;
            }
            TStruct @struct = list[first.Index];
            if (@struct is IPropertyAccessor accessor)
            {
                accessor.SetValueInternal(path.SkipFirst, value);
                list[first.Index] = (TStruct)accessor;
                return true;
            }
            try
            {
                PropertyAccessor.SetValue(@struct, path.SkipFirst, value);
                list[first.Index] = @struct;
                return true;
            }
            catch
            {
                return false;
            }
        }

    }


}
