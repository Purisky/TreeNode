using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Utility
{
    public class DropdownList<T> : List<DropdownItem<T>>
    {






    }
    public static class EnumList<T>// where T : Enum
    {
        public static DropdownList<T> GetList(Type assetType)
        {
            if (!typeof(T).IsSubclassOf(typeof(Enum))) { throw new Exception(); }
            DropdownList<T> dropdownItems = new ();
            Type type = typeof(T);
            AssetFilterAttribute dft = type.GetCustomAttribute<AssetFilterAttribute>();
            FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fieldInfos.Length; i++)
            {
                if (fieldInfos[i].GetCustomAttribute<HideEnumAttribute>() != null) { continue; }
                AssetFilterAttribute current = fieldInfos[i].GetCustomAttribute<AssetFilterAttribute>();
                if (!Check(dft, current, assetType)) { continue; }
                LabelInfoAttribute labelInfo = fieldInfos[i].GetCustomAttribute<LabelInfoAttribute>();
                DropdownItem<T> item = new(labelInfo?.Text ?? fieldInfos[i].Name, (T)Enum.Parse(typeof(T), fieldInfos[i].Name));
                if(labelInfo!=null&& ColorUtility.TryParseHtmlString(labelInfo.Color, out Color c))
                {
                    item.TextColor = c;
                }
                IconAttribute icon = fieldInfos[i].GetCustomAttribute<IconAttribute>();
                if (icon != null)
                {
                    item.IconPath = icon.path;
                }
                dropdownItems.Add(item);
            }
            return dropdownItems;

        }
        public static bool Check(AssetFilterAttribute dft, AssetFilterAttribute current, Type assetType)
        {
            if (current != null)
            {
                return current.Allowed == current.Types.Contains(assetType);
            }
            if (dft != null)
            {
                return dft.Allowed == dft.Types.Contains(assetType);
            }
            return true;
        
        }


    }


}
