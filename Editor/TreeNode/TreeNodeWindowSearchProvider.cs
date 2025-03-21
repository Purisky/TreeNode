using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.Progress;
namespace TreeNode.Editor
{
    public struct SearchContextElement
    {
        public Type Type;
        public string[] Title;
        public SearchContextElement(Type type, string title)
        {
            Type = type;
            Title = title.Split('/');
        }
    }

    public class TreeNodeWindowSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        public TreeNodeGraphView Graph;
        public VisualElement Target;
        public static List<SearchContextElement> Elements;

        static TreeNodeWindowSearchProvider()
        {
            Elements = new();
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(n => n.GetCustomAttribute<NodeInfoAttribute>() != null))
            {
                NodeInfoAttribute attribute = type.GetCustomAttribute<NodeInfoAttribute>();
                if (string.IsNullOrEmpty(attribute.MenuItem)) { continue; }
                Elements.Add(new(type, attribute.MenuItem));
                Elements.Sort((entry1, entry2) =>
                {
                    string[] strings1 = entry1.Title;
                    string[] strings2 = entry2.Title;
                    for (int i = 0; i < strings1.Length; i++)
                    {
                        if (strings2.Length <= i) { return 1; }
                        if (strings1[i] != strings2[i])
                        {
                            return strings1[i].CompareTo(strings2[i]);
                        }
                    }
                    return -1;
                });
            }
        }




        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new()
            {
                new SearchTreeGroupEntry(new GUIContent("Nodes"), 0)
            };
            List<string> groups = new();
            for (int i = 0; i < Elements.Count; i++)
            {
                SearchContextElement searchContextElement = Elements[i];
                if (IsNodeValidIn(searchContextElement.Type, Graph))
                {
                    string[] strings = searchContextElement.Title;
                    string groupName = "";
                    for (int j = 0; j < strings.Length - 1; j++)
                    {
                        groupName += strings[j];
                        if (!groups.Contains(groupName))
                        {
                            tree.Add(new SearchTreeGroupEntry(new GUIContent(strings[j]), j + 1));
                            groups.Add(groupName);
                        }
                        groupName += "/";
                    }
                    SearchTreeEntry entry = new(new GUIContent(strings[^1]))
                    {
                        level = strings.Length,
                        userData = searchContextElement
                    };
                    tree.Add(entry);
                }
            }
            if (Graph is not NodePrefabGraphView)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("NodePrefab"), 0));
                foreach (var item in NodePrefabManager.Previews)
                {
                    tree.Add(new SearchTreeEntry(new GUIContent(item.Value.Name))
                    {
                        level = 1,
                        userData = item.Value
                    });
                }
            }



            return tree;


        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            JsonNode node = SearchTreeEntry.userData switch
            {
                SearchContextElement sce => Activator.CreateInstance(sce.Type) as JsonNode,
                PrefabPreviewData ppd => ppd.CreateNode(),
                _ => null
            };
            if (node == null) { return false; }
            var windowPosition = Graph.ChangeCoordinatesTo(Graph, context.screenMousePosition - Graph.Window.position.position);
            var graphMousePosition = Graph.ViewContainer.WorldToLocal(windowPosition);
            node.Position = graphMousePosition;
            Graph.AddNode(node);
            return true;
        }

        public static bool IsNodeValidIn(Type nodeType, TreeNodeGraphView graph)
        {
            AssetFilterAttribute filter = nodeType.GetCustomAttribute<AssetFilterAttribute>();
            if (filter != null)
            {
                if (graph.Asset.Data is NodePrefabAsset && filter.BanPrefab) { return false; }
                if (filter.Allowed == !filter.Types.Contains(graph.Asset.Data.GetType())) { return false; }
            }
            NodeInfoAttribute attribute = nodeType.GetCustomAttribute<NodeInfoAttribute>();
            if (attribute.Unique && graph.NodeDic.Keys.Any(n => n.GetType() == nodeType)) { return false; }
            return true;
        }
    }
}
