using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEngine;

namespace TreeNode.Utility
{
    public class DropdownItem
    {
        public string Text;
        public string FullText;
    }
    public class DropdownItem<T> : DropdownItem
    {
        readonly T value;
        readonly bool isValueType;
        public readonly string JsonText;


        public T Value => value;
        public DropdownItem(string text, T v)
        {
            FullText = text;
            Text = FullText.Split('/').Last();
            value = v;
            isValueType = typeof(T).IsValueType || typeof(T) == typeof(string);
            if (!isValueType)
            {
                JsonText = Json.ToJson(v);
            }
        }
        public bool ValueEquals(T v)
        {
            if (isValueType)
            {
                return value.Equals(v);
            }
            else
            {
                if (Value.Equals(v)) { return true; }
                return Json.ToJson(v).Equals(JsonText);
            }
        }


    }
}
