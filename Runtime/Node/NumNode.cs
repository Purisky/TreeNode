using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Runtime.CompilerServices;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    [PortColor("#7CFC00")]
    public abstract class NumNode : JsonNode, IText
    {
        public abstract string GetText();




    }
    public class NumValue<T> : NumValue where T : NumNode
    {
    }



    public abstract class NumValue : IText, IPropertyAccessor
    {
        public float Value;
        public NumNode Node;


        public string GetText()
        {
            if (Node != null)
            {
                return Node.GetText();
            }
            return Value.ToString();
        }


        public T GetValueInternal<T>(PAPath path)
        {
            PAPart first = path.FirstPart;
            if (first.IsIndex) { throw new NotSupportedException($"Index access not supported by {GetType().Name}"); }
            if (path.Parts.Length == 1)
            {
                switch (first.Name)
                {
                    case "Value":
                        if (Value is T value)
                        {
                            return value;
                        }
                        throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");
                    case "Node":
                        if (Node is T nodeValue)
                        {
                            return nodeValue;
                        }
                        if (Node == null)
                        {
                            return default;
                        }
                        throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");
                }
            }
            if (Node != null&&first.Name == "Node")
            { 
                return Node.GetValueInternal<T>(path.SkipFirst);
            }
            throw new NotSupportedException($"Path '{path}' is not supported by {GetType().Name}");
        }
        public void SetValueInternal<T>(PAPath path, T value)
        {
            PAPart first = path.FirstPart;
            if (first.IsIndex) { throw new NotSupportedException($"Index access not supported by {GetType().Name}"); }
            if (path.Parts.Length == 1)
            {
                switch (first.Name)
                {
                    case "Value":
                        if (value is float fValue)
                        {
                            Value = fValue;
                            return;
                        }
                        throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");
                    case "Node":
                        if (value is null)
                        {
                            Node = null;
                            return;
                        }
                        if (value is NumValue nodeValue)
                        {
                            Node = nodeValue.Node;
                            return;
                        }
                        throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");
                }
            }
            if (Node != null && first.Name == "Node")
            {
                 Node.SetValueInternal<T>(path.SkipFirst,value);
            }
            throw new NotSupportedException($"Path '{path}' is not supported by {GetType().Name}");
        }
    }
}
