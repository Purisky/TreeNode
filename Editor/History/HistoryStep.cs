using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Editor
{
    public partial class History
    {
        public class HistoryStep
        {
            string json;
            public List<IAtomicOperation> Operations { get; private set; } = new();
            public string Description { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public bool IsCommitted { get; set; } = false;

            public HistoryStep()
            {
                Timestamp = DateTime.Now;
            }

            public HistoryStep(JsonAsset asset) : this()
            {
                if (asset != null)
                {
                    json = Json.ToJson(asset);
                }
                else
                {
                    json = null;
                }
                Description = "传统操作";
                IsCommitted = true;
            }

            /// <summary>
            /// 确保步骤包含状态快照
            /// </summary>
            public void EnsureSnapshot(JsonAsset asset)
            {
                if (string.IsNullOrEmpty(json) && asset != null)
                {
                    json = Json.ToJson(asset);
                }
            }

            public JsonAsset GetAsset()
            {
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("HistoryStep的json数据为空，无法恢复状态");
                    return null;
                }

                try
                {
                    return Json.Get<JsonAsset>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"反序列化HistoryStep失败: {e.Message}");
                    return null;
                }
            }

            public void AddOperation(IAtomicOperation operation)
            {
                if (operation != null)
                {
                    Operations.Add(operation);
                }
            }

            public void Commit(string description = "")
            {
                if (!string.IsNullOrEmpty(description))
                {
                    Description = description;
                }
                IsCommitted = true;
            }
        }
    }
}
