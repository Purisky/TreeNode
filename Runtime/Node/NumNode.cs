using Newtonsoft.Json;
using System;
using UnityEngine;

namespace TreeNode.Runtime
{
    [PortColor("#7CFC00")]
    public abstract class NumNode : JsonNode
    {
        public abstract string GetText(); 
    }
    public class NumValue
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
    }
}
