using UnityEditor;
using UnityEngine;
using TreeNode.Test;
using TreeNode.Utility;
using TreeNode.Runtime;
using System;
using UnityEditor.PackageManager.UI;
using System.Reflection;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unity.Properties;
namespace TreeNode.Editor
{
    public static class Test
    {
        [MenuItem("Test/Run _F5")]
        public static void RunF5()
        {
            //获取当前项目路径


            //Debug.Log(
        }
        public class TestType0
        { 
            
        }
        public class TestType1 : TestType0
        {
            public int Index;
        }
    }
}
