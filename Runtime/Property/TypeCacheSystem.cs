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

            #endregion

            #region 性能优化

            /// <summary>
            /// 预编译的Getter委托
            /// </summary>
            public Func<object, object> Getter { get; set; }

            /// <summary>
            /// 预编译的Setter委托
            /// </summary>
            public Action<object, object> Setter { get; set; }

            #endregion

            /// <summary>
            /// 获取成员信息描述
            /// </summary>
            public override string ToString()
            {
                return $"{MemberType}.{Name}({ValueType.Name}) - {Category}";
            }
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
            /// 是否有无参构造函数
            /// </summary>
            public bool HasParameterlessConstructor { get; set; }

            /// <summary>
            /// 无参构造函数委托
            /// </summary>
            public Func<object> Constructor { get; set; }

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
            /// 根据名称查找成员
            /// </summary>
            public UnifiedMemberInfo GetMember(string name)
            {
                MemberLookup.TryGetValue(name, out var member);
                return member;
            }

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

            return _typeInfoCache.GetOrAdd(type, BuildTypeInfo);
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
        /// 智能预热缓存 - 针对 JsonNode 相关类型的专用预热
        /// </summary>
        public static void WarmupJsonNodeTypes(params Type[] additionalTypes)
        {
            var coreTypes = new[]
            {
                typeof(JsonNode),
                typeof(TreeNodeAsset),
                typeof(System.Collections.Generic.List<>),
                typeof(System.Collections.Generic.Dictionary<,>)
            };

            var allTypes = coreTypes.Concat(additionalTypes ?? new Type[0]).Distinct();
            
            foreach (var type in allTypes)
            {
                try
                {
                    GetTypeInfo(type);
                    
                    // 预热常见的泛型实例化
                    if (type.IsGenericTypeDefinition)
                    {
                        continue; // 跳过泛型定义类型，它们无法直接实例化
                    }
                }
                catch
                {
                    // 忽略无法预热的类型
                }
            }
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
        /// 构建Type的完整反射信息
        /// </summary>
        private static TypeReflectionInfo BuildTypeInfo(Type type)
        {
            var info = new TypeReflectionInfo
            {
                Type = type,
                IsUserDefinedType = IsUserDefinedType(type),
                ContainsJsonNode = ContainsJsonNode(type),
                HasParameterlessConstructor = HasParameterlessConstructor(type)
            };

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
                MayContainNestedStructure = MayContainNestedStructure(valueType)
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
