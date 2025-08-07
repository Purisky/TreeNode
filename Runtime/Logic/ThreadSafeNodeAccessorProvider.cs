using System;
using System.Collections.Concurrent;
using TreeNode.Runtime.Generated;

namespace TreeNode.Runtime.Logic
{
    /// <summary>
    /// 线程安全的节点访问器提供者
    /// 使用ConcurrentDictionary和双重检查锁定模式确保高性能并发访问
    /// </summary>
    public class ThreadSafeNodeAccessorProvider : INodeAccessorProvider
    {
        /// <summary>
        /// 线程安全的访问器缓存
        /// </summary>
        private readonly ConcurrentDictionary<Type, INodeAccessor> _accessors;
        
        /// <summary>
        /// 访问器工厂委托
        /// </summary>
        private readonly Func<Type, INodeAccessor> _accessorFactory;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="accessorFactory">访问器创建工厂，如果为null则使用反射访问器</param>
        public ThreadSafeNodeAccessorProvider(Func<Type, INodeAccessor> accessorFactory = null)
        {
            _accessors = new ConcurrentDictionary<Type, INodeAccessor>();
            _accessorFactory = accessorFactory ?? CreateReflectionAccessor;
        }

        /// <summary>
        /// 获取指定类型的访问器 - 线程安全
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <returns>访问器实例</returns>
        public INodeAccessor GetAccessor(Type nodeType)
        {
            if (nodeType == null)
                throw new ArgumentNullException(nameof(nodeType));

            // 使用ConcurrentDictionary的GetOrAdd方法确保线程安全
            return _accessors.GetOrAdd(nodeType, _accessorFactory);
        }

        /// <summary>
        /// 注册访问器 - 线程安全
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="accessor">访问器实例</param>
        public void RegisterAccessor(Type nodeType, INodeAccessor accessor)
        {
            if (nodeType == null)
                throw new ArgumentNullException(nameof(nodeType));
            if (accessor == null)
                throw new ArgumentNullException(nameof(accessor));

            _accessors.AddOrUpdate(nodeType, accessor, (key, oldValue) => accessor);
        }

        /// <summary>
        /// 尝试获取访问器 - 线程安全
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="accessor">输出访问器</param>
        /// <returns>是否找到访问器</returns>
        public bool TryGetAccessor(Type nodeType, out INodeAccessor accessor)
        {
            if (nodeType == null)
            {
                accessor = null;
                return false;
            }

            return _accessors.TryGetValue(nodeType, out accessor);
        }

        /// <summary>
        /// 获取已注册的访问器数量
        /// </summary>
        public int AccessorCount => _accessors.Count;

        /// <summary>
        /// 清除所有访问器缓存
        /// </summary>
        public void ClearCache()
        {
            _accessors.Clear();
        }

        /// <summary>
        /// 预注册常用类型的访问器
        /// </summary>
        /// <param name="types">要预注册的类型集合</param>
        public void PreRegisterAccessors(params Type[] types)
        {
            if (types == null) return;

            // 并行预注册访问器
            System.Threading.Tasks.Parallel.ForEach(types, type =>
            {
                if (type != null && !_accessors.ContainsKey(type))
                {
                    GetAccessor(type); // 触发访问器创建
                }
            });
        }

        /// <summary>
        /// 默认的反射访问器创建方法
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <returns>反射访问器实例</returns>
        private static INodeAccessor CreateReflectionAccessor(Type nodeType)
        {
            return new ThreadSafeReflectionAccessor(nodeType);
        }
    }
}