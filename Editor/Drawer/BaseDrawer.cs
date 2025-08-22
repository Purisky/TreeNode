using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    // 优化的反射缓存 - 使用 TypeCacheSystem 作为后端
    public static class ReflectionCache
    {
        /// <summary>
        /// 获取带有指定 Attribute 的成员 - 使用 TypeCacheSystem 优化
        /// </summary>
        public static MemberInfo[] GetCachedMembers<T>(Type type) where T : Attribute
        {
            var typeInfo = TypeCacheSystem.GetTypeInfo(type);
            
            // 根据 Attribute 类型返回对应的成员
            return typeof(T).Name switch
            {
                nameof(ShowInNodeAttribute) => typeInfo.GetVisibleMembers().Select(m => m.Member).ToArray(),
                nameof(ChildAttribute) => typeInfo.GetChildMembers().Select(m => m.Member).ToArray(),
                nameof(TitlePortAttribute) => typeInfo.GetTitlePortMembers().Select(m => m.Member).ToArray(),
                nameof(DropdownAttribute) => typeInfo.GetDropdownMembers().Select(m => m.Member).ToArray(),
                nameof(OnChangeAttribute) => typeInfo.GetOnChangeMembers().Select(m => m.Member).ToArray(),
                _ => GetMembersByAttributeFallback<T>(typeInfo) // 回退到通用方法
            };
        }
        
        /// <summary>
        /// 获取成员的指定 Attribute - 使用 TypeCacheSystem 预解析的信息
        /// </summary>
        public static T GetCachedAttribute<T>(MemberInfo member) where T : Attribute
        {
            // 首先尝试从 TypeCacheSystem 获取预解析的 Attribute
            var typeInfo = TypeCacheSystem.GetTypeInfo(member.DeclaringType);
            var unifiedMember = typeInfo.GetMember(member.Name);
            
            if (unifiedMember != null)
            {
                return typeof(T).Name switch
                {
                    nameof(ShowInNodeAttribute) => unifiedMember.ShowInNodeAttribute as T,
                    nameof(LabelInfoAttribute) => unifiedMember.LabelInfoAttribute as T,
                    nameof(StyleAttribute) => unifiedMember.StyleAttribute as T,
                    nameof(GroupAttribute) => unifiedMember.GroupAttribute as T,
                    nameof(OnChangeAttribute) => unifiedMember.OnChangeAttribute as T,
                    nameof(DropdownAttribute) => unifiedMember.DropdownAttribute as T,
                    nameof(TitlePortAttribute) => unifiedMember.TitlePortAttribute as T,
                    _ => member.GetCustomAttribute<T>() // 回退到反射
                };
            }
            
            // 如果 TypeCacheSystem 中没有找到，回退到直接反射
            return member.GetCustomAttribute<T>();
        }
        
        /// <summary>
        /// 通用方法：从 UnifiedMemberInfo 中查找带有指定 Attribute 的成员
        /// </summary>
        private static MemberInfo[] GetMembersByAttributeFallback<T>(TypeCacheSystem.TypeReflectionInfo typeInfo) where T : Attribute
        {
            var attributeName = typeof(T).Name;
            return typeInfo.AllMembers
                .Where(member => HasAttributeByName(member, attributeName))
                .Select(member => member.Member)
                .ToArray();
        }
        
        /// <summary>
        /// 检查 UnifiedMemberInfo 是否有指定名称的 Attribute
        /// </summary>
        private static bool HasAttributeByName(TypeCacheSystem.UnifiedMemberInfo member, string attributeName)
        {
            return attributeName switch
            {
                nameof(ShowInNodeAttribute) => member.ShowInNodeAttribute != null,
                nameof(LabelInfoAttribute) => member.LabelInfoAttribute != null,
                nameof(StyleAttribute) => member.StyleAttribute != null,
                nameof(GroupAttribute) => member.GroupAttribute != null,
                nameof(OnChangeAttribute) => member.OnChangeAttribute != null,
                nameof(DropdownAttribute) => member.DropdownAttribute != null,
                nameof(TitlePortAttribute) => member.TitlePortAttribute != null,
                nameof(ChildAttribute) => member.IsChild,
                _ => member.Member.GetCustomAttribute(Type.GetType($"TreeNode.Runtime.{attributeName}") ?? 
                                                    Type.GetType($"TreeNode.{attributeName}")) != null
            };
        }

        /// <summary>
        /// 清理缓存 - 现在委托给 TypeCacheSystem
        /// </summary>
        public static void ClearCache()
        {
            TypeCacheSystem.ClearAllCache();
        }
    }

    public abstract class BaseDrawer
    {
        public abstract Type DrawType { get; }
        public abstract PropertyElement Create(MemberMeta  memberMeta, ViewNode node, PAPath path, Action action);

        public static Label CreateLabel(LabelInfoAttribute info = null)
        {
            Label label = new();
            label.SetInfo(info);
            return label;
        }
    }

    public class DrawerManager
    {
        // 改为线程安全的缓存机制
        private static readonly ConcurrentDictionary<Type, BaseDrawer> _drawerCache = new();
        static ConcurrentDictionary<Type, BaseDrawer> Drawers;
        
        static DrawerManager()
        {
            InitDrawers();
        }
        static void InitDrawers()
        {

            //using Timer timer = new("Init Drawers");
            Drawers = new();
            Type[] types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).Where(n => !n.IsAbstract&& n.IsSubclassOf(typeof(BaseDrawer))).ToArray();
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i].IsGenericType) { continue; }
                BaseDrawer baseDrawer = (BaseDrawer)Activator.CreateInstance(types[i]);
                Drawers[baseDrawer.DrawType] = baseDrawer;
            }
        }

        public static BaseDrawer GetDropdownDrawer(Type type)
        {
            Type keyType = typeof(DropdownList<>).MakeGenericType(type);
            
            // 使用线程安全的缓存检查
            if (_drawerCache.TryGetValue(keyType, out var cachedDrawer))
                return cachedDrawer;
                
            if (Drawers.TryGetValue(keyType, out BaseDrawer drawer))
            {
                _drawerCache.TryAdd(keyType, drawer);
                return drawer;
            }
            Type drawerType = typeof(DropdownDrawer<>).MakeGenericType(type);
            drawer = Activator.CreateInstance(drawerType) as BaseDrawer;
            Drawers[keyType] = drawer;
            _drawerCache.TryAdd(keyType, drawer);
            return drawer;
        }

        public static BaseDrawer GetEnumDrawer(Type type)
        {
            // 使用线程安全的缓存检查
            if (_drawerCache.TryGetValue(type, out var cachedDrawer))
                return cachedDrawer;
                
            if (Drawers.TryGetValue(type, out BaseDrawer drawer))
            {
                _drawerCache.TryAdd(type, drawer);
                return drawer;
            }
            Type drawerType = typeof(EnumDrawer<>).MakeGenericType(type);
            drawer = Activator.CreateInstance(drawerType) as BaseDrawer;
            Drawers[type] = drawer;
            _drawerCache.TryAdd(type, drawer);
            return drawer;
        }

        // 统一的缓存清理方法
        public static void ClearCache()
        {
            _drawerCache.Clear();
            // 注意：不清理 Drawers，因为那是静态注册的基础 Drawer 映射
        }
        
        /// <summary>
        /// 完全重新初始化 DrawerManager（慎用）
        /// </summary>
        public static void Reset()
        {
            _drawerCache.Clear();
            InitDrawers(); // 重新初始化基础 Drawer 映射
        }



        public static bool TryGet(MemberInfo member, out BaseDrawer drawer)
        {
            // 使用 TypeCacheSystem 优化的版本
            var typeInfo = TypeCacheSystem.GetTypeInfo(member.DeclaringType);
            var unifiedMember = typeInfo.GetMember(member.Name);
            
            if (unifiedMember != null)
            {
                Type type = unifiedMember.ValueType;
                if (type.IsEnum)
                {
                    drawer = GetEnumDrawer(type);
                    return true;
                }
                
                // 检查是否有下拉列表（使用预解析的 DropdownAttribute）
                if (unifiedMember.HasDropdown() && !type.Inherited(typeof(IList)))
                {
                    drawer = GetDropdownDrawer(type);
                    return true;
                }
                
                return TryGet(type, out drawer);
            }
            
            // 回退到原始逻辑
            Type memberType = member.GetValueType();
            if (memberType.IsEnum)
            {
                drawer = GetEnumDrawer(memberType);
                return true;
            }
            DropdownAttribute dropdown = member.GetCustomAttribute<DropdownAttribute>();
            if (dropdown != null && !memberType.Inherited(typeof(IList)))
            {
                drawer = GetDropdownDrawer(memberType);
                return true;
            }
            
            return TryGet(memberType, out drawer);
        }
        public static bool TryGet(Type type, out BaseDrawer drawer)
        {
            if (Drawers.TryGetValue(type, out drawer))
            {
                return drawer != null;
            }
            if (type.Inherited(typeof(IList)))
            {
                return Drawers.TryGetValue(typeof(List<>), out drawer);
            }
            if (type.IsComplex())
            {

                Type drawerType = typeof(ComplexDrawer<>).MakeGenericType(type);
                drawer = Activator.CreateInstance(drawerType) as BaseDrawer;
                Drawers[type] = drawer;
                return true;
            }
            else
            {
                Drawers.TryAdd(type, null);
            }
            return false;
        }
        public static BaseDrawer Get(Type type) => TryGet(type, out BaseDrawer drawer) ? drawer : null;


    }

}
