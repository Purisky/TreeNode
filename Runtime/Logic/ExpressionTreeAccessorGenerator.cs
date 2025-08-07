using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using TreeNode.Runtime.Generated;
using TreeNode.Utility;

namespace TreeNode.Runtime.Logic
{
    /// <summary>
    /// 基于Expression Tree的高性能访问器生成器
    /// 在运行时编译高性能的访问委托，避免反射开销
    /// </summary>
    public sealed class ExpressionTreeAccessorGenerator : INodeAccessorProvider
    {
        #region 私有字段
        
        // 访问器缓存 - 线程安全
        private readonly ConcurrentDictionary<Type, INodeAccessor> _accessorCache = new();
        
        // 成员渲染顺序缓存
        private readonly ConcurrentDictionary<Type, Dictionary<string, int>> _renderOrderCache = new();
        
        #endregion
        
        #region INodeAccessorProvider实现
        
        /// <summary>
        /// 获取访问器，如果不存在则创建
        /// </summary>
        public INodeAccessor GetAccessor(Type nodeType)
        {
            if (nodeType == null || !typeof(JsonNode).IsAssignableFrom(nodeType))
            {
                throw new ArgumentException($"Type {nodeType?.Name} is not a JsonNode");
            }
            
            return _accessorCache.GetOrAdd(nodeType, CreateAccessor);
        }
        
        /// <summary>
        /// 注册访问器
        /// </summary>
        public void RegisterAccessor(Type nodeType, INodeAccessor accessor)
        {
            if (nodeType == null) throw new ArgumentNullException(nameof(nodeType));
            if (accessor == null) throw new ArgumentNullException(nameof(accessor));
            
            _accessorCache[nodeType] = accessor;
        }
        
        /// <summary>
        /// 尝试获取访问器
        /// </summary>
        public bool TryGetAccessor(Type nodeType, out INodeAccessor accessor)
        {
            accessor = null;
            if (nodeType == null || !typeof(JsonNode).IsAssignableFrom(nodeType))
            {
                return false;
            }
            
            return _accessorCache.TryGetValue(nodeType, out accessor);
        }
        
        #endregion
        
        #region 私有方法
        
        /// <summary>
        /// 创建访问器
        /// </summary>
        private INodeAccessor CreateAccessor(Type nodeType)
        {
            try
            {
                return new ExpressionTreeAccessor(nodeType, GetOrCreateRenderOrderMap(nodeType));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create accessor for type {nodeType.Name}", ex);
            }
        }
        
        /// <summary>
        /// 获取或创建渲染顺序映射
        /// </summary>
        private Dictionary<string, int> GetOrCreateRenderOrderMap(Type nodeType)
        {
            return _renderOrderCache.GetOrAdd(nodeType, type =>
            {
                var renderOrderMap = new Dictionary<string, int>();
                
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                    .ToList();
                
                foreach (var member in members)
                {
                    renderOrderMap[member.Name] = CalculateRenderOrder(member);
                }
                
                return renderOrderMap;
            });
        }
        
        /// <summary>
        /// 计算UI渲染顺序
        /// </summary>
        private int CalculateRenderOrder(MemberInfo member)
        {
            int order = 1000; // 默认顺序

            // TitlePort具有最高优先级
            if (member.GetCustomAttribute<TitlePortAttribute>() != null)
            {
                order = 0;
            }
            // Child属性次之
            else if (member.GetCustomAttribute<ChildAttribute>() != null)
            {
                var childAttr = member.GetCustomAttribute<ChildAttribute>();
                // Child(true) 表示置顶，优先级较高
                order = childAttr.Require ? 100 : 200;
            }
            // ShowInNode属性再次之
            else if (member.GetCustomAttribute<ShowInNodeAttribute>() != null)
            {
                order = 300;
            }

            // Group属性影响顺序
            if (member.GetCustomAttribute<GroupAttribute>() != null)
            {
                order += 50; // Group内的元素稍微延后
            }

            // 根据成员名称的字母顺序作为次要排序
            order += Math.Abs(member.Name.GetHashCode()) % 100;

            return order;
        }
        
        #endregion
        
        #region 内部访问器实现
        
        /// <summary>
        /// 基于Expression Tree的高性能访问器实现
        /// </summary>
        private sealed class ExpressionTreeAccessor : INodeAccessor
        {
            #region 私有字段
            
            private readonly Type _nodeType;
            private readonly Dictionary<string, int> _renderOrderMap;
            
            // 预编译的访问委托
            private readonly Func<JsonNode, List<JsonNode>> _childrenGetter;
            private readonly Func<JsonNode, List<(JsonNode, string, int)>> _childrenWithMetadataGetter;
            private readonly Func<JsonNode, int> _childCountGetter;
            private readonly Func<JsonNode, bool> _hasChildrenGetter;
            private readonly Action<JsonNode, JsonNode[], int[]> _unsafeChildrenGetter;
            
            #endregion
            
            #region 构造函数
            
            /// <summary>
            /// 构造函数：生成Expression Tree委托
            /// </summary>
            public ExpressionTreeAccessor(Type nodeType, Dictionary<string, int> renderOrderMap)
            {
                _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
                _renderOrderMap = renderOrderMap ?? throw new ArgumentNullException(nameof(renderOrderMap));
                
                // 生成高性能访问委托
                _childrenGetter = CreateChildrenGetter();
                _childrenWithMetadataGetter = CreateChildrenWithMetadataGetter();
                _childCountGetter = CreateChildCountGetter();
                _hasChildrenGetter = CreateHasChildrenGetter();
                _unsafeChildrenGetter = CreateUnsafeChildrenGetter();
            }
            
            #endregion
            
            #region INodeAccessor实现
            
            public void CollectChildren(JsonNode node, List<JsonNode> children)
            {
                if (node?.GetType() != _nodeType)
                {
                    throw new ArgumentException($"Node type mismatch. Expected {_nodeType.Name}, got {node?.GetType().Name}");
                }
                
                var childNodes = _childrenGetter(node);
                children.AddRange(childNodes);
            }
            
            public void CollectChildrenWithMetadata(JsonNode node, List<(JsonNode, string, int)> children)
            {
                if (node?.GetType() != _nodeType)
                {
                    throw new ArgumentException($"Node type mismatch. Expected {_nodeType.Name}, got {node?.GetType().Name}");
                }
                
                var childNodesWithMetadata = _childrenWithMetadataGetter(node);
                children.AddRange(childNodesWithMetadata);
            }
            
            public int GetRenderOrder(string memberName)
            {
                return _renderOrderMap.TryGetValue(memberName, out var order) ? order : 1000;
            }
            
            public Type GetNodeType()
            {
                return _nodeType;
            }
            
            public void CollectChildrenToBuffer(JsonNode node, JsonNode[] buffer, out int count)
            {
                if (node?.GetType() != _nodeType)
                {
                    throw new ArgumentException($"Node type mismatch. Expected {_nodeType.Name}, got {node?.GetType().Name}");
                }
                
                var countArray = new int[1]; // 用于输出计数
                _unsafeChildrenGetter(node, buffer, countArray);
                count = countArray[0];
            }
            
            public bool HasChildren(JsonNode node)
            {
                if (node?.GetType() != _nodeType)
                {
                    return false;
                }
                
                return _hasChildrenGetter(node);
            }
            
            public int GetChildCount(JsonNode node)
            {
                if (node?.GetType() != _nodeType)
                {
                    return 0;
                }
                
                return _childCountGetter(node);
            }
            
            #endregion
            
            #region Expression Tree生成方法
            
            /// <summary>
            /// 创建子节点收集器
            /// </summary>
            private Func<JsonNode, List<JsonNode>> CreateChildrenGetter()
            {
                var nodeParam = Expression.Parameter(typeof(JsonNode), "node");
                var typedNode = Expression.Convert(nodeParam, _nodeType);
                var resultList = Expression.Variable(typeof(List<JsonNode>), "result");
                
                var expressions = new List<Expression>
                {
                    Expression.Assign(resultList, Expression.New(typeof(List<JsonNode>)))
                };
                
                // 分析子节点成员
                var childMembers = GetChildMembers();
                
                foreach (var (member, memberName, isCollection, renderOrder) in childMembers)
                {
                    var memberAccess = Expression.PropertyOrField(typedNode, memberName);
                    
                    if (isCollection)
                    {
                        // 处理集合类型的子节点
                        expressions.Add(CreateCollectionChildrenExpression(memberAccess, resultList, member));
                    }
                    else
                    {
                        // 处理单个子节点
                        expressions.Add(CreateSingleChildExpression(memberAccess, resultList));
                    }
                }
                
                expressions.Add(resultList);
                
                var block = Expression.Block(new[] { resultList }, expressions);
                return Expression.Lambda<Func<JsonNode, List<JsonNode>>>(block, nodeParam).Compile();
            }
            
            /// <summary>
            /// 创建带元数据的子节点收集器
            /// </summary>
            private Func<JsonNode, List<(JsonNode, string, int)>> CreateChildrenWithMetadataGetter()
            {
                var nodeParam = Expression.Parameter(typeof(JsonNode), "node");
                var typedNode = Expression.Convert(nodeParam, _nodeType);
                var resultList = Expression.Variable(typeof(List<(JsonNode, string, int)>), "result");
                
                var expressions = new List<Expression>
                {
                    Expression.Assign(resultList, Expression.New(typeof(List<(JsonNode, string, int)>)))
                };
                
                var childMembers = GetChildMembers();
                
                foreach (var (member, memberName, isCollection, renderOrder) in childMembers)
                {
                    var memberAccess = Expression.PropertyOrField(typedNode, memberName);
                    
                    if (isCollection)
                    {
                        expressions.Add(CreateCollectionChildrenWithMetadataExpression(memberAccess, resultList, memberName, renderOrder, member));
                    }
                    else
                    {
                        expressions.Add(CreateSingleChildWithMetadataExpression(memberAccess, resultList, memberName, renderOrder));
                    }
                }
                
                expressions.Add(resultList);
                
                var block = Expression.Block(new[] { resultList }, expressions);
                return Expression.Lambda<Func<JsonNode, List<(JsonNode, string, int)>>>(block, nodeParam).Compile();
            }
            
            /// <summary>
            /// 创建子节点计数器
            /// </summary>
            private Func<JsonNode, int> CreateChildCountGetter()
            {
                var nodeParam = Expression.Parameter(typeof(JsonNode), "node");
                var typedNode = Expression.Convert(nodeParam, _nodeType);
                var count = Expression.Variable(typeof(int), "count");
                
                var expressions = new List<Expression>
                {
                    Expression.Assign(count, Expression.Constant(0))
                };
                
                var childMembers = GetChildMembers();
                
                foreach (var (member, memberName, isCollection, renderOrder) in childMembers)
                {
                    var memberAccess = Expression.PropertyOrField(typedNode, memberName);
                    
                    if (isCollection)
                    {
                        expressions.Add(CreateCollectionCountExpression(memberAccess, count, member));
                    }
                    else
                    {
                        expressions.Add(CreateSingleChildCountExpression(memberAccess, count));
                    }
                }
                
                expressions.Add(count);
                
                var block = Expression.Block(new[] { count }, expressions);
                return Expression.Lambda<Func<JsonNode, int>>(block, nodeParam).Compile();
            }
            
            /// <summary>
            /// 创建是否有子节点检查器
            /// </summary>
            private Func<JsonNode, bool> CreateHasChildrenGetter()
            {
                var nodeParam = Expression.Parameter(typeof(JsonNode), "node");
                var typedNode = Expression.Convert(nodeParam, _nodeType);
                
                var childMembers = GetChildMembers();
                
                if (childMembers.Count == 0)
                {
                    return Expression.Lambda<Func<JsonNode, bool>>(Expression.Constant(false), nodeParam).Compile();
                }
                
                Expression condition = null;
                
                foreach (var (member, memberName, isCollection, renderOrder) in childMembers)
                {
                    var memberAccess = Expression.PropertyOrField(typedNode, memberName);
                    Expression memberCondition;
                    
                    if (isCollection)
                    {
                        memberCondition = CreateCollectionHasChildrenExpression(memberAccess, member);
                    }
                    else
                    {
                        memberCondition = Expression.NotEqual(memberAccess, Expression.Constant(null));
                    }
                    
                    condition = condition == null ? memberCondition : Expression.OrElse(condition, memberCondition);
                }
                
                return Expression.Lambda<Func<JsonNode, bool>>(condition ?? Expression.Constant(false), nodeParam).Compile();
            }
            
            /// <summary>
            /// 创建不安全子节点收集器
            /// </summary>
            private Action<JsonNode, JsonNode[], int[]> CreateUnsafeChildrenGetter()
            {
                var nodeParam = Expression.Parameter(typeof(JsonNode), "node");
                var bufferParam = Expression.Parameter(typeof(JsonNode[]), "buffer");
                var countParam = Expression.Parameter(typeof(int[]), "count");
                
                var typedNode = Expression.Convert(nodeParam, _nodeType);
                var index = Expression.Variable(typeof(int), "index");
                
                var expressions = new List<Expression>
                {
                    Expression.Assign(index, Expression.Constant(0))
                };
                
                var childMembers = GetChildMembers();
                
                foreach (var (member, memberName, isCollection, renderOrder) in childMembers)
                {
                    var memberAccess = Expression.PropertyOrField(typedNode, memberName);
                    
                    if (isCollection)
                    {
                        expressions.Add(CreateUnsafeCollectionExpression(memberAccess, bufferParam, index, member));
                    }
                    else
                    {
                        expressions.Add(CreateUnsafeSingleChildExpression(memberAccess, bufferParam, index));
                    }
                }
                
                // 设置输出计数
                expressions.Add(Expression.Assign(
                    Expression.ArrayAccess(countParam, Expression.Constant(0)),
                    index
                ));
                
                var block = Expression.Block(new[] { index }, expressions);
                return Expression.Lambda<Action<JsonNode, JsonNode[], int[]>>(block, nodeParam, bufferParam, countParam).Compile();
            }
            
            #endregion
            
            #region 辅助方法
            
            /// <summary>
            /// 获取子节点成员信息
            /// </summary>
            private List<(MemberInfo member, string name, bool isCollection, int renderOrder)> GetChildMembers()
            {
                var result = new List<(MemberInfo, string, bool, int)>();
                
                var members = _nodeType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                    .Where(m => m.GetCustomAttribute<ChildAttribute>() != null || m.GetCustomAttribute<TitlePortAttribute>() != null)
                    .ToList();
                
                foreach (var member in members)
                {
                    var memberType = member.GetValueType();
                    if (memberType == null) continue;
                    
                    var renderOrder = GetRenderOrder(member.Name);
                    var isCollection = IsJsonNodeCollection(memberType);
                    var isDirectChild = typeof(JsonNode).IsAssignableFrom(memberType);
                    
                    if (isCollection || isDirectChild)
                    {
                        result.Add((member, member.Name, isCollection, renderOrder));
                    }
                }
                
                return result.OrderBy(x => x.Item4).ToList();
            }
            
            /// <summary>
            /// 检查是否是JsonNode集合类型
            /// </summary>
            private bool IsJsonNodeCollection(Type type)
            {
                if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || typeof(string).IsAssignableFrom(type))
                {
                    return false;
                }
                
                var elementType = GetCollectionElementType(type);
                return elementType != null && typeof(JsonNode).IsAssignableFrom(elementType);
            }
            
            /// <summary>
            /// 获取集合的元素类型
            /// </summary>
            private Type GetCollectionElementType(Type collectionType)
            {
                if (collectionType.IsArray)
                    return collectionType.GetElementType();
                
                if (collectionType.IsGenericType)
                {
                    var genericArgs = collectionType.GetGenericArguments();
                    if (genericArgs.Length > 0)
                        return genericArgs[0];
                }
                
                foreach (var interfaceType in collectionType.GetInterfaces())
                {
                    if (interfaceType.IsGenericType &&
                        (interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                         interfaceType.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                         interfaceType.GetGenericTypeDefinition() == typeof(IList<>)))
                    {
                        return interfaceType.GetGenericArguments()[0];
                    }
                }
                
                return null;
            }
            
            /// <summary>
            /// 创建单个子节点表达式
            /// </summary>
            private Expression CreateSingleChildExpression(Expression memberAccess, Expression resultList)
            {
                var addMethod = typeof(List<JsonNode>).GetMethod("Add");
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var addCall = Expression.Call(resultList, addMethod, memberAccess);
                
                return Expression.IfThen(nullCheck, addCall);
            }
            
            /// <summary>
            /// 创建集合子节点表达式
            /// </summary>
            private Expression CreateCollectionChildrenExpression(Expression memberAccess, Expression resultList, MemberInfo member)
            {
                var elementType = GetCollectionElementType(member.GetValueType());
                var addRangeMethod = typeof(List<JsonNode>).GetMethod("AddRange");
                var castMethod = typeof(Enumerable).GetMethod("Cast").MakeGenericMethod(typeof(JsonNode));
                
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var cast = Expression.Call(castMethod, memberAccess);
                var addRangeCall = Expression.Call(resultList, addRangeMethod, cast);
                
                return Expression.IfThen(nullCheck, addRangeCall);
            }
            
            /// <summary>
            /// 创建单个子节点元数据表达式
            /// </summary>
            private Expression CreateSingleChildWithMetadataExpression(Expression memberAccess, Expression resultList, string memberName, int renderOrder)
            {
                var addMethod = typeof(List<(JsonNode, string, int)>).GetMethod("Add");
                var tupleConstructor = typeof((JsonNode, string, int)).GetConstructors()[0];
                
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var tuple = Expression.New(tupleConstructor, 
                    memberAccess, 
                    Expression.Constant(memberName), 
                    Expression.Constant(renderOrder));
                var addCall = Expression.Call(resultList, addMethod, tuple);
                
                return Expression.IfThen(nullCheck, addCall);
            }
            
            /// <summary>
            /// 创建集合子节点元数据表达式
            /// </summary>
            private Expression CreateCollectionChildrenWithMetadataExpression(Expression memberAccess, Expression resultList, string memberName, int renderOrder, MemberInfo member)
            {
                var addMethod = typeof(List<(JsonNode, string, int)>).GetMethod("Add");
                var tupleConstructor = typeof((JsonNode, string, int)).GetConstructors()[0];
                var index = Expression.Variable(typeof(int), "i");
                
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var indexInit = Expression.Assign(index, Expression.Constant(0));
                
                // 创建foreach循环
                var breakLabel = Expression.Label("break");
                var continueLabel = Expression.Label("continue");
                
                var elementType = GetCollectionElementType(member.GetValueType());
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
                var getEnumeratorMethod = enumerableType.GetMethod("GetEnumerator");
                var enumeratorType = getEnumeratorMethod.ReturnType;
                var moveNextMethod = typeof(System.Collections.IEnumerator).GetMethod("MoveNext");
                var currentProperty = enumeratorType.GetProperty("Current");
                
                var enumerator = Expression.Variable(enumeratorType, "enumerator");
                var current = Expression.Variable(elementType, "current");
                
                var loop = Expression.Block(
                    new[] { index, enumerator, current },
                    indexInit,
                    Expression.Assign(enumerator, Expression.Call(memberAccess, getEnumeratorMethod)),
                    Expression.Loop(
                        Expression.IfThenElse(
                            Expression.Call(enumerator, moveNextMethod),
                            Expression.Block(
                                Expression.Assign(current, Expression.Property(enumerator, currentProperty)),
                                Expression.IfThen(
                                    Expression.NotEqual(current, Expression.Constant(null)),
                                    Expression.Block(
                                        Expression.Call(resultList, addMethod, 
                                            Expression.New(tupleConstructor,
                                                Expression.Convert(current, typeof(JsonNode)),
                                                Expression.Call(typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) }),
                                                    Expression.Constant("{0}[{1}]"),
                                                    Expression.Constant(memberName),
                                                    index),
                                                Expression.Constant(renderOrder))),
                                        Expression.PostIncrementAssign(index)
                                    )
                                )
                            ),
                            Expression.Break(breakLabel)
                        ),
                        breakLabel
                    )
                );
                
                return Expression.IfThen(nullCheck, loop);
            }
            
            /// <summary>
            /// 创建单个子节点计数表达式
            /// </summary>
            private Expression CreateSingleChildCountExpression(Expression memberAccess, Expression count)
            {
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var increment = Expression.PostIncrementAssign(count);
                
                return Expression.IfThen(nullCheck, increment);
            }
            
            /// <summary>
            /// 创建集合计数表达式
            /// </summary>
            private Expression CreateCollectionCountExpression(Expression memberAccess, Expression count, MemberInfo member)
            {
                var elementType = GetCollectionElementType(member.GetValueType());
                var countMethod = typeof(Enumerable).GetMethods()
                    .Where(m => m.Name == "Count" && m.GetParameters().Length == 1)
                    .First()
                    .MakeGenericMethod(elementType);
                
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var collectionCount = Expression.Call(countMethod, memberAccess);
                var addCount = Expression.AddAssign(count, collectionCount);
                
                return Expression.IfThen(nullCheck, addCount);
            }
            
            /// <summary>
            /// 创建集合是否有子节点表达式
            /// </summary>
            private Expression CreateCollectionHasChildrenExpression(Expression memberAccess, MemberInfo member)
            {
                var elementType = GetCollectionElementType(member.GetValueType());
                var anyMethod = typeof(Enumerable).GetMethods()
                    .Where(m => m.Name == "Any" && m.GetParameters().Length == 1)
                    .First()
                    .MakeGenericMethod(elementType);
                
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var hasAny = Expression.Call(anyMethod, memberAccess);
                
                return Expression.AndAlso(nullCheck, hasAny);
            }
            
            /// <summary>
            /// 创建不安全单个子节点表达式
            /// </summary>
            private Expression CreateUnsafeSingleChildExpression(Expression memberAccess, Expression buffer, Expression index)
            {
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var assign = Expression.Assign(
                    Expression.ArrayAccess(buffer, index),
                    memberAccess
                );
                var increment = Expression.PostIncrementAssign(index);
                
                return Expression.IfThen(nullCheck, Expression.Block(assign, increment));
            }
            
            /// <summary>
            /// 创建不安全集合表达式
            /// </summary>
            private Expression CreateUnsafeCollectionExpression(Expression memberAccess, Expression buffer, Expression index, MemberInfo member)
            {
                var nullCheck = Expression.NotEqual(memberAccess, Expression.Constant(null));
                var elementType = GetCollectionElementType(member.GetValueType());
                
                // 创建简化的foreach循环
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
                var getEnumeratorMethod = enumerableType.GetMethod("GetEnumerator");
                var enumeratorType = getEnumeratorMethod.ReturnType;
                var moveNextMethod = typeof(System.Collections.IEnumerator).GetMethod("MoveNext");
                var currentProperty = enumeratorType.GetProperty("Current");
                
                var enumerator = Expression.Variable(enumeratorType, "enumerator");
                var current = Expression.Variable(elementType, "current");
                var breakLabel = Expression.Label("break");
                
                var loop = Expression.Block(
                    new[] { enumerator, current },
                    Expression.Assign(enumerator, Expression.Call(memberAccess, getEnumeratorMethod)),
                    Expression.Loop(
                        Expression.IfThenElse(
                            Expression.Call(enumerator, moveNextMethod),
                            Expression.Block(
                                Expression.Assign(current, Expression.Property(enumerator, currentProperty)),
                                Expression.IfThen(
                                    Expression.NotEqual(current, Expression.Constant(null)),
                                    Expression.Block(
                                        Expression.Assign(
                                            Expression.ArrayAccess(buffer, index),
                                            Expression.Convert(current, typeof(JsonNode))
                                        ),
                                        Expression.PostIncrementAssign(index)
                                    )
                                )
                            ),
                            Expression.Break(breakLabel)
                        ),
                        breakLabel
                    )
                );
                
                return Expression.IfThen(nullCheck, loop);
            }
            
            #endregion
        }
        
        #endregion
    }
}