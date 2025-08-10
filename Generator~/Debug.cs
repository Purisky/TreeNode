using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TreeNodeSourceGenerator
{
    public static class Debug
    {
        public static readonly List<string> debugLogs = new List<string>();
        public static void Log(object obj)
        {
            debugLogs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {obj}");
        }
        public static void GenerateDebugFile(GeneratorExecutionContext context)
        {
            if (debugLogs.Count == 0) return;

            var debugContent = string.Join("\n", debugLogs);
            var assemblyName = context.Compilation.Assembly.Name;
            string assemblyName_ = assemblyName.Replace(".", "_").Replace("-", "_");
            var assemblyIdentity = context.Compilation.Assembly.Identity.ToString();
            
            var debugSource = $@"/*
代码生成器调试日志
生成时间: {DateTime.Now}
目标程序集: {assemblyName}
程序集标识: {assemblyIdentity}

{debugContent}
*/

namespace TreeNodeSourceGenerator.Debug
{{
    public static class GeneratorDebugInfo_{assemblyName_}
    {{
        public const string LastRunTime = ""{DateTime.Now}"";
        public const int LogCount = {debugLogs.Count};
        public const string TargetAssembly = ""{assemblyName}"";
        public const string AssemblyIdentity = ""{assemblyIdentity}"";
    }}
}}";

            context.AddSource($"GeneratorDebugInfo_{assemblyName_}.g.cs", SourceText.From(debugSource, Encoding.UTF8));
        }
    }
}