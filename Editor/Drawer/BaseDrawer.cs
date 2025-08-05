using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    // 缓存反射获取的成员信息
    public static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> _membersCache = new();
        private static readonly ConcurrentDictionary<MemberInfo, Attribute[]> _attributesCache = new();
        
        public static MemberInfo[] GetCachedMembers<T>(Type type) where T : Attribute
        {
            return _membersCache.GetOrAdd(type, t => t.GetAll<T>().ToArray());
        }
        
        public static T GetCachedAttribute<T>(MemberInfo member) where T : Attribute
        {
            var attributes = _attributesCache.GetOrAdd(member, m => m.GetCustomAttributes().ToArray());
            return attributes.OfType<T>().FirstOrDefault();
        }
        

        public static void ClearCache()
        {
            _membersCache.Clear();
            _attributesCache.Clear();
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

        // 清理缓存方法
        public static void ClearCache()
        {
            _drawerCache.Clear();
        }





        public static bool TryGet(MemberInfo member, out BaseDrawer drawer)
        {
            Type type = member.GetValueType();
            if (type.IsEnum)
            {
                drawer = GetEnumDrawer(type);
                return true;
            }
            DropdownAttribute dropdown = member.GetCustomAttribute<DropdownAttribute>();
            if (dropdown != null && !type.Inherited(typeof(IList)))
            {
                drawer = GetDropdownDrawer(type);
                return true;
            }
            else
            {
                return TryGet(type, out drawer);
            }
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
