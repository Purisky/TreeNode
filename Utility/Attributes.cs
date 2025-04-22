using System;
using System.Collections.Generic;
using UnityEngine;

namespace TreeNode.Utility
{
    [AttributeUsage(AttributeTargets.Class)]
    public class NodeAssetAttribute : Attribute
    {
        public Type Type;
        public NodeAssetAttribute(Type type)
        {
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Class| AttributeTargets.Field)]
    public class IconAttribute : Attribute
    {
        public string path;
        public IconAttribute(string path = null)
        {
            this.path = path;
        }
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class NodeInfoAttribute : Attribute
    {
        public Type Type;
        public string Title;
        public string MenuItem;
        public int Width;
        public Color Color;
        static Color DefaultColor = new(63 / 256f, 63 / 256f, 63 / 256f, 204 / 256f);
        public bool Unique;
        public NodeInfoAttribute(Type type, string title, int width, string menuItem = "", string color = "#3F3F3F")
        {
            Type = type;
            Title = title;
            Width = width;
            MenuItem = menuItem;
            Color = ColorUtility.TryParseHtmlString(color, out Color c) ? c : DefaultColor;
            Color.a = 204 / 256f;
        }
    }


    [AttributeUsage(AttributeTargets.Class)]
    public class PortColorAttribute : Attribute
    {
        public Color Color;
        public PortColorAttribute(string color)
        {
            Color = ColorUtility.TryParseHtmlString(color, out Color c) ? c : Color.white;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ChildAttribute : ShowInNodeAttribute
    {
        public bool Require;
        public ChildAttribute(bool require = false)
        {
            Require = require;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ShowInNodeAttribute : Attribute
    {
        public int Order;
        public string ShowIf;
        public bool ReadOnly;
        public ShowInNodeAttribute()
        {
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LabelInfoAttribute : Attribute
    {
        public static LabelInfoAttribute Default = new();
        public static Color DefaultColor = new(210 / 256f, 210 / 256f, 210 / 256f);
        public string Text;
        /// <summary>
        /// 0=>Auto, (0~1]=>Percent, (1,∞)=>Pixel
        /// </summary>
        public float Width = 0.5f;
        public int Size = 11;
        public string Color = "#D2D2D2";
        public bool Hide;
        public LabelInfoAttribute()
        {
        }
        public LabelInfoAttribute(string text)
        {
            Text = text;
        }
        public LabelInfoAttribute(string text, float width = 0.5f)
        {
            Text = text;
            Width = width;
        }
    }


    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class GroupAttribute : Attribute
    {
        public string Name;
        public float Width;
        public string ShowIf;
        public GroupAttribute(string name)
        {
            Name = name;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class OnChangeAttribute : Attribute
    {
        public string Action;
        public bool IncludeChildren;
        public OnChangeAttribute(string action)
        {
            Action = action;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DropdownAttribute : Attribute
    {
        public string ListGetter;
        public bool Flat;
        public bool SkipExist;
        public DropdownAttribute(string listGetter)
        {
            ListGetter = listGetter;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class TitlePortAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public class AssetFilterAttribute : Attribute
    {
        public bool Allowed;
        public bool BanPrefab;
        public HashSet<Type> Types;
        public AssetFilterAttribute(bool allowed, bool banPrefab, params Type[] types)
        {
            Allowed = allowed;
            BanPrefab = banPrefab;
            Types = new HashSet<Type>(types);
        }
        public AssetFilterAttribute(bool allowed, params Type[] types)
        {
            Allowed = allowed;
            BanPrefab = false;
            Types = new HashSet<Type>(types);
        }


    }
    [AttributeUsage(AttributeTargets.Field)]
    public class HideEnumAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Field| AttributeTargets.Class| AttributeTargets.Property| AttributeTargets.Struct,Inherited =false)]
    public class PromptAttribute : Attribute
    {
        public string Desc;
        public PromptAttribute(string desc)
        {
            Desc = desc;
        }
    }
}
