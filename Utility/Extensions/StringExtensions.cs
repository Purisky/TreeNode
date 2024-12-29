using System.Collections.Generic;

namespace TreeNode.Utility
{
    public static class StringExtensions
    {
        public static List<string> SplitRich(this string str, char sep)
        {
            List<string> list = new();
            int stack = 0;
            int index = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '<') {
                    stack++; }
                if (str[i] == '>') {
                    stack--; 
                }
                if (stack != 0) { continue; }
                if (str[i] == sep)
                {
                    list.Add(str[index..i]);
                    index = i + 1;
                }
            }
            if (index != str.Length - 1)
            {
                list.Add(str[index..]);
            }
            return list;
        }
    }
}
