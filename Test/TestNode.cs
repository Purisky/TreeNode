using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
namespace TreeNode.Test
{
    public class TestNode0 : JsonNode
    {
        public string Data;
    }
    [NodeInfo(null,"TestNode1",200, "Test/TestNode1"), PortColor("#0000ff")]
    public class TestNode1 : JsonNode
    {
        [Child]
        public List<JsonNode> Nodes;
    }
    [NodeInfo(typeof(TestNode2),"TestNode2", 500, "Test/TestNode2"),PortColor("#ff0000")]
    public class TestNode2 : JsonNode
    {
        [Child, Group("b")]
        public List<TestNode2> node;
        [Child]
        public TestNode2 nodexxx;
        [ShowInNode,LabelInfo(Width =50),Group("a",Width =0.2f)]
        public float text;
        [ShowInNode(ReadOnly =true), LabelInfo(Width = 50), Group("a")]
        public float text2;
        [ShowInNode, LabelInfo(Width = 50), Group("b")]
        public string text_;

        [ShowInNode(ReadOnly =false), Group("b")]
        public bool asdasd;
        [ShowInNode(ReadOnly = false), LabelInfo(Width = 50), Group("c")]
        public List<TestStruct> ints;
        [ShowInNode(ReadOnly = true)]
        public NumType NumType;

        [ShowInNode(ReadOnly = false), Dropdown(nameof(GetDropdownItems))]
        public List<string> Dropdown;


        [ShowInNode]
        public TestStruct TestStruct;

        public static DropdownList<string> GetDropdownItems()
        {
            DropdownList<string> list = new();
            for (int i = 0; i < 5; i++)
            {
                list.Add(new($"group{i/2}/ text{i}", $"value {i}"));
            }


            return list;

        }



    }
    [NodeInfo(typeof(TestNode3),"TestNode3", 200, "Test/TestNode3"), PortColor("#00ff00")]
    public class TestNode3 : JsonNode
    {
        public JsonNode node;
    }

    [NodeInfo(typeof(NumNode), "TestNumNode", 250, "Test/TestNumNode"), PortColor("#00ff00")]
    public class TestNumNode : NumNode
    {
        [Child, LabelInfo(Width = 100 )]
        public NumValue node0;
        [Child]
        public NumValue node1;

        public override string GetText()
        {
            string text0 = node0 == null ? "0" : node0.GetText(); 
            string text1 = node1 == null ? "0" : node1.GetText();
            return $"({text0}+{text1})";

        }

    }

    public struct TestStruct
    {
        [ShowInNode(ReadOnly = false), LabelInfo(Width = 50), Group("a")]
        public int index;
        [ShowInNode(ReadOnly = false), LabelInfo(Width = 50), Group("a")]
        public string text;
        [ShowInNode(ReadOnly = false), LabelInfo(Width = 50), Group("a")]
        public Vector2 pos;

        [Child]
        public NumValue node1;
    }

    public enum NumType
    {
        [InspectorName("aaa/asdas")]
        Int = 1,
        Float = 2,
        Double = 4,
        Decimal = 8,
    }
}