using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Runtime
{
    [PortColor("#40E0D0")]
    public abstract class Condition : JsonNode
    {
        public abstract string GetText();
    }
}