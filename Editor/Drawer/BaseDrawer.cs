using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine.UIElements;
namespace TreeNode.Editor
{
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
            if (Drawers.TryGetValue(keyType, out BaseDrawer drawer))
            {
                return drawer;
            }
            Type drawerType = typeof(DropdownDrawer<>).MakeGenericType(type);
            drawer = Activator.CreateInstance(drawerType) as BaseDrawer;
            Drawers[keyType] = drawer;
            return drawer;
        }

        public static BaseDrawer GetEnumDrawer(Type type)
        {
            if (Drawers.TryGetValue(type, out BaseDrawer drawer))
            {
                return drawer;
            }
            Type drawerType = typeof(EnumDrawer<>).MakeGenericType(type);
            drawer = Activator.CreateInstance(drawerType) as BaseDrawer;
            Drawers[type] = drawer;
            return drawer;
        }





        public static bool TryGet(MemberInfo member, out BaseDrawer drawer)
        {
            Type type = member.GetValueType();
            DropdownAttribute dropdown = member.GetCustomAttribute<DropdownAttribute>();
            if (type.IsEnum)
            {
                drawer = GetEnumDrawer(type);
                return true;
            }
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
