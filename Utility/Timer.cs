using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TreeNode.Utility
{
    public class Timer : IDisposable
    {
        private static Dictionary<string, List<long>> _timings = new();
        private Stopwatch _stopwatch;
        private string _name;

        public Timer(string name)
        {
            _name = name;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            if (!_timings.ContainsKey(_name))
            {
                _timings[_name] = new List<long>();
            }
            _timings[_name].Add(_stopwatch.ElapsedMilliseconds);
        }

        public static string PrintTimings()
        {
            StringBuilder stringBuilder = new();
            foreach (var timing in _timings)
            {
                long total = 0;
                foreach (var time in timing.Value)
                {
                    total += time;
                }
                stringBuilder.AppendLine($"{timing.Key}: {total / timing.Value.Count} ms (average over {timing.Value.Count} runs)");
            }
            return stringBuilder.ToString();
        }
    }
}
