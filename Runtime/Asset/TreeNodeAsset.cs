using System;
using System.Collections.Generic;
namespace TreeNode.Runtime
{
    [Serializable]
    public abstract class TreeNodeAsset
    {
        public List<JsonNode> Nodes = new();


        public T GetValue<T>(string path)
        {
            return PropertyAccessor.GetValue<T>(Nodes, path);
        }
    }

}
