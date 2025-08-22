using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Graphs;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = TreeNode.Utility.Debug;
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
        
        // 类型过滤相关
        private Type filterType = null;
        private bool isFiltering = false;
        
        // 拖拽连接相关
        private ChildPort pendingConnectionPort = null;
        
        // 纹理缓存，避免重复创建相同颜色的圆环纹理
        private static readonly Dictionary<Color, Texture2D> colorTextureCache = new Dictionary<Color, Texture2D>();
        
        /// <summary>
        /// 清理纹理缓存，释放内存
        /// </summary>
        public static void ClearTextureCache()
        {
            foreach (var texture in colorTextureCache.Values)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
            }
            colorTextureCache.Clear();
        }

        static TreeNodeWindowSearchProvider()
        {
            Elements = new();
            
            // 使用 TypeCacheSystem 获取所有带有 NodeInfo 的类型
            var nodeInfoTypes = TypeCacheSystem.GetTypesWithNodeInfo();
            
            foreach (var type in nodeInfoTypes)
            {
                var typeInfo = TypeCacheSystem.GetTypeInfo(type);
                NodeInfoAttribute attribute = typeInfo.NodeInfo;
                if (attribute == null || string.IsNullOrEmpty(attribute.MenuItem)) { continue; }
                Elements.Add(new(type, attribute.MenuItem));
            }
            
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
            
            // 监听编辑器应用关闭事件，清理纹理缓存
            UnityEditor.EditorApplication.quitting += ClearTextureCache;
        }

        /// <summary>
        /// 设置类型过滤和待连接端口
        /// </summary>
        public void SetTypeFilter(Type portType, ChildPort sourcePort = null)
        {
            filterType = portType;
            isFiltering = portType != null;
            pendingConnectionPort = sourcePort;
        }

        /// <summary>
        /// 清除类型过滤和待连接端口
        /// </summary>
        public void ClearTypeFilter()
        {
            filterType = null;
            isFiltering = false;
            pendingConnectionPort = null;
        }

        /// <summary>
        /// 获取节点类型的颜色，复用Port的颜色逻辑
        /// </summary>
        private static Color GetNodeColor(Type nodeType)
        {
            var typeInfo = TypeCacheSystem.GetTypeInfo(nodeType);
            
            // 首先尝试从NodeInfo的Type字段获取颜色
            NodeInfoAttribute nodeInfo = typeInfo.NodeInfo;
            if (nodeInfo?.Type != null)
            {
                var targetTypeInfo = TypeCacheSystem.GetTypeInfo(nodeInfo.Type);
                PortColorAttribute portColorAtt = targetTypeInfo.PortColor;
                if (portColorAtt != null)
                {
                    //Debug.Log($"Found color from NodeInfo.Type for {nodeType.Name}: {portColorAtt.Color}");
                    return portColorAtt.Color;
                }
            }
            
            // 如果NodeInfo.Type没有颜色，直接从节点类型获取
            PortColorAttribute directPortColor = typeInfo.PortColor;
            if (directPortColor != null)
            {
                //Debug.Log($"Found direct color for {nodeType.Name}: {directPortColor.Color}");
                return directPortColor.Color;
            }
            
            // 默认颜色
            //Debug.Log($"Using default color for {nodeType.Name}");
            return Color.white;
        }

        /// <summary>
        /// 创建分段圆环纹理，根据颜色统计显示不同比例的颜色段
        /// </summary>
        private static Texture2D CreateSegmentedColorTexture(Dictionary<Color, int> colorCounts)
        {
            if (colorCounts == null || colorCounts.Count == 0)
            {
                return CreateColorTexture(Color.white);
            }
            
            // 量化颜色键以确保缓存一致性
            var quantizedColorCounts = new Dictionary<Color, int>();
            foreach (var kvp in colorCounts)
            {
                Color quantizedColor = new Color(
                    Mathf.Round(kvp.Key.r * 255f) / 255f,
                    Mathf.Round(kvp.Key.g * 255f) / 255f,
                    Mathf.Round(kvp.Key.b * 255f) / 255f,
                    Mathf.Round(kvp.Key.a * 255f) / 255f
                );
                
                if (quantizedColorCounts.ContainsKey(quantizedColor))
                {
                    quantizedColorCounts[quantizedColor] += kvp.Value;
                }
                else
                {
                    quantizedColorCounts[quantizedColor] = kvp.Value;
                }
            }
            
            // 如果只有一种颜色，直接返回单色圆环
            if (quantizedColorCounts.Count == 1)
            {
                return CreateColorTexture(quantizedColorCounts.Keys.First());
            }
            
            int size = 16;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];
            
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size * 0.4f;
            float innerRadius = size * 0.2f;
            
            // 计算总数量和角度分配
            int totalCount = quantizedColorCounts.Values.Sum();
            var colorSegments = new List<(Color color, float startAngle, float endAngle)>();
            
            float currentAngle = 0f;
            foreach (var kvp in quantizedColorCounts.OrderByDescending(x => x.Value))
            {
                float proportion = (float)kvp.Value / totalCount;
                float segmentAngle = proportion * 360f;
                colorSegments.Add((kvp.Key, currentAngle, currentAngle + segmentAngle));
                currentAngle += segmentAngle;
            }
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float distance = Vector2.Distance(pos, center);
                    
                    if (distance <= outerRadius && distance >= innerRadius)
                    {
                        // 计算当前像素的角度
                        Vector2 direction = (pos - center).normalized;
                        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                        if (angle < 0) angle += 360f; // 转换到0-360度范围
                        
                        // 找到对应的颜色段
                        Color pixelColor = Color.white;
                        foreach (var segment in colorSegments)
                        {
                            if (angle >= segment.startAngle && angle < segment.endAngle)
                            {
                                pixelColor = segment.color;
                                break;
                            }
                        }
                        
                        // 添加抗锯齿效果
                        float alpha = 1f;
                        if (distance > outerRadius - 0.7f)
                        {
                            alpha = 1f - Mathf.SmoothStep(outerRadius - 0.7f, outerRadius, distance);
                        }
                        else if (distance < innerRadius + 0.7f)
                        {
                            alpha = 1f - Mathf.SmoothStep(innerRadius, innerRadius + 0.7f, innerRadius + 0.7f - distance);
                        }
                        
                        pixelColor.a = Mathf.Clamp01(alpha);
                        colors[y * size + x] = pixelColor;
                    }
                    else
                    {
                        colors[y * size + x] = Color.clear;
                    }
                }
            }
            
            texture.SetPixels(colors);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;
            texture.filterMode = FilterMode.Bilinear;
            
            return texture;
        }
        private static Texture2D CreateColorTexture(Color color)
        {
            //Debug.Log($"Creating texture for color: {color}");
            
            // 将颜色量化到合理精度，避免浮点精度问题导致缓存失效
            Color quantizedColor = new Color(
                Mathf.Round(color.r * 255f) / 255f,
                Mathf.Round(color.g * 255f) / 255f,
                Mathf.Round(color.b * 255f) / 255f,
                Mathf.Round(color.a * 255f) / 255f
            );
            
            //Debug.Log($"Quantized color: {quantizedColor}");
            
            // 使用缓存避免重复创建相同颜色的纹理
            if (colorTextureCache.TryGetValue(quantizedColor, out Texture2D cachedTexture) && cachedTexture != null)
            {
                //Debug.Log($"Using cached texture for color: {quantizedColor}");
                return cachedTexture;
            }
            
            int size = 16; // 稍微增大尺寸
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];
            
            //Debug.Log($"Creating new texture of size {size}x{size}");
            
            Vector2 center = new Vector2(size / 2f, size / 2f); // 简化中心点计算
            float outerRadius = size * 0.4f; // 外圆半径更大一些
            float innerRadius = size * 0.2f; // 内圆半径更小，使圆环更宽
            
            //Debug.Log($"Center: {center}, OuterRadius: {outerRadius}, InnerRadius: {innerRadius}");
            
            int pixelsInRing = 0; // 统计圆环内的像素数量
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float distance = Vector2.Distance(pos, center);
                    
                    if (distance <= outerRadius && distance >= innerRadius)
                    {
                        pixelsInRing++;
                        
                        // 简化抗锯齿逻辑
                        float alpha = 1f;
                        
                        // 外边缘渐变（更简单的逻辑）
                        if (distance > outerRadius - 1f)
                        {
                            alpha = (outerRadius - distance);
                        }
                        // 内边缘渐变
                        else if (distance < innerRadius + 1f)
                        {
                            alpha = (distance - innerRadius);
                        }
                        
                        alpha = Mathf.Clamp01(alpha);
                        
                        Color pixelColor = quantizedColor;
                        pixelColor.a = alpha;
                        colors[y * size + x] = pixelColor;
                    }
                    else
                    {
                        // 圆环外或内部，使用透明色
                        colors[y * size + x] = Color.clear;
                    }
                }
            }
            
            //Debug.Log($"Pixels in ring: {pixelsInRing}");
            
            texture.SetPixels(colors);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave; // 避免保存到场景
            texture.filterMode = FilterMode.Bilinear; // 使用双线性过滤获得更平滑的效果
            
            //Debug.Log($"Texture created and applied successfully");
            
            // 缓存纹理
            colorTextureCache[quantizedColor] = texture;
            //Debug.Log($"Texture cached for color: {quantizedColor}");
            
            return texture;
        }




        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new()
            {
                new SearchTreeGroupEntry(new GUIContent(isFiltering ? $"Compatible Nodes ({filterType?.Name})" : "Nodes"), 0)
            };
            
            // 如果是从端口拖拽筛选，使用平面显示
            if (isFiltering)
            {
                for (int i = 0; i < Elements.Count; i++)
                {
                    SearchContextElement searchContextElement = Elements[i];
                    if (IsNodeValidWithFilter(searchContextElement.Type))
                    {
                        // 使用完整路径作为显示名称
                        string fullPath = string.Join("/", searchContextElement.Title);
                        Color nodeColor = GetNodeColor(searchContextElement.Type);
                        
                        GUIContent content = new(fullPath)
                        {
                            image = CreateColorTexture(nodeColor)
                        };
                        
                        SearchTreeEntry entry = new(content)
                        {
                            level = 1, // 所有条目都在同一层级
                            userData = searchContextElement
                        };
                        tree.Add(entry);
                    }
                }
                return tree;
            }
            
            // 非筛选状态，使用原来的树状显示
            // 用于收集每个组的颜色统计
            Dictionary<string, Dictionary<Color, int>> groupColorStats = new Dictionary<string, Dictionary<Color, int>>();
            List<string> groups = new();
            
            // 第一遍遍历：收集颜色统计信息
            for (int i = 0; i < Elements.Count; i++)
            {
                SearchContextElement searchContextElement = Elements[i];
                if (IsNodeValidWithFilter(searchContextElement.Type))
                {
                    string[] strings = searchContextElement.Title;
                    Color nodeColor = GetNodeColor(searchContextElement.Type);
                    
                    // 为每个层级的组收集颜色统计
                    string groupPath = "";
                    for (int j = 0; j < strings.Length - 1; j++)
                    {
                        groupPath += strings[j];
                        
                        if (!groupColorStats.ContainsKey(groupPath))
                        {
                            groupColorStats[groupPath] = new Dictionary<Color, int>();
                        }
                        
                        if (groupColorStats[groupPath].ContainsKey(nodeColor))
                        {
                            groupColorStats[groupPath][nodeColor]++;
                        }
                        else
                        {
                            groupColorStats[groupPath][nodeColor] = 1;
                        }
                        
                        groupPath += "/";
                    }
                }
            }
            
            // 第二遍遍历：创建树结构
            for (int i = 0; i < Elements.Count; i++)
            {
                SearchContextElement searchContextElement = Elements[i];
                if (IsNodeValidWithFilter(searchContextElement.Type))
                {
                    string[] strings = searchContextElement.Title;
                    string groupName = "";
                    for (int j = 0; j < strings.Length - 1; j++)
                    {
                        groupName += strings[j];
                        if (!groups.Contains(groupName))
                        {
                            // 为组条目创建分段圆环图标
                            Texture2D groupIcon = null;
                            if (groupColorStats.ContainsKey(groupName))
                            {
                                groupIcon = CreateSegmentedColorTexture(groupColorStats[groupName]);
                            }
                            
                            GUIContent groupContent = new GUIContent(strings[j]);
                            if (groupIcon != null)
                            {
                                groupContent.image = groupIcon;
                            }
                            
                            tree.Add(new SearchTreeGroupEntry(groupContent, j + 1));
                            groups.Add(groupName);
                        }
                        groupName += "/";
                    }
                    
                    // 获取节点类型的颜色
                    Color nodeColor = GetNodeColor(searchContextElement.Type);
                    GUIContent content = new(strings[^1])
                    {
                        // 使用颜色作为图标或标识
                        image = CreateColorTexture(nodeColor)
                    };
                    
                    SearchTreeEntry entry = new(content)
                    {
                        level = strings.Length,
                        userData = searchContextElement
                    };
                    tree.Add(entry);
                }
            }
            return tree;
#pragma warning disable CS0162 // 检测到无法访问的代码
            if (Graph is not TemplateGraphView)//todo not ready
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("Template"), 1));
                foreach (var item in TemplateManager.Previews)
                {
                    Debug.Log(item.Value.Name);
                    tree.Add(new SearchTreeEntry(new GUIContent($"{item.Value.Name}"))
                    {
                        level = 2,
                        userData = item.Value
                    });
                }
            }
#pragma warning restore CS0162 // 检测到无法访问的代码
            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            JsonNode node = SearchTreeEntry.userData switch
            {
                SearchContextElement sce => Activator.CreateInstance(sce.Type) as JsonNode,
                TemplatePreviewData ppd => ppd.CreateNode(),
                _ => null
            };
            if (node == null) { return false; }
            
            var windowPosition = Graph.ChangeCoordinatesTo(Graph, context.screenMousePosition - Graph.Window.position.position);
            var graphMousePosition = Graph.ViewContainer.WorldToLocal(windowPosition);
            node.Position = graphMousePosition;
            
            // 开始批量操作
            Graph.Window.History.BeginBatch();
            
            try
            {
                JsonNode originalConnectedNode = null;
                PAPath originalNodePath = PAPath.Empty;

                // 如果有待连接的端口，处理原连接
                if (pendingConnectionPort != null)
                {
                    // 如果是单连接端口且已有连接，先处理原节点
                    if (pendingConnectionPort.capacity == Port.Capacity.Single && pendingConnectionPort.connected)
                    {
                        var existingEdges = pendingConnectionPort.connections.ToList();
                        if (existingEdges.Count > 0)
                        {
                            var existingEdge = existingEdges[0];
                            var originalViewNode = existingEdge.ParentPort().node;
                            originalConnectedNode = originalViewNode.Data;

                            // 获取原节点的当前路径
                            originalNodePath = originalViewNode.GetNodePath();

                            // 1. 先将原节点移动至根节点
                            PAPath newRootPath = PAPath.Index(Graph.Asset.Data.Nodes.Count);
                            Graph.Asset.Data.Nodes.Add(originalConnectedNode);
                            int index = 0;
                            Graph.Asset.Data.Nodes.RemoveValueInternal(ref originalNodePath, ref index);
                            var moveOperation = NodeOperation.Move(originalConnectedNode, originalNodePath, newRootPath, Graph.Asset);
                            Graph.Window.History.Record(moveOperation);
                            // 断开现有连接
                            Graph.RemoveElement(existingEdge);
                            pendingConnectionPort.Disconnect(existingEdge);
                            existingEdge.ParentPort().Disconnect(existingEdge);
                            pendingConnectionPort.OnRemoveEdge(existingEdge);
                        }
                    }
                    
                }
                // 如果有待连接的端口，创建连接
                if (pendingConnectionPort != null)
                {
                    Graph.SetNodeByPath(node, pendingConnectionPort.node.GetNodePath().Combine(pendingConnectionPort.LocalPath));
                    ViewNode newViewNode =  Graph.AddViewNode(node);
                    var edge = pendingConnectionPort.ConnectTo(newViewNode.ParentPort);
                    if (edge != null)
                    {
                        Graph.AddElement(edge);
                        pendingConnectionPort.Connect(edge);
                        newViewNode.ParentPort.Connect(edge);
                        pendingConnectionPort.OnAddEdge(edge);
                    }
                }
                else
                {
                    Graph.AddNode(node);
                    Graph.AddViewNode(node);
                }





            }
            finally
            {
                // 结束批量操作
                Graph.Window.History.EndBatch();
                
                // 清除类型过滤和待连接端口
                ClearTypeFilter();
            }
            
            return true;
        }

        public static bool IsNodeValidIn(Type nodeType, TreeNodeGraphView graph)
        {
            var typeInfo = TypeCacheSystem.GetTypeInfo(nodeType);
            
            AssetFilterAttribute filter = typeInfo.AssetFilter;
            if (filter != null)
            {
                if (graph.Asset.Data is TemplateAsset && filter.BanTemplate) { return false; }
                if (filter.Allowed == !filter.Types.Contains(graph.Asset.Data.GetType())) { return false; }
            }
            NodeInfoAttribute attribute = typeInfo.NodeInfo;
            if (attribute != null && attribute.Unique && graph.NodeDic.Keys.Any(n => n.GetType() == nodeType)) { return false; }
            return true;
        }

        /// <summary>
        /// 检查节点是否在当前过滤条件下有效
        /// </summary>
        public bool IsNodeValidWithFilter(Type nodeType)
        {
            // 首先检查基本的有效性
            if (!IsNodeValidIn(nodeType, Graph)) { return false; }
            
            // 如果启用了类型过滤
            if (isFiltering && filterType != null)
            {
                // 检查节点类型是否与端口类型兼容
                return IsTypeCompatible(nodeType, filterType);
            }
            
            return true;
        }

        /// <summary>
        /// 检查节点类型是否与端口类型兼容
        /// </summary>
        private bool IsTypeCompatible(Type nodeType, Type portType)
        {
            // 如果节点类型就是端口类型或其子类
            if (portType.IsAssignableFrom(nodeType))
            {
                return true;
            }
            
            // 如果节点类型是端口类型的父类
            if (nodeType.IsAssignableFrom(portType))
            {
                return true;
            }
            
            // TODO: 这里可以添加更复杂的兼容性检查逻辑
            // 比如检查接口实现、泛型类型等
            
            return false;
        }
    }
}
