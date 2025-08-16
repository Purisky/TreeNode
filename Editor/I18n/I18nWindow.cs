using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TreeNode.Utility;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class I18nWindow : EditorWindow
    {
        public const string DefaultLanguage = nameof(English);

        [MenuItem(I18n.Editor.Menu.TreeNode + "/" + I18n.ChangeLanguage)]
        public static void ShowWindow()
        {
            I18nWindow wnd = GetWindow<I18nWindow>();
            wnd.titleContent = new GUIContent(I18n.ChangeLanguage);
            wnd.minSize = new Vector2(200, 200);
        }


        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            List<Type> languageTypes = new() { typeof(English) };
            languageTypes.AddRange( AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(n => n.BaseType == typeof(Language)&& n!=typeof(English)));
            List<string> choices = new();


            int index = 0;
            for (int i = 0; i < languageTypes.Count; i++)
            {
                choices.Add(languageTypes[i].GetField("SelfName").GetValue(null).ToString());
                string code = $"TREENODE_{languageTypes[i].Name.ToUpper()}";
                //检测当前宏
                if (PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Contains(code))
                {
                    index = i;
                }
            }
            RadioButtonGroup radioButtonGroup = new RadioButtonGroup(null, choices);
            radioButtonGroup.SetValueWithoutNotify(index);
            root.Add(radioButtonGroup);
            radioButtonGroup.RegisterCallback<ChangeEvent<int>>(evt =>
            {
                if (evt.previousValue == evt.newValue) { return; }
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] define);
                List<Type> types = new (languageTypes);
                define = define.Where(n => !types.Any(m => n == $"TREENODE_{m.Name.ToUpper()}")).ToArray();
                if (evt.newValue != 0)
                {
                    define = define.Append($"TREENODE_{types[evt.newValue].Name.ToUpper()}").ToArray();
                }
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, define);
            });

        }






    }

    public class I18nData
    {
        public string[] Languages;
        public Translation[] Translations;
    }

    public struct Translation
    {
        public string Key;
        public string Detail;
        public string[] Texts;
        public Translation(string key, int LanguageCount)
        {
            Key = key;
            Detail = null;
            Texts = new string[LanguageCount];
        }
        public void Resize(int LanguageCount)
        {
            Array.Resize(ref Texts, LanguageCount);
        }

    }
}
