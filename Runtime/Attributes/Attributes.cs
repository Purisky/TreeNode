using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
{
    [AttributeUsage( AttributeTargets.Class)]
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
        public NodeInfoAttribute(Type type, string title,int width, string menuItem = "")
        {
            Type = type;
            Title = title;
            Width = width;
            MenuItem = menuItem;
        }
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class PortColorAttribute : Attribute
    {
        public Color Color;
        public PortColorAttribute(string color, string arrayColor = null)
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
    public class LabelInfoAttribute : Attribute
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
        public LabelInfoAttribute()
        {
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
