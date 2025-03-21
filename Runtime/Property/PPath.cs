using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
{
    public struct PPath
    {
        public PathPart[] Paths;
        public PPath(string path)
        {
            var parts = new List<PathPart>();
            var matches = Regex.Matches(path, @"([a-zA-Z_]\w*)|(\[\d+\])");

            foreach (Match match in matches)
            {
                if (match.Value.StartsWith("["))
                {
                    parts.Add(new(int.Parse(match.Value.Trim('[', ']'))));
                }
                else
                {
                    parts.Add(new(match.Value));
                }
            }
            Paths = parts.ToArray();
        }
        public PPath(params PathPart[] paths)
        {
            Paths = new PathPart[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                Paths[i] = paths[i];
            }
        }
        public bool Valid=> Paths.Length > 0;
        public readonly PPath Pop(out PathPart last)
        {
            last = default;
            int length = Paths.Length;
            if (length == 0)
            {
                return this;
            }

            var lastPath = Paths[length - 1];
            if (lastPath.IsIndex)
            {
                last = lastPath;
                return new PPath(Paths.Take(length - 1).ToArray());
            }
            else
            {
                string path = lastPath.Path;
                int lastDotIndex = path.LastIndexOf('.');
                if (lastDotIndex != -1)
                {
                    last = new PathPart(path[lastDotIndex..]);
                    path = path[..lastDotIndex];
                    var newPaths = new PathPart[length];
                    Array.Copy(Paths, newPaths, length);
                    newPaths[length - 1] = new PathPart(path);
                    return new PPath(newPaths);
                }
                else
                {
                    last = lastPath;
                    return new PPath(Paths.Take(length - 1).ToArray());
                }
            }
        }

        public override string ToString()
        {
            return string.Join("", Paths.Select(p => p.IsIndex ? $"[{p.Index}]" : $".{p.Path}" ))[1..];
        }
        public static implicit operator PPath(string path)
        {
            return new PPath(path);
        }
    }
    public struct PathPart
    {
        public string Path;
        public int Index;

        public PathPart(int index)
        {
            Index = index;
            Path = null;
        }
        public PathPart(string path)
        {
            Index = 0;
            Path = path;
        }

        public readonly bool IsIndex => Path == null;

        public override string ToString()
        {
            return IsIndex ? $"[{Index}]" : $"{Path}";
        }
    }
}
