using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Runtime
{

    [NodeInfo(typeof(NumNode), "二元运算", 100, "数学/二元运算"), PortColor("#00ff00")]
    public class BinaryCal: NumNode
    {
        [Child, LabelInfo(Hide = true)]
        public NumValue Left;
        [JsonProperty,ShowInNode,LabelInfo(Hide =true)]
        public CalculateType CalculateType;
        [Child, LabelInfo(Hide = true)]
        public NumValue Right;
        public override string GetText()
        {
            string left = Left == null ? "0" : Left.GetText();
            string right = Right == null ? "0" : Right.GetText();
            string calculateText = CalculateType switch
            {
                CalculateType.Add => "+",
                CalculateType.Sub => "-",
                CalculateType.Mul => "*",
                CalculateType.Div => "/",
                CalculateType.Mod => "%",
                CalculateType.DivInt => "//",
                CalculateType.Random => "~",
                _=>"?"
            };
            return $"({left}{calculateText}{right})";
        }
    }


    [NodeInfo(typeof(NumNode), "三元运算", 100, "数学/三元运算"), PortColor("#00ff00")]
    public class ConditionCal : NumNode
    {
        [Child, LabelInfo(Text ="条件")]
        public Condition Condition;
        [Child, LabelInfo(Text = "真",Width =10)]
        public NumValue True;
        [Child, LabelInfo(Text = "假", Width = 10)]
        public NumValue False;
        public override string GetText()
        {
            string _true = True == null ? "0" : True.GetText();
            string _false = False == null ? "0" : False.GetText();
            if (Condition == null)
            {
                return _true;
            }
            return $"({Condition.GetText()}?{_true}:{_false})";
        }
    }

    [NodeInfo(typeof(Condition), "比较", 100, "数学/比较"), PortColor("#0000ff")]
    public class Compare : Condition
    {
        [Child, LabelInfo(Hide = true)]
        public NumValue Left;
        [JsonProperty, ShowInNode, LabelInfo(Hide = true)]
        public CompareType CompareType;
        [Child, LabelInfo(Hide = true)]
        public NumValue Right;
        public override string GetText()
        {
            string left = Left == null ? "0" : Left.GetText();
            string right = Right == null ? "0" : Right.GetText();
            string compareText = CompareType switch
            {
                CompareType.GreaterThan => ">",
                CompareType.GreaterThanOrEqual => "≥",
                CompareType.LessThan => "<",
                CompareType.LessThanOrEqual => "≤",
                CompareType.Equal => "=",
                CompareType.NotEqual => "≠",
                _ => "?"
            };
            return $"({left}{compareText}{right})";
        }
    }
    [NodeInfo(typeof(Condition), "与", 70, "数学/逻辑/与", "#000080"), PortColor("#0000ff")]
    public class And : Condition
    {
        [Child, LabelInfo(Hide = true)]
        public List<Condition> Conditions;
        public override string GetText()
        {
            if (Conditions.Count == 0) { return "true"; }
            return $"({string.Join("&",Conditions.Select(n=>n.GetText()))})";
        }

    }
    [NodeInfo(typeof(Condition), "或", 70, "数学/逻辑/或", "#D2691E"), PortColor("#0000ff")]
    public class Or : Condition
    {
        [Child, LabelInfo(Hide = true)]
        public  List<Condition> Conditions;
        public override string GetText()
        {
            if (Conditions==null||Conditions.Count == 0) { return "true"; }
            return $"({string.Join("|", Conditions.Select(n => n.GetText()))})";
        }
    }
    [NodeInfo(typeof(Condition), "非", 70, "数学/逻辑/非", "#800000"), PortColor("#0000ff")]
    public class Not : Condition
    {
        [Child, LabelInfo(Hide = true)]
        public Condition Condition;
        public override string GetText()
        {
            if (Condition==null) { return "true"; }
            return $"(!{Condition.GetText()})";
        }
    }


}

