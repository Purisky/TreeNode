using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TreeNode.Runtime
{
    public static class JsonNodeHelper
    {
        public static List<JsonNode> GetAllJsonNodes(JsonNode rootNode)
        {
            List<JsonNode> jsonNodes = new List<JsonNode>();
            GetAllJsonNodesRecursive(rootNode, jsonNodes);
            return jsonNodes;
        }

        private static void GetAllJsonNodesRecursive(JsonNode currentNode, List<JsonNode> jsonNodes)
        {
            if (currentNode == null)
                return;

            jsonNodes.Add(currentNode);
            var properties = currentNode.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (typeof(JsonNode).IsAssignableFrom(property.PropertyType))
                {
                    var childNode = property.GetValue(currentNode) as JsonNode;
                    GetAllJsonNodesRecursive(childNode, jsonNodes);
                }
                else if (typeof(IEnumerable<JsonNode>).IsAssignableFrom(property.PropertyType))
                {
                    var childNodes = property.GetValue(currentNode) as IEnumerable<JsonNode>;
                    if (childNodes != null)
                    {
                        foreach (var childNode in childNodes)
                        {
                            GetAllJsonNodesRecursive(childNode, jsonNodes);
                        }
                    }
                }
            }
            var fields = currentNode.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (typeof(JsonNode).IsAssignableFrom(field.FieldType))
                {
                    var childNode = field.GetValue(currentNode) as JsonNode;
                    GetAllJsonNodesRecursive(childNode, jsonNodes);
                }
                else if (typeof(IEnumerable<JsonNode>).IsAssignableFrom(field.FieldType))
                {
                    var childNodes = field.GetValue(currentNode) as IEnumerable<JsonNode>;
                    if (childNodes != null)
                    {
                        foreach (var childNode in childNodes)
                        {
                            GetAllJsonNodesRecursive(childNode, jsonNodes);
                        }
                    }
                }
            }
        }
    }
}
