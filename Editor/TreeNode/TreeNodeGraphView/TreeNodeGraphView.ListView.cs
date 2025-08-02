using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//ListView
    {
        private readonly ListViewInitializationTracker _listViewTracker = new();

        #region ListView初始化状态管理

        /// <summary>
        /// ListView初始化状态管理器
        /// </summary>
        private class ListViewInitializationTracker
        {
            private readonly HashSet<ListView> _pendingListViews = new();
            private readonly object _lock = new object();

            public void RegisterListView(ListView listView)
            {
                lock (_lock)
                {
                    _pendingListViews.Add(listView);
                }
                Debug.Log($"注册ListView到初始化跟踪器, 当前待初始化数量: {_pendingListViews.Count}");
            }

            public void MarkListViewReady(ListView listView)
            {
                lock (_lock)
                {
                    if (_pendingListViews.Remove(listView))
                    {
                        Debug.Log($"ListView初始化完成, 剩余待初始化数量: {_pendingListViews.Count}");
                    }
                }
            }

            public bool AllListViewsReady()
            {
                lock (_lock)
                {
                    return _pendingListViews.Count == 0;
                }
            }

            public int PendingCount()
            {
                lock (_lock)
                {
                    return _pendingListViews.Count;
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _pendingListViews.Clear();
                }
            }
        }

        /// <summary>
        /// 智能检测是否有ListView节点需要等待初始化
        /// </summary>
        private async Task<bool> CheckForListViewNodesAsync(List<JsonNodeTree.NodeMetadata> edgeMetadataList, CancellationToken cancellationToken)
        {
            bool hasListViewNodes = false;

            await ExecuteOnMainThreadAsync(() =>
            {
                // 检查是否有节点的端口处在ListView内部
                foreach (var metadata in edgeMetadataList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (NodeDic.TryGetValue(metadata.Parent.Node, out var parentViewNode))
                    {
                        // 检查该节点是否包含ListView
                        var listViews = parentViewNode.Query<ListView>().ToList();
                        if (listViews.Any())
                        {
                            hasListViewNodes = true;

                            // 注册所有未初始化的ListView
                            foreach (var listView in listViews)
                            {
                                if (!(listView.userData is bool initialized && initialized))
                                {
                                    _listViewTracker.RegisterListView(listView);
                                }
                            }
                        }
                    }
                }
            });

            return hasListViewNodes;
        }

        /// <summary>
        /// 等待ListView初始化完成
        /// </summary>
        private async Task WaitForListViewInitializationAsync(CancellationToken cancellationToken)
        {
            const int maxWaitTime = 5000; // 5秒超时
            const int checkInterval = 50; // 每50ms检查一次
            int elapsedTime = 0;

            while (!_listViewTracker.AllListViewsReady() && elapsedTime < maxWaitTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(checkInterval, cancellationToken);
                elapsedTime += checkInterval;

                if (elapsedTime % 500 == 0) // 每500ms输出一次进度
                {
                    Debug.Log($"等待ListView初始化... 剩余: {_listViewTracker.PendingCount()}个, 已等待: {elapsedTime}ms");
                }
            }

            if (_listViewTracker.AllListViewsReady())
            {
                Debug.Log($"所有ListView初始化完成，总耗时: {elapsedTime}ms");
            }
            else
            {
                Debug.LogWarning($"ListView初始化等待超时 ({maxWaitTime}ms)，强制继续连接创建");
            }
        }

        #endregion
        /// <summary>
        /// ListView连接创建尝试信息
        /// </summary>
        private class ListViewConnectionAttempt
        {
            public ChildPort ChildPort;
            public ViewNode ChildViewNode;
            public ListView ListView;
            public int MaxRetries;
            public int RetryInterval;
            public DateTime StartTime;
            public int CurrentRetry = 0;
        }

        /// <summary>
        /// 调度ListView连接创建 - 智能重试机制
        /// </summary>
        private void ScheduleListViewConnection(ListViewConnectionAttempt attempt)
        {
            void CheckAndCreateConnection()
            {
                try
                {
                    // 检查ListView是否已初始化
                    if (attempt.ListView.userData is bool initialized && initialized)
                    {
                        CreateConnectionImmediately(attempt.ChildPort, attempt.ChildViewNode);
                        var elapsed = (DateTime.Now - attempt.StartTime).TotalMilliseconds;
                        Debug.Log($"ListView延迟连接创建成功 (重试{attempt.CurrentRetry}次, 耗时{elapsed:F0}ms)");
                        return;
                    }

                    // 检查是否超过最大的重试次数
                    if (attempt.CurrentRetry >= attempt.MaxRetries)
                    {
                        var elapsed = (DateTime.Now - attempt.StartTime).TotalMilliseconds;
                        Debug.LogWarning($"ListView连接创建超时: 等待{elapsed:F0}ms后放弃 (重试{attempt.CurrentRetry}次)");
                        return;
                    }

                    // 继续重试
                    attempt.CurrentRetry++;
                    schedule.Execute(CheckAndCreateConnection).ExecuteLater(attempt.RetryInterval);
                }
                catch (Exception e)
                {
                    Debug.LogError($"ListView连接创建过程中发生异常: {e.Message}");
                }
            }

            CheckAndCreateConnection();
        }
        #region ListView初始化公共接口

        /// <summary>
        /// 注册ListView到初始化跟踪器 - 供ListDrawer调用
        /// </summary>
        public void RegisterListViewForTracking(ListView listView)
        {
            _listViewTracker.RegisterListView(listView);
        }

        /// <summary>
        /// 标记ListView为就绪状态 - 供ListDrawer调用
        /// </summary>
        public void MarkListViewAsReady(ListView listView)
        {
            _listViewTracker.MarkListViewReady(listView);
        }

        #endregion
    }
}
