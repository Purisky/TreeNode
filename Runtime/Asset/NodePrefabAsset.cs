using Newtonsoft.Json;
using System.Collections.Generic;
using Unity.Properties;

namespace TreeNode.Runtime
{
    [AssetIcon]
    public class NodePrefabAsset : TreeNodeAsset
    {
        public string Name;
        public List<PrefabProperty> Properties = new();



        public int Width;

        [JsonIgnore]
        public JsonNode RootNode
        {
            get
            {
                if (0 < Nodes.Count)
                {
                    return Nodes[0];
                }
                return null;
            }
        }
        public T GetValue<T>(in PropertyPath path)
        {
            return PropertyContainer.GetValue<List<JsonNode>, T>(Nodes, path.ToString());
        }
    }
    public class PrefabProperty
    {
        public string ID;
        public PropertyPath Path;
        public string Name;
        public string Type;
    }

    public struct NodeMap
    {
        public Node[] Nodes;
        public Vec2 Size;
        public struct Node
        {
            public Vec2 Pos;
            public Vec2 Size;
            public string Color;
            public int ParentIndex;
            public int ConnectionIndex;
        }
    }



}
