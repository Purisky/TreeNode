using System.Collections.Generic;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class History
    {
        const int MaxStep = 5;

        TreeNodeGraphWindow Window;
        List<HistoryStep> Steps = new();
        Stack<HistoryStep> RedoSteps = new();

        public History(TreeNodeGraphWindow window)
        {
            Window = window;
            AddStep(false);
        }
        public void Clear()
        {
            HistoryStep historyStep = Steps[0];
            Steps.Clear();
            Steps.Add(historyStep);
            RedoSteps.Clear();
        }
        public void AddStep(bool dirty = true)
        {
            if (dirty)
            {
                Window.MakeDirty();
            }
            Steps.Add(new HistoryStep(Window.JsonAsset));
            if (Steps.Count > MaxStep)
            {
                Steps.RemoveAt(0);
            }
            RedoSteps.Clear();
        }
        public bool Undo()
        {
            if (Steps.Count<=1) { return false; }
            Debug.Log($"Undo:[{Steps.Count}]");
            HistoryStep step = Steps[^1];
            Steps.RemoveAt(Steps.Count - 1);
            RedoSteps.Push(step);
            Commit(step, true);
            return true;
        }
        public bool Redo()
        {
            if (!RedoSteps.Any()) { return false; }
            Debug.Log("Redo");
            HistoryStep step = RedoSteps.Pop();
            Steps.Add(step);
            Commit(step, false);
            return true;
        }






        void Commit(HistoryStep step, bool undo)
        {
            if (undo)
            {
                if (Steps.Any())
                {
                    Window.JsonAsset = Steps[^1].GetAsset();
                    Window.GraphView.Redraw();
                }
            }
            else
            {
                Window.JsonAsset = step.GetAsset();
                Window.GraphView.Redraw();
            }
        }



        public class HistoryStep
        {
            string json;

            public HistoryStep(JsonAsset asset)
            {
                json = Json.ToJson(asset);
            }

            public JsonAsset GetAsset()
            {
                return Json.Get<JsonAsset>(json);
            }
        }






    }

    public static class HistoryExtensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.History.AddStep();
        }
    }


    //public enum HistotyType
    //{
    //    Add,
    //    Remove,
    //    Modify,
    //}

}
