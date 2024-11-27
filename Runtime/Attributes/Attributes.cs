using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
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

    [AttributeUsage(AttributeTargets.Class)]
    public class AssetIconAttribute : Attribute
    {
        public string path;
        public AssetIconAttribute(string path = null)
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
        public NodeInfoAttribute(Type type, string title,int width, string menuItem = "",string color = "#3F3F3F")
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
    public class AssetFilterAttribute : Attribute
    {
        public bool Allowed;
        public bool BanPrefab;
        public HashSet<Type> Types;
        public bool Unique;
        public AssetFilterAttribute(bool allowed, bool unique,bool banPrefab, params Type[] types)
        {
            Allowed = allowed;
            BanPrefab = banPrefab;
            Types = new HashSet<Type>(types);
            Unique = unique;
        }
        public AssetFilterAttribute(bool allowed, params Type[] types)
        {
            Allowed = allowed;
            BanPrefab = false;
            Types = new HashSet<Type>(types);
            Unique = false;
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
    public class LabelInfoAttribute : InspectorNameAttribute
    {
        public static LabelInfoAttribute Default = new();
        public static Color DefaultColor = new (210/256f, 210 / 256f, 210 / 256f);
        public string Text;
        /// <summary>
        /// 0=>Auto, (0~1]=>Percent, (1,∞)=>Pixel
        /// </summary>
        public float Width = 0.5f;
        public int Size = 11;
        public string Color = "#D2D2D2";
        public bool Hide;
        public LabelInfoAttribute() : base(null)
        {
        }
        public LabelInfoAttribute(string text):base(text)
        {
            Text = text;
        }
        public LabelInfoAttribute(string text,float width = 0.5f) : base(text)
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

    public class DropdownAttribute : Attribute
    {
        public string ListGetter;
        public DropdownAttribute( string listGetter)
        {
            ListGetter = listGetter;
        }
    }

}
