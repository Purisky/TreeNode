using Newtonsoft.Json;
using System.Collections.Generic;
using TreeNode.Utility;
using Unity.Properties;

namespace TreeNode.Runtime
{
    [Icon]
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
        //public T GetValue<T>(in PropertyPath path)
        //{
        //    return PropertyContainer.GetValue<List<JsonNode>, T>(Nodes, path.ToString());
        //}
        public T GetValue<T>(string path)
        {
           return PropertyAccessor.GetValue< T>(Nodes, path);
            //int index = int.Parse(path[1..path.IndexOf(']')]);
            //JsonNode node = Nodes[index];
            //return PropertyContainer.GetValue<JsonNode,T>(node, path[(path.IndexOf(']') + 2)..]);
        }

        public JsonNode this[int index] => Nodes[index];



    }
    public class PrefabProperty
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
