using Newtonsoft.Json;
using System.Collections.Generic;
using TreeNode.Utility;
using Unity.Properties;

namespace TreeNode.Runtime
{
    [Icon]
    public class TemplateAsset : TreeNodeAsset
    {
        public string Name;
        public List<TemplateProperty> Properties = new();



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




    }
    public class TemplateProperty
    {
        public string ID;
        public string Path;
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
