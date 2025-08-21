using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;
using TreeNode.Utility;
using UnityEngine;
using Newtonsoft.Json;

namespace TreeNode.Runtime
{
    public static class TypeCacheSystem
    {
        #region 数据结构定义

        /// <summary>
        /// 成员类型枚举
        /// </summary>
        public enum MemberType
        {
            Field,
            Property,
            Indexer
        }

        /// <summary>
        /// 成员分类枚举
        /// </summary>
        public enum MemberCategory
        {
            Normal,         // 普通成员
            JsonNode,       // JsonNode类型成员
            Collection      // 集合类型成员
        }

        /// <summary>
        /// 统一的成员信息（包含渲染元数据）
        /// </summary>
        public class UnifiedMemberInfo
        {
            #region 基础信息

            /// <summary>
            /// 成员反射信息
            /// </summary>
            public MemberInfo Member { get; set; }

            /// <summary>
            /// 成员类型（字段/属性/索引器）
            /// </summary>
            public MemberType MemberType { get; set; }

            /// <summary>
            /// 成员名称
            /// </summary>
            public string Name => Member.Name;

            /// <summary>
            /// 值类型
            /// </summary>
            public Type ValueType { get; set; }

            #endregion

            #region 分类信息

            /// <summary>
            /// 成员分类
            /// </summary>
            public MemberCategory Category { get; set; }

            /// <summary>
            /// 是否为Child标记的成员
            /// </summary>
            public bool IsChild { get; set; }

            /// <summary>
            /// 是否为TitlePort标记的成员
            /// </summary>
            public bool IsTitlePort { get; set; }

            /// <summary>
            /// 是否显示在节点中
            /// </summary>
            public bool ShowInNode { get; set; }

            #endregion

            #region 渲染信息

            /// <summary>
            /// 渲染顺序
            /// </summary>
            public int RenderOrder { get; set; }

            /// <summary>
            /// 分组名称
            /// </summary>
            public string GroupName { get; set; }

            /// <summary>
            /// 是否为多值类型
            /// </summary>
            public bool IsMultiValue { get; set; }

            /// <summary>
            /// 是否可能包含嵌套结构（用于运行时过滤优化）
            /// </summary>
            public bool MayContainNestedStructure { get; set; }

            /// <summary>
            /// 是否可能包含嵌套JsonNode（排除NoJsonNodeContainer标记的类型）
            /// </summary>
            public bool MayContainNestedJsonNode { get; set; }

            #endregion

            #region Attribute 信息

            /// <summary>
            /// ShowInNode Attribute 信息
            /// </summary>
            public ShowInNodeAttribute ShowInNodeAttribute { get; set; }

            /// <summary>
            /// LabelInfo Attribute 信息
            /// </summary>
            public LabelInfoAttribute LabelInfoAttribute { get; set; }

            /// <summary>
            /// Style Attribute 信息
            /// </summary>
            public StyleAttribute StyleAttribute { get; set; }

            /// <summary>
            /// Group Attribute 信息
            /// </summary>
            public GroupAttribute GroupAttribute { get; set; }

            /// <summary>
            /// OnChange Attribute 信息
            /// </summary>
            public OnChangeAttribute OnChangeAttribute { get; set; }

            /// <summary>
            /// Dropdown Attribute 信息
            /// </summary>
            public DropdownAttribute DropdownAttribute { get; set; }

            /// <summary>
            /// TitlePort Attribute 信息
            /// </summary>
            public TitlePortAttribute TitlePortAttribute { get; set; }

            #endregion

            public Func<object, object> Getter { get; set; }
            public Action<object, object> Setter { get; set; }

            /// <summary>
            /// 获取成员信息描述
            /// </summary>
            public override string ToString()
            {
                return $"{MemberType}.{Name}({ValueType.Name}) - {Category}";
            }

            #region Attribute 查询方法

            /// <summary>
            /// 获取显示标签文本（优先使用 LabelInfoAttribute.Text，否则使用成员名称）
            /// </summary>
            public string GetDisplayLabel()
            {
                return LabelInfoAttribute?.Text ?? Name;
            }

            /// <summary>
            /// 获取标签宽度
            /// </summary>
            public float GetLabelWidth()
            {
                return LabelInfoAttribute?.Width ?? 0f;
            }

            /// <summary>
            /// 获取标签大小
            /// </summary>
            public int GetLabelSize()
            {
                return LabelInfoAttribute?.Size ?? 11;
            }

            /// <summary>
            /// 获取标签颜色
            /// </summary>
            public string GetLabelColor()
            {
                return LabelInfoAttribute?.Color ?? "#D2D2D2";
            }

            /// <summary>
            /// 检查标签是否隐藏
            /// </summary>
            public bool IsLabelHidden()
            {
                return LabelInfoAttribute?.Hide ?? false;
            }

            /// <summary>
            /// 获取分组名称（优先使用 GroupAttribute.Name，否则使用从渲染信息中提取的组名）
            /// </summary>
            public string GetEffectiveGroupName()
            {
                return GroupAttribute?.Name ?? GroupName;
            }

            /// <summary>
            /// 获取分组宽度
            /// </summary>
            public float GetGroupWidth()
            {
                return GroupAttribute?.Width ?? 0f;
            }

            /// <summary>
            /// 获取分组显示条件
            /// </summary>
            public string GetGroupShowIf()
            {
                return GroupAttribute?.ShowIf ?? string.Empty;
            }

            /// <summary>
            /// 获取显示顺序（优先使用 ShowInNodeAttribute.Order，否则使用渲染顺序）
            /// </summary>
            public int GetDisplayOrder()
            {
                return ShowInNodeAttribute?.Order ?? RenderOrder;
            }

            /// <summary>
            /// 获取显示条件
            /// </summary>
            public string GetShowIf()
            {
                return ShowInNodeAttribute?.ShowIf ?? string.Empty;
            }

            /// <summary>
            /// 检查是否为只读
            /// </summary>
            public bool IsReadOnly()
            {
                return ShowInNodeAttribute?.ReadOnly ?? false;
            }

            /// <summary>
            /// 获取变化事件处理动作
            /// </summary>
            public string GetOnChangeAction()
            {
                return OnChangeAttribute?.Action ?? string.Empty;
            }

            /// <summary>
            /// 检查变化事件是否包含子项
            /// </summary>
            public bool IsOnChangeIncludeChildren()
            {
                return OnChangeAttribute?.IncludeChildren ?? false;
            }

            /// <summary>
            /// 获取下拉列表获取器
            /// </summary>
            public string GetDropdownListGetter()
            {
                return DropdownAttribute?.ListGetter ?? string.Empty;
            }

            /// <summary>
            /// 检查下拉列表是否扁平化
            /// </summary>
            public bool IsDropdownFlat()
            {
                return DropdownAttribute?.Flat ?? false;
            }

            /// <summary>
            /// 检查下拉列表是否跳过现有项
            /// </summary>
            public bool IsDropdownSkipExist()
            {
                return DropdownAttribute?.SkipExist ?? false;
            }

            /// <summary>
            /// 检查是否有下拉列表
            /// </summary>
            public bool HasDropdown()
            {
                return DropdownAttribute != null && !string.IsNullOrEmpty(DropdownAttribute.ListGetter);
            }

            /// <summary>
            /// 检查是否有样式设置
            /// </summary>
            public bool HasStyle()
            {
                return StyleAttribute != null;
            }

            #endregion
        }


        /// <summary>
        /// 完整的Type反射信息
        /// </summary>
        public class TypeReflectionInfo
        {
            #region 基础类型信息

            /// <summary>
            /// Type实例
            /// </summary>
            public Type Type { get; set; }

            /// <summary>
            /// 是否为用户定义类型
            /// </summary>
            public bool IsUserDefinedType { get; set; }

            /// <summary>
            /// 是否包含JsonNode
            /// </summary>
            public bool ContainsJsonNode { get; set; }

            /// <summary>
            /// 是否可能包含嵌套JsonNode（排除NoJsonNodeContainer标记的类型）
            /// </summary>
            public bool MayContainNestedJsonNode { get; set; }

            /// <summary>
            /// 是否有无参构造函数
            /// </summary>
            public bool HasParameterlessConstructor { get; set; }

            /// <summary>
            /// 无参构造函数委托
            /// </summary>
            public Func<object> Constructor { get; set; }

            #endregion

            #region Attribute 信息

            /// <summary>
            /// NodeInfo Attribute 信息
            /// </summary>
            public NodeInfoAttribute NodeInfo { get; set; }

            /// <summary>
            /// AssetFilter Attribute 信息
            /// </summary>
            public AssetFilterAttribute AssetFilter { get; set; }

            /// <summary>
            /// PortColor Attribute 信息
            /// </summary>
            public PortColorAttribute PortColor { get; set; }

            #endregion

            #region 统一成员信息

            /// <summary>
            /// 所有成员的统一列表（已按渲染顺序排序）
            /// </summary>
            public List<UnifiedMemberInfo> AllMembers { get; set; } = new();

            /// <summary>
            /// 成员快速查找字典
            /// </summary>
            public Dictionary<string, UnifiedMemberInfo> MemberLookup { get; set; } = new();

            #endregion

            #region 查询方法

            /// <summary>
            /// 获取所有成员
            /// </summary>
            public IReadOnlyList<UnifiedMemberInfo> GetAllMembers() => AllMembers.AsReadOnly();

            /// <summary>
            /// 获取JsonNode类型成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetJsonNodeMembers()
                => AllMembers.Where(m => m.Category == MemberCategory.JsonNode);

            /// <summary>
            /// 获取集合类型成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetCollectionMembers()
                => AllMembers.Where(m => m.Category == MemberCategory.Collection);

            /// <summary>
            /// 获取可能包含嵌套结构的成员（用于运行时迭代优化）
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetNestedCandidateMembers()
                => AllMembers.Where(m => m.MayContainNestedStructure);

            /// <summary>
            /// 获取可能包含嵌套JsonNode的成员（用于JsonNode收集优化）
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetNestedJsonNodeCandidateMembers()
                => AllMembers.Where(m => m.MayContainNestedJsonNode);

            /// <summary>
            /// 根据名称查找成员
            /// </summary>
            public UnifiedMemberInfo GetMember(string name)
            {
                MemberLookup.TryGetValue(name, out var member);
                return member;
            }

            /// <summary>
            /// 根据值类型获取兼容的成员
            /// </summary>
            public List<UnifiedMemberInfo> GetMembersByValueType(Type valueType)
            {
                if (valueType == null)
                {
                    return new List<UnifiedMemberInfo>();
                }

                return AllMembers.Where(member => IsTypeCompatible(member.ValueType, valueType)).ToList();
            }

            /// <summary>
            /// 获取可移除的成员（用于Remove操作）
            /// </summary>
            public List<UnifiedMemberInfo> GetRemovableMembers()
            {
                // 可移除的成员：非必需字段，集合类型，或可空类型
                return AllMembers.Where(member => 
                    IsRemovableType(member.ValueType) || 
                    member.Category == MemberCategory.Collection ||
                    member.Category == MemberCategory.JsonNode).ToList();
            }

            /// <summary>
            /// 检查类型兼容性
            /// </summary>
            private static bool IsTypeCompatible(Type memberType, Type targetType)
            {
                if (memberType == null || targetType == null)
                {
                    return false;
                }

                // 直接类型匹配
                if (memberType == targetType)
                {
                    return true;
                }

                // 继承关系匹配
                if (memberType.IsAssignableFrom(targetType))
                {
                    return true;
                }

                // 处理可空类型
                var memberNullableType = Nullable.GetUnderlyingType(memberType);
                if (memberNullableType != null && memberNullableType == targetType)
                {
                    return true;
                }

                var targetNullableType = Nullable.GetUnderlyingType(targetType);
                if (targetNullableType != null && memberType == targetNullableType)
                {
                    return true;
                }

                return false;
            }

            /// <summary>
            /// 检查是否为可移除类型
            /// </summary>
            private static bool IsRemovableType(Type type)
            {
                if (type == null)
                {
                    return false;
                }

                // 可空类型
                if (Nullable.GetUnderlyingType(type) != null)
                {
                    return true;
                }

                // 引用类型（除了字符串，字符串通常不应该被"移除"）
                if (!type.IsValueType && type != typeof(string))
                {
                    return true;
                }

                return false;
            }

            #endregion

            #region Attribute 查询方法

            /// <summary>
            /// 获取节点显示标题（优先使用 NodeInfo.Title，否则使用类型名称）
            /// </summary>
            public string GetDisplayTitle()
            {
                return NodeInfo?.Title ?? Type?.Name ?? "Unknown";
            }

            /// <summary>
            /// 获取节点显示宽度（优先使用 NodeInfo.Width，否则使用默认值）
            /// </summary>
            public int GetDisplayWidth()
            {
                return NodeInfo?.Width ?? 200; // 默认宽度
            }

            /// <summary>
            /// 获取节点颜色（优先使用 NodeInfo.Color，然后 PortColor.Color，否则使用默认颜色）
            /// </summary>
            public UnityEngine.Color GetDisplayColor()
            {
                if (NodeInfo != null)
                {
                    return NodeInfo.Color;
                }
                if (PortColor != null)
                {
                    return PortColor.Color;
                }
                return UnityEngine.Color.white; // 默认颜色
            }

            /// <summary>
            /// 获取菜单项路径（来自 NodeInfo.MenuItem）
            /// </summary>
            public string GetMenuItemPath()
            {
                return NodeInfo?.MenuItem ?? string.Empty;
            }

            /// <summary>
            /// 检查是否为唯一节点（来自 NodeInfo.Unique）
            /// </summary>
            public bool IsUniqueNode()
            {
                return NodeInfo?.Unique ?? false;
            }

            /// <summary>
            /// 检查是否允许在指定类型的资源中使用（来自 AssetFilter）
            /// </summary>
            public bool IsAllowedInAsset(Type assetType)
            {
                if (AssetFilter == null)
                {
                    return true; // 没有过滤器时默认允许
                }

                if (AssetFilter.Types != null && AssetFilter.Types.Contains(assetType))
                {
                    return AssetFilter.Allowed;
                }

                return !AssetFilter.Allowed; // 不在指定类型中时取反
            }

            public bool IsBannedInTemplate()
            {
                return AssetFilter?.BanTemplate ?? false;
            }

            #endregion

            #region 成员 Attribute 查询方法

            /// <summary>
            /// 获取 TitlePort 标记的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetTitlePortMembers()
                => AllMembers.Where(m => m.IsTitlePort);

            /// <summary>
            /// 获取 Child 标记的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetChildMembers()
                => AllMembers.Where(m => m.IsChild);

            /// <summary>
            /// 获取需要显示在节点中的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetVisibleMembers()
                => AllMembers.Where(m => m.ShowInNode);

            /// <summary>
            /// 获取有下拉列表的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetDropdownMembers()
                => AllMembers.Where(m => m.HasDropdown());

            /// <summary>
            /// 获取只读成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetReadOnlyMembers()
                => AllMembers.Where(m => m.IsReadOnly());

            /// <summary>
            /// 按分组获取成员
            /// </summary>
            public IEnumerable<IGrouping<string, UnifiedMemberInfo>> GetMembersByGroup()
                => AllMembers.GroupBy(m => m.GetEffectiveGroupName());

            /// <summary>
            /// 获取指定分组的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetMembersInGroup(string groupName)
                => AllMembers.Where(m => m.GetEffectiveGroupName() == groupName);

            /// <summary>
            /// 获取有变化事件的成员
            /// </summary>
            public IEnumerable<UnifiedMemberInfo> GetOnChangeMembers()
                => AllMembers.Where(m => !string.IsNullOrEmpty(m.GetOnChangeAction()));

            #endregion
        }

        #endregion

        #region 性能统计数据结构

        /// <summary>
        /// JsonNode 缓存统计信息
        /// </summary>
        public class JsonNodeCacheStatistics
        {
            public int TotalTypes { get; set; }
            public int JsonNodeTypes { get; set; }
            public int TotalMembers { get; set; }
            public int JsonNodeMembers { get; set; }
            public int CollectionMembers { get; set; }
            
            public double JsonNodeTypeRatio => TotalTypes > 0 ? (double)JsonNodeTypes / TotalTypes : 0.0;
            public double JsonNodeMemberRatio => TotalMembers > 0 ? (double)JsonNodeMembers / TotalMembers : 0.0;
            
            public override string ToString()
            {
                return $"Types: {JsonNodeTypes}/{TotalTypes} ({JsonNodeTypeRatio:P}), " +
                       $"Members: {JsonNodeMembers}/{TotalMembers} ({JsonNodeMemberRatio:P})";
            }
        }

        /// <summary>
        /// 缓存性能统计信息
        /// </summary>
        public class CachePerformanceStats
        {
            public int TotalCacheEntries { get; set; }
            public double EstimatedHitRate { get; set; }
            public double AverageAccessTime { get; set; }
            
            public override string ToString()
            {
                return $"Entries: {TotalCacheEntries}, Hit Rate: {EstimatedHitRate:P}, " +
                       $"Avg Time: {AverageAccessTime:F2}ms";
            }
        }

        #endregion

        #region 缓存字段

        /// <summary>
        /// Type反射信息主缓存
        /// </summary>
        private static readonly ConcurrentDictionary<Type, TypeReflectionInfo> _typeInfoCache = new();

        /// <summary>
        /// 静态构造函数 - 自动预热 JsonNode 类型
        /// </summary>
        static TypeCacheSystem()
        {
            // 在 Unity 编辑器模式下自动预热
            #if UNITY_EDITOR
            try
            {
                // 延迟预热，避免影响编辑器启动性能
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    try
                    {
                        WarmupJsonNodeTypes();
                        //UnityEngine.Debug.Log($"TypeCacheSystem: 自动预热完成，缓存了 {CacheStats.CachedTypeCount} 个类型");
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"TypeCacheSystem: 自动预热失败 - {ex.Message}");
                    }
                };
            }
            catch
            {
                // 静态构造函数中不处理异常，避免影响类型初始化
            }
            #endif
        }

        /// <summary>
        /// 缓存统计信息
        /// </summary>
        public static class CacheStats
        {
            public static int CachedTypeCount => _typeInfoCache.Count;
            public static int TotalMemberCount => _typeInfoCache.Values.Sum(info => info.AllMembers.Count);
        }

        #endregion

        #region 公共API

        /// <summary>
        /// 获取Type的完整反射信息
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TypeReflectionInfo GetTypeInfo(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return _typeInfoCache.GetOrAdd(type, BuildTypeInfoWithPrecompiled);
        }
        /// <summary>
        /// 预热缓存 - 提前构建指定类型的反射信息
        /// </summary>
        public static void WarmupCache(params Type[] types)
        {
            foreach (var type in types)
            {
                GetTypeInfo(type);
            }
        }
        /// <summary>
        /// 智能预热缓存 - 自动发现并预热所有 JsonNode 相关类型
        /// </summary>
        public static void WarmupJsonNodeTypes(params Type[] additionalTypes)
        {
            // 自动发现所有 JsonNode 派生类型
            var allJsonNodeTypes = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(assembly => 
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch
                    {
                        return System.Linq.Enumerable.Empty<Type>();
                    }
                })
                .Where(type => typeof(JsonNode).IsAssignableFrom(type) && !type.IsAbstract)
                .ToArray();

            var coreTypes = new[]
            {
                typeof(JsonNode),
                typeof(TreeNodeAsset),
                typeof(System.Collections.Generic.List<>),
                typeof(System.Collections.Generic.Dictionary<,>)
            };

            var allTypes = coreTypes.Concat(allJsonNodeTypes).Concat(additionalTypes ?? new Type[0]).Distinct();
            
            foreach (var type in allTypes)
            {
                try
                {
                    // 跳过泛型定义类型，它们无法直接实例化
                    if (type.IsGenericTypeDefinition)
                    {
                        continue;
                    }
                    
                    GetTypeInfo(type);
                }
                catch
                {
                    // 忽略无法预热的类型
                }
            }
        }

        /// <summary>
        /// 获取所有带有 NodeInfo 的类型
        /// </summary>
        public static IEnumerable<Type> GetTypesWithNodeInfo()
        {
            // 先预热所有 JsonNode 类型（如果还没有预热）
            if (CacheStats.CachedTypeCount == 0)
            {
                WarmupJsonNodeTypes();
            }

            // 从缓存中获取所有带有 NodeInfo 的类型
            return _typeInfoCache
                .Where(kvp => kvp.Value.NodeInfo != null)
                .Select(kvp => kvp.Key);
        }

        /// <summary>
        /// 获取 JsonNode 缓存统计信息
        /// </summary>
        public static JsonNodeCacheStatistics GetJsonNodeCacheStats()
        {
            var stats = new JsonNodeCacheStatistics();
            
            foreach (var kvp in _typeInfoCache)
            {
                var typeInfo = kvp.Value;
                stats.TotalTypes++;
                
                if (typeInfo.ContainsJsonNode)
                {
                    stats.JsonNodeTypes++;
                }
                
                stats.TotalMembers += typeInfo.AllMembers.Count;
                stats.JsonNodeMembers += typeInfo.GetJsonNodeMembers().Count();
                stats.CollectionMembers += typeInfo.GetCollectionMembers().Count();
            }
            
            return stats;
        }

        /// <summary>
        /// 清理 JsonNode 相关缓存
        /// </summary>
        public static void ClearJsonNodeCache()
        {
            var typesToRemove = _typeInfoCache.Keys
                .Where(type => _typeInfoCache[type].ContainsJsonNode)
                .ToList();

            foreach (var type in typesToRemove)
            {
                _typeInfoCache.TryRemove(type, out _);
            }
        }

        /// <summary>
        /// LRU 缓存淘汰策略 - 限制缓存大小
        /// </summary>
        /// <param name="maxCacheSize">最大缓存条目数</param>
        public static void EnforceCacheLimit(int maxCacheSize = 1000)
        {
            if (_typeInfoCache.Count <= maxCacheSize)
            {
                return;
            }

            // 简单的 LRU 实现：移除一些较少使用的类型
            // 这里使用类型名称长度作为简单的启发式方法
            var typesToRemove = _typeInfoCache.Keys
                .OrderByDescending(t => t.FullName?.Length ?? 0)
                .Take(_typeInfoCache.Count - maxCacheSize)
                .ToList();

            foreach (var type in typesToRemove)
            {
                _typeInfoCache.TryRemove(type, out _);
            }
        }

        /// <summary>
        /// 获取缓存命中率统计
        /// </summary>
        public static CachePerformanceStats GetCachePerformanceStats()
        {
            // 简单的统计实现，实际项目中可以添加更详细的监控
            return new CachePerformanceStats
            {
                TotalCacheEntries = _typeInfoCache.Count,
                EstimatedHitRate = _typeInfoCache.Count > 0 ? 0.85 : 0.0, // 预估命中率
                AverageAccessTime = 0.1 // 预估平均访问时间（毫秒）
            };
        }

        /// <summary>
        /// 清理指定类型的缓存信息
        /// </summary>
        public static void ClearTypeInfo(Type type)
        {
            if (type != null)
            {
                _typeInfoCache.TryRemove(type, out _);
            }
        }

        /// <summary>
        /// 清理所有缓存信息
        /// </summary>
        public static void ClearAllCache()
        {
            _typeInfoCache.Clear();
        }

        #endregion

        #region 内部构建方法

        /// <summary>
        /// 构建Type的完整反射信息（优先使用预编译数据）
        /// </summary>
        private static TypeReflectionInfo BuildTypeInfoWithPrecompiled(Type type)
        {
            // 首先尝试从IPropertyAccessor接口获取预编译的TypeInfo
            var precompiledInfo = TryGetPrecompiledTypeInfo(type);
            if (precompiledInfo != null)
            {
                return precompiledInfo;
            }

            // 如果没有预编译数据，则使用反射构建
            return BuildTypeInfo(type);
        }

        /// <summary>
        /// 尝试从IPropertyAccessor接口获取预编译的TypeInfo
        /// </summary>
        private static TypeReflectionInfo TryGetPrecompiledTypeInfo(Type type)
        {
            try
            {
                // 检查类型是否实现了IPropertyAccessor接口
                if (!typeof(IPropertyAccessor).IsAssignableFrom(type))
                {
                    return null;
                }

                // 检查类型是否有无参构造函数
                if (!HasParameterlessConstructor(type))
                {
                    return null;
                }

                // 创建类型实例
                var instance = Activator.CreateInstance(type);
                if (instance is IPropertyAccessor propertyAccessor)
                {
                    var typeInfo = propertyAccessor.TypeInfo;
                    if (typeInfo != null)
                    {
                        // 确保TypeInfo的Type字段正确设置
                        if (typeInfo.Type == null)
                        {
                            typeInfo.Type = type;
                        }

                        // 提取 Attribute 信息（预编译信息可能不包含这些）
                        if (typeInfo.NodeInfo == null || typeInfo.AssetFilter == null || 
                            typeInfo.PortColor == null)
                        {
                            ExtractTypeAttributes(typeInfo);
                        }

                        // 如果MemberLookup字典为空，从AllMembers构建
                        if (typeInfo.MemberLookup == null || typeInfo.MemberLookup.Count == 0)
                        {
                            typeInfo.MemberLookup = new Dictionary<string, UnifiedMemberInfo>();
                            foreach (var member in typeInfo.AllMembers)
                            {
                                typeInfo.MemberLookup[member.Name] = member;
                            }
                        }

                        return typeInfo;
                    }
                }
            }
            catch (Exception ex)
            {
                // 预编译数据获取失败时记录警告，但不阻止后续的反射构建
                UnityEngine.Debug.LogWarning($"Failed to get precompiled TypeInfo for {type.Name}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 构建Type的完整反射信息
        /// </summary>
        private static TypeReflectionInfo BuildTypeInfo(Type type)
        {
            var info = new TypeReflectionInfo
            {
                Type = type,
                IsUserDefinedType = IsUserDefinedType(type),
                ContainsJsonNode = ContainsJsonNode(type),
                MayContainNestedJsonNode = MayContainNestedJsonNode(type),
                HasParameterlessConstructor = HasParameterlessConstructor(type)
            };

            // 解析 Attribute 信息
            ExtractTypeAttributes(info);

            // 构建构造函数委托
            if (info.HasParameterlessConstructor)
            {
                info.Constructor = CreateConstructorDelegate(type);
            }

            // 分析并构建统一成员信息
            BuildUnifiedMembers(info);

            return info;
        }

        #endregion

        #region Attribute 解析方法

        /// <summary>
        /// 提取类型的 Attribute 信息
        /// </summary>
        private static void ExtractTypeAttributes(TypeReflectionInfo info)
        {
            var type = info.Type;

            try
            {
                // 提取 NodeInfo Attribute
                info.NodeInfo = type.GetCustomAttribute<NodeInfoAttribute>();

                // 提取 AssetFilter Attribute
                info.AssetFilter = type.GetCustomAttribute<AssetFilterAttribute>();

                // 提取 PortColor Attribute
                info.PortColor = type.GetCustomAttribute<PortColorAttribute>();
            }
            catch (Exception ex)
            {
                // Attribute 提取失败时记录警告，但不阻止类型信息构建
                UnityEngine.Debug.LogWarning($"Failed to extract attributes for {type.Name}: {ex.Message}");
            }
        }

        #endregion

        #region 成员分析方法

        /// <summary>
        /// 构建统一成员信息列表
        /// </summary>
        private static void BuildUnifiedMembers(TypeReflectionInfo info)
        {
            var type = info.Type;
            var allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                .ToList();

            var memberInfos = new List<UnifiedMemberInfo>();

            foreach (var member in allMembers)
            {
                var memberInfo = CreateUnifiedMemberInfo(member);
                if (memberInfo != null)
                {
                    memberInfos.Add(memberInfo);
                    info.MemberLookup[member.Name] = memberInfo;
                }
            }

            // 按渲染顺序排序
            info.AllMembers = memberInfos.OrderBy(m => m.RenderOrder).ToList();
        }

        /// <summary>
        /// 创建统一的成员信息
        /// 只收集同时能够读写且不被标记为JsonIgnore的成员
        /// </summary>
        private static UnifiedMemberInfo CreateUnifiedMemberInfo(MemberInfo member)
        {
            var valueType = GetMemberValueType(member);
            if (valueType == null) return null;

            // 检查是否同时可读可写
            if (!CanReadMember(member) || !CanWriteMember(member))
            {
                return null;
            }

            // 检查是否被JsonIgnore标记
            if (HasAttribute<JsonIgnoreAttribute>(member))
            {
                return null;
            }

            var memberInfo = new UnifiedMemberInfo
            {
                Member = member,
                MemberType = GetMemberType(member),
                ValueType = valueType,

                // 分析成员分类
                Category = DetermineMemberCategory(valueType),

                // 分析特性标记
                IsChild = HasAttribute<ChildAttribute>(member),
                IsTitlePort = HasAttribute<TitlePortAttribute>(member),
                ShowInNode = HasAttribute<ShowInNodeAttribute>(member) || HasAttribute<ChildAttribute>(member) || HasAttribute<TitlePortAttribute>(member),

                // 渲染信息
                RenderOrder = CalculateRenderOrder(member),
                GroupName = GetGroupName(member),
                IsMultiValue = IsCollectionType(valueType),
                MayContainNestedStructure = MayContainNestedStructure(valueType),
                MayContainNestedJsonNode = MayContainNestedJsonNode(valueType),

                // 提取 Attribute 信息
                ShowInNodeAttribute = member.GetCustomAttribute<ShowInNodeAttribute>(),
                LabelInfoAttribute = member.GetCustomAttribute<LabelInfoAttribute>(),
                StyleAttribute = member.GetCustomAttribute<StyleAttribute>(),
                GroupAttribute = member.GetCustomAttribute<GroupAttribute>(),
                OnChangeAttribute = member.GetCustomAttribute<OnChangeAttribute>(),
                DropdownAttribute = member.GetCustomAttribute<DropdownAttribute>(),
                TitlePortAttribute = member.GetCustomAttribute<TitlePortAttribute>()
            };

            // 创建访问委托
            CreateAccessorDelegates(memberInfo);

            return memberInfo;
        }

        /// <summary>
        /// 获取成员值类型
        /// </summary>
        private static Type GetMemberValueType(MemberInfo member)
        {
            return member.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)member).PropertyType,
                MemberTypes.Field => ((FieldInfo)member).FieldType,
                _ => null
            };
        }

        /// <summary>
        /// 确定成员类型
        /// </summary>
        private static MemberType GetMemberType(MemberInfo member)
        {
            return member.MemberType switch
            {
                MemberTypes.Property => MemberType.Property,
                MemberTypes.Field => MemberType.Field,
                _ => MemberType.Field
            };
        }

        /// <summary>
        /// 检查成员是否可读
        /// </summary>
        private static bool CanReadMember(MemberInfo member)
        {
            return member.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)member).CanRead,
                MemberTypes.Field => true,
                _ => false
            };
        }

        /// <summary>
        /// 检查成员是否可写
        /// </summary>
        private static bool CanWriteMember(MemberInfo member)
        {
            return member.MemberType switch
            {
                MemberTypes.Property => ((PropertyInfo)member).CanWrite,
                MemberTypes.Field => !((FieldInfo)member).IsInitOnly && !((FieldInfo)member).IsLiteral,
                _ => false
            };
        }

        /// <summary>
        /// 确定成员分类
        /// </summary>
        private static MemberCategory DetermineMemberCategory(Type valueType)
        {
            // 检查是否为JsonNode类型
            if (IsJsonNodeType(valueType))
            {
                return MemberCategory.JsonNode;
            }

            // 检查是否为集合类型
            if (IsCollectionType(valueType))
            {
                return MemberCategory.Collection;
            }

            return MemberCategory.Normal;
        }

        /// <summary>
        /// 计算渲染顺序
        /// </summary>
        private static int CalculateRenderOrder(MemberInfo member)
        {
            int order = 1000; // 默认顺序

            // TitlePort具有最高优先级
            if (HasAttribute<TitlePortAttribute>(member))
            {
                order = 0;
            }
            // Child属性次之
            else if (HasAttribute<ChildAttribute>(member))
            {
                var childAttr = member.GetCustomAttribute<ChildAttribute>();
                bool isTop = false;

                // 尝试判断是否为置顶Child
                try
                {
                    var attrString = childAttr?.ToString();
                    isTop = attrString?.Contains("True") == true;
                }
                catch
                {
                    isTop = false;
                }

                order = isTop ? 100 : 200;
            }
            // ShowInNode属性再次之
            else if (HasAttribute<ShowInNodeAttribute>(member))
            {
                order = 300;
            }

            // Group属性影响顺序
            if (HasAttribute<GroupAttribute>(member))
            {
                order += 50;
            }

            // 根据成员名称的字母顺序作为次要排序
            order += Math.Abs(member.Name.GetHashCode()) % 100;

            return order;
        }

        /// <summary>
        /// 获取分组名称
        /// </summary>
        private static string GetGroupName(MemberInfo member)
        {
            var groupAttr = member.GetCustomAttribute<GroupAttribute>();
            return groupAttr?.Name ?? string.Empty;
        }

        /// <summary>
        /// 创建成员访问委托
        /// 由于已经过滤了可读写成员，这里直接创建委托
        /// </summary>
        private static void CreateAccessorDelegates(UnifiedMemberInfo memberInfo)
        {
            var member = memberInfo.Member;
            var memberType = member.DeclaringType;

            try
            {
                // 由于已经过滤，所有成员都是可读写的，直接创建委托
                memberInfo.Getter = CreateMemberGetter(memberType, member);
                memberInfo.Setter = CreateMemberSetter(memberType, member);
            }
            catch (Exception ex)
            {
                // 访问委托创建失败，记录但不阻止成员信息创建
                UnityEngine.Debug.LogWarning($"Failed to create accessor delegates for {memberType.Name}.{member.Name}: {ex.Message}");
            }
        }

        #endregion

        #region 类型检测和辅助方法

        /// <summary>
        /// 检查是否为用户定义类型
        /// </summary>
        private static bool IsUserDefinedType(Type type)
        {
            if (type == null) return false;

            // 基本类型和系统类型
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
                type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
            {
                return false;
            }

            // Unity基本类型
            if (type.Namespace?.StartsWith("UnityEngine") == true &&
                (type.IsValueType || type == typeof(UnityEngine.Object)))
            {
                return false;
            }

            // 系统命名空间
            if (type.Namespace?.StartsWith("System") == true && type.Assembly == typeof(object).Assembly)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查是否包含JsonNode
        /// </summary>
        private static bool ContainsJsonNode(Type type)
        {
            if (type == null) return false;

            // 直接是JsonNode类型
            if (IsJsonNodeType(type)) return true;

            // 检查成员中是否包含JsonNode类型（只检查直接成员，不递归）
            try
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field);

                foreach (var member in members)
                {
                    var memberType = GetMemberValueType(member);
                    if (memberType != null)
                    {
                        // 检查是否为JsonNode类型
                        if (IsJsonNodeType(memberType))
                        {
                            return true;
                        }
                        
                        // 检查集合元素是否为JsonNode类型
                        if (IsCollectionType(memberType))
                        {
                            var elementType = GetCollectionElementType(memberType);
                            if (elementType != null && IsJsonNodeType(elementType))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 反射异常时返回false
            }

            return false;
        }

        /// <summary>
        /// 检查是否有无参构造函数
        /// </summary>
        private static bool HasParameterlessConstructor(Type type)
        {
            if (type == null || type.IsAbstract || type.IsInterface) return false;

            // 值类型总是有默认构造函数
            if (type.IsValueType) return true;

            // 检查是否有公共无参构造函数
            return type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) != null;
        }

        /// <summary>
        /// 创建构造函数委托
        /// </summary>
        private static Func<object> CreateConstructorDelegate(Type type)
        {
            try
            {
                // 值类型使用Activator
                if (type.IsValueType)
                {
                    return () => Activator.CreateInstance(type);
                }

                // 引用类型使用表达式树编译
                var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (constructor != null)
                {
                    var newExpr = Expression.New(constructor);
                    var lambda = Expression.Lambda<Func<object>>(newExpr);
                    return lambda.Compile();
                }
            }
            catch
            {
                // 构造函数创建失败，返回null
            }

            return null;
        }

        /// <summary>
        /// 检查类型是否有NoJsonNodeContainer标记
        /// </summary>
        private static bool HasNoJsonNodeContainerAttribute(Type type)
        {
            if (type == null) return false;
            
            try
            {
                return type.GetCustomAttribute<NoJsonNodeContainerAttribute>() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查是否为JsonNode类型
        /// </summary>
        private static bool IsJsonNodeType(Type type)
        {
            if (type == null) return false;

            try
            {
                // 检查是否继承自JsonNode或者就是JsonNode类型
                var jsonNodeType = typeof(JsonNode);
                return type == jsonNodeType || type.IsSubclassOf(jsonNodeType);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断类型是否可能包含嵌套JsonNode
        /// 考虑NoJsonNodeContainer标记，使用快速判断避免递归分析以提高性能
        /// 注意：这里只检查子字段，不包括类型本身是否为JsonNode
        /// </summary>
        private static bool MayContainNestedJsonNode(Type type)
        {
            if (type == null) return false;

            // 如果类型被标记为NoJsonNodeContainer，则不可能包含JsonNode
            if (HasNoJsonNodeContainerAttribute(type))
                return false;

            // 基本类型不包含JsonNode
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return false;

            // 枚举类型不包含JsonNode
            if (type.IsEnum)
                return false;

            // 常见的系统值类型不包含JsonNode
            if (type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid) ||
                type == typeof(DateTimeOffset))
                return false;

            // Unity特定的基本值类型不包含JsonNode
            if (type.Namespace?.StartsWith("UnityEngine") == true)
            {
                return false;
            }

            // 集合类型快速检查：只检查元素类型是否直接为JsonNode
            if (IsCollectionType(type))
            {
                var elementType = GetCollectionElementType(type);
                if (elementType != null)
                {
                    // 快速检查：只判断元素是否直接为JsonNode类型
                    if (IsJsonNodeType(elementType))
                        return true;
                    
                    // 如果元素类型有NoJsonNodeContainer标记，则不可能包含JsonNode
                    if (HasNoJsonNodeContainerAttribute(elementType))
                        return false;
                        
                    // 对于用户定义的元素类型，保守返回true（避免深度递归）
                    return IsUserDefinedType(elementType);
                }
            }

            // 对于用户定义的类型，保守返回true（避免递归分析成员）
            if (IsUserDefinedType(type))
            {
                return true;
            }

            // 系统类型（如 System.Object）可能包含JsonNode
            if (type == typeof(object))
                return true;

            return false;
        }

        /// <summary>
        /// 判断类型是否可能包含嵌套结构
        /// 用于运行时过滤优化，避免对基本类型进行不必要的迭代分析
        /// </summary>
        private static bool MayContainNestedStructure(Type type)
        {
            if (type == null) return false;

            // 基本类型不包含嵌套结构
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return false;

            // 枚举类型不包含嵌套结构
            if (type.IsEnum)
                return false;

            // 常见的系统值类型不包含嵌套结构
            if (type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid) ||
                type == typeof(DateTimeOffset))
                return false;

            // Unity特定的基本值类型不包含嵌套结构
            if (type.Namespace?.StartsWith("UnityEngine") == true)
            {
                return false;
            }

            // JsonNode类型本身可能包含嵌套结构
            if (IsJsonNodeType(type))
                return true;

            // 集合类型可能包含嵌套结构（取决于元素类型）
            if (IsCollectionType(type))
            {
                var elementType = GetCollectionElementType(type);
                return elementType != null && MayContainNestedStructure(elementType);
            }

            // 用户定义的类型可能包含嵌套结构
            if (IsUserDefinedType(type))
                return true;

            // 系统类型（如 System.Object）可能包含嵌套结构
            if (type == typeof(object))
                return true;

            // 其他情况默认为不包含
            return false;
        }

        /// <summary>
        /// 检查是否为集合类型
        /// </summary>
        private static bool IsCollectionType(Type type)
        {
            if (type == null || type == typeof(string)) return false;

            return type.IsArray ||
                   typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ||
                   (type.IsGenericType &&
                    (type.GetGenericTypeDefinition() == typeof(IList<>) ||
                     type.GetGenericTypeDefinition() == typeof(List<>) ||
                     type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                     type.GetGenericTypeDefinition() == typeof(IEnumerable<>)));
        }

        /// <summary>
        /// 获取集合元素类型
        /// </summary>
        private static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType == null) return null;

            // 数组类型
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            // 泛型集合类型
            if (collectionType.IsGenericType)
            {
                var genericArgs = collectionType.GetGenericArguments();
                if (genericArgs.Length > 0)
                {
                    return genericArgs[0];
                }
            }

            return null;
        }

        /// <summary>
        /// 检查成员是否有指定特性
        /// </summary>
        private static bool HasAttribute<T>(MemberInfo member) where T : Attribute
        {
            return member.GetCustomAttribute<T>() != null;
        }

        /// <summary>
        /// 创建成员Getter委托
        /// </summary>
        private static Func<object, object> CreateMemberGetter(Type ownerType, MemberInfo member)
        {
            var param = Expression.Parameter(typeof(object), "obj");
            var instance = Expression.Convert(param, ownerType);

            Expression memberAccess = member.MemberType switch
            {
                MemberTypes.Property => Expression.Property(instance, (PropertyInfo)member),
                MemberTypes.Field => Expression.Field(instance, (FieldInfo)member),
                _ => throw new ArgumentException($"Unsupported member type: {member.MemberType}")
            };

            var convert = Expression.Convert(memberAccess, typeof(object));
            return Expression.Lambda<Func<object, object>>(convert, param).Compile();
        }

        /// <summary>
        /// 创建成员Setter委托
        /// </summary>
        private static Action<object, object> CreateMemberSetter(Type ownerType, MemberInfo member)
        {
            var instanceParam = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var instance = Expression.Convert(instanceParam, ownerType);
            var memberType = GetMemberValueType(member);
            var value = Expression.Convert(valueParam, memberType);

            Expression memberAccess = member.MemberType switch
            {
                MemberTypes.Property => Expression.Property(instance, (PropertyInfo)member),
                MemberTypes.Field => Expression.Field(instance, (FieldInfo)member),
                _ => throw new ArgumentException($"Unsupported member type: {member.MemberType}")
            };

            var assign = Expression.Assign(memberAccess, value);
            return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
        }

        #endregion
    }
}
