using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
namespace TreeNode.Editor
{
    public struct SearchContextElement
    {
        public object Target { get; private set; }
        public string Title { get; private set; }
        public SearchContextElement(object target, string title)
        {
            Target = target;
            Title = title;
        }


    }



    public class TreeNodeWindowSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        public TreeNodeGraphView Graph;
        public VisualElement Target;
        public static List<SearchContextElement> Elements;



        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new()
            {
                new SearchTreeGroupEntry(new GUIContent("Nodes"), 0)
            };
            Elements = new();

            Type assetType = Graph.Asset.Data.GetType();
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .Where(n => n.GetCustomAttribute<NodeInfoAttribute>() != null))
            {
                AssetFilterAttribute filter = type.GetCustomAttribute<AssetFilterAttribute>();
                if (filter != null)
                {
                    if (Graph is NodePrefabGraphView && filter.BanPrefab) { continue; }
                    if (filter.Allowed == !filter.Types.Contains(assetType)) { continue; }
                    
                }
                NodeInfoAttribute attribute = type.GetCustomAttribute<NodeInfoAttribute>();
                if (attribute.Unique && Graph.Asset.Data.Nodes.Any(n => n.GetType() == type)) { continue; }
                object node = Activator.CreateInstance(type);
                if (string.IsNullOrEmpty(attribute.MenuItem)) { continue; }
                Elements.Add(new(node, attribute.MenuItem));
            }

            if (Graph is not NodePrefabGraphView)
            {
                foreach (var item in NodePrefabManager.Previews)
                {
                    JsonNode node = item.Value.CreateNode();
                    Elements.Add(new(node, $"{"NodePrefab"}/{item.Value.Name}"));
                }
            }




            Elements.Sort((entry1, entry2) =>
                {
                    string[] strings1 = entry1.Title.Split('/');
                    string[] strings2 = entry2.Title.Split('/');
                    for (int i = 0; i < strings1.Length; i++)
                    {
                        if (strings2.Length <= i) { return 1; }
                        if (strings1[i] != strings2[i])
                        {
                            return strings1[i].CompareTo(strings2[i]);
                        }
                    }
                    return -1;
                }
                );
            List<string> groups = new();
            foreach (var item in Elements)
            {
                string[] strings = item.Title.Split('/');
                string groupName = "";
                for (int i = 0; i < strings.Length-1; i++)
                {

                    groupName += strings[i];
                    if (!groups.Contains(groupName))
                    {
                        tree.Add(new SearchTreeGroupEntry(new GUIContent(strings[i]), i + 1));
                        groups.Add(groupName);
                    }
                    groupName += "/";

                }
                SearchTreeEntry entry = new(new GUIContent(strings[^1]))
                {
                    level = strings.Length,
                    userData = new SearchContextElement(item.Target, item.Title)
                };
                tree.Add(entry);

            }
            return tree;


        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            var windowPosition = Graph.ChangeCoordinatesTo(Graph, context.screenMousePosition - Graph.Window.position.position);
            var graphMousePosition = Graph.ViewContainer.WorldToLocal(windowPosition);
            SearchContextElement element = (SearchContextElement)SearchTreeEntry.userData;

            JsonNode node = (JsonNode)element.Target;
            node.Position = graphMousePosition;
            Graph.AddNode(node);
            return true;
        }


    }
}
