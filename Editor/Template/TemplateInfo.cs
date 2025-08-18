using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class TemplateInfo : VisualElement
    {
        public TemplateGraphView GraphView;

        public List<TemplateProperty> Data => GraphView.AssetData.Properties;


        public List<TemplateInfoProperty> Properties;
        public VisualElement PropertiesElement;
        public TemplateInfo(TemplateGraphView graphView)
        {
            GraphView = graphView;
            Init();
        }

        public void Init()
        {
            styleSheets.Add(ResourcesUtil.LoadStyleSheet("TemplateInfo"));
            TextField textField = new("Name");
            Add(textField);
            textField.value = GraphView.AssetData.Name;
            Properties = new();
            PropertiesElement = new() { name = "Properties" };
            Add(PropertiesElement);
            for (int i = 0; i < Data.Count; i++)
            {
                Add(Data[i]);
            }
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            textField.RegisterValueChangedCallback(evt =>
            {
                GraphView.AssetData.Name = evt.newValue;
                //GraphView.Window.History.AddStep();
            });
        }

        public void UpdateProperties()
        {
            List<TemplateInfoProperty> delete = new();
            for (int i = 0; i < Properties.Count; i++)
            {
                //Debug.Log(i);
                if (!Properties[i].UpdatePath())
                {
                    delete.Add(Properties[i]);
                }
            }
            for (int i = 0; i < delete.Count; i++)
            {
                Data.Remove(delete[i].TemplateProperty);
                Remove(delete[i]);
            }
        }



        public void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }
        public void Add(TemplateProperty property)
        {
            TemplateInfoProperty templateInfoProperty = new(this, property);
            Properties.Add(templateInfoProperty);
            PropertiesElement.Add(templateInfoProperty);
        }
        public void Add(PropertyElement element)
        {
            TemplateProperty property = element.TemplateProperty;
            property.ID = GetNewID();
            TemplateInfoProperty templateInfoProperty = new(this, property)
            {
                PropertyElement = element,
                ViewNode = element.ViewNode
            };

            List<TemplateInfoProperty> delete = new();
            for (int i = 0; i < Properties.Count; i++)
            {
                TemplateInfoProperty infoProperty = Properties[i];
                if (Check(Properties[i].PropertyElement))
                {
                    delete.Add(Properties[i]);
                }
            }
            for (int i = 0; i < delete.Count; i++)
            {
                //Remove(delete[i]);
                delete[i].PropertyElement.SetOutput(false);
            }
            Properties.Add(templateInfoProperty);
            PropertiesElement.Add(templateInfoProperty);

            bool Check(PropertyElement otherElement )
            {
                return CheckAncestor(element, otherElement) || CheckAncestor(otherElement, element);
            }
        }

        static bool CheckAncestor(PropertyElement a, PropertyElement b)
        {
            while (a != null)
            {
                if (a == b) { return true; }
                a = a.GetFirstAncestorOfType<PropertyElement>();
            }
            return false;
        }






        public string GetNewID()
        {
            int index = 0;
            while(true)
            {
                string id = $"{index:x2}";
                if (Properties.Find(p => p.TemplateProperty.ID == id) == null)
                {
                    return id;
                }
                index++;
            }
        }


        public void Remove(TemplateProperty property)
        {
            TemplateInfoProperty infoProperty = FindProperty(property);
            if (infoProperty != null)
            {
                Properties.Remove(infoProperty);
                PropertiesElement.Remove(infoProperty);
            }
        }
        public void Remove(TemplateInfoProperty property)
        {
            Properties.Remove(property);
            PropertiesElement.Remove(property);
        }

        public TemplateInfoProperty FindProperty(TemplateProperty property)
        {
            return Properties.Find(p => p.TemplateProperty == property);
        }
        public PropertyElement FindPropertyElement(TemplateProperty property)
        {
            (string node, string local) = DividePath(property.Path);
            JsonNode jsonNode = GraphView.AssetData.GetValue<JsonNode>(node);
            ViewNode viewNode = GraphView.NodeDic[jsonNode];
            List<PropertyElement> propertyElements = viewNode.Query<PropertyElement>().ToList();
            for (int i = 0; i < propertyElements.Count; i++)
            {
                if (propertyElements[i].LocalPath == local)
                {
                    return propertyElements[i];
                }
            }
            return null;
        }
        public (string node, string local) DividePath(string global)
        {
            int popCount = global.Split('.', '[').Length;
            string node = global;
            if (popCount <= 1)
            { 
                return (global, "");
            }
            for (int i = 0; i < popCount; i++)
            {
                node = PropertyAccessor.ExtractParentPath(node);
                object value = GraphView.AssetData.GetValue<object>(node);
                if (value is JsonNode)
                { 
                    return (node, global[(node.Length + 1)..]);
                }
            }
            return (global, "");
        }
    }



    public class TemplateInfoProperty : VisualElement
    {
        public TemplateProperty TemplateProperty;
        public TemplateInfo TemplateInfo;
        public ViewNode ViewNode;
        public PropertyElement PropertyElement;

        public TextField PropertyName;
        public TextField PropertyID;
        //bool inited;
        public TemplateInfoProperty(TemplateInfo templateInfo, TemplateProperty property)
        {
            TemplateInfo = templateInfo;
            TemplateProperty = property;
            //Label label = new(property.Name);
            //Add(label);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            RegisterCallback<MouseOverEvent>(OnMouseEnter);
            RegisterCallback<MouseOutEvent>(OnMouseOut);
            PropertyName = new TextField(GetPathName(property.Path));
            PropertyName.style.flexGrow = 1;
            Add(PropertyName);
            PropertyName.value = property.Name;
            PropertyName.RegisterValueChangedCallback(evt =>
            {
                TemplateProperty.Name = evt.newValue;
            });

            PropertyID = new TextField
            {
                name = "PropertyID"
            };
            PropertyID.style.width = 60;
            PropertyID.style.fontSize = 8;
            Add(PropertyID);
            PropertyID.value = property.ID;
            PropertyID.SetEnabled(false);
            PropertyID.RegisterCallback<BlurEvent>((evt) => CommitEdit());
        }

        public void CommitEdit()
        {
            PropertyID.SetEnabled(false);
            if (string.IsNullOrEmpty(PropertyID.value)) { PropertyID.value = TemplateProperty.ID; return; }
            string value = PropertyID.value.Trim();
            if (string.IsNullOrWhiteSpace(value)) { PropertyID.value = TemplateProperty.ID; return; }
            if (value == TemplateProperty.ID) { return; }
            for (int i = 0; i < TemplateInfo.Properties.Count; i++)
            {
                if (TemplateInfo.Properties[i] != this && TemplateInfo.Properties[i].TemplateProperty.ID == value)
                {
                    PropertyID.value = TemplateProperty.ID;
                    return;
                }
            }
            TemplateProperty.ID = value;
            PropertyID.value = value;
        }



        string GetPathName(string path)
        {
            int index = path.LastIndexOf('.');
            if (index < 0)
            {
                return path;
            }
            return path[(index + 1)..];
        }
        public bool UpdatePath()
        {
            if (TemplateInfo.GraphView.ViewNodes.Contains(ViewNode))
            {
                PropertyElement.TemplateProperty.Path = PropertyElement.GetGlobalPath();
                PropertyName.label = GetPathName(TemplateProperty.Path);
                return true;
            }
            return false;
        }
        private void OnMouseOut(MouseOutEvent evt)
        {
            //InitPropertyElement();
            PropertyElement.RemoveFromClassList("TemplatePropertyHover");
        }

        private void OnMouseEnter(MouseOverEvent evt)
        {
            //InitPropertyElement();
            PropertyElement.AddToClassList("TemplatePropertyHover");
        }

        public void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is TemplateInfoProperty property)
            {


                int count = TemplateInfo.Data.Count;
                int index = TemplateInfo.Data.IndexOf(property.TemplateProperty);
                if (count > 1)
                {
                    if (index > 0)
                    {
                        evt.menu.AppendAction($"▲{I18n.Editor.List.Move2Top}", delegate
                         {
                             TemplateInfo.Data.Remove(property.TemplateProperty);
                             TemplateInfo.Data.Insert(0,property.TemplateProperty);
                             TemplateInfo.Properties.Remove(property);
                             TemplateInfo.Properties.Insert(0,property);
                             property.SendToBack();

                         });
                        evt.menu.AppendAction($"▲{I18n.Editor.List.MoveUp}", delegate
                        {
                            int index = TemplateInfo.Data.IndexOf(property.TemplateProperty);
                            (TemplateInfo.Data[index], TemplateInfo.Data[index-1]) = (TemplateInfo.Data[index - 1], TemplateInfo.Data[index]);
                            (TemplateInfo.Properties[index], TemplateInfo.Properties[index - 1]) = (TemplateInfo.Properties[index - 1], TemplateInfo.Properties[index]);
                            TemplateInfo.PropertiesElement.Remove(property);
                            TemplateInfo.PropertiesElement.Insert(index - 1, property);
                        });
                    }
                    if (index < count - 1)
                    {
                        evt.menu.AppendAction($"▼{I18n.Editor.List.MoveDown}", delegate
                        {
                            int index = TemplateInfo.Data.IndexOf(property.TemplateProperty);
                            (TemplateInfo.Data[index], TemplateInfo.Data[index + 1]) = (TemplateInfo.Data[index + 1], TemplateInfo.Data[index]);
                            (TemplateInfo.Properties[index], TemplateInfo.Properties[index + 1]) = (TemplateInfo.Properties[index + 1], TemplateInfo.Properties[index]);
                            TemplateInfo.PropertiesElement.Remove(property);
                            TemplateInfo.PropertiesElement.Insert(index + 1, property);
                        });
                        evt.menu.AppendAction($"▼{I18n.Editor.List.Move2Bottom}", delegate
                        {
                            TemplateInfo.Data.Remove(property.TemplateProperty);
                            TemplateInfo.Data.Add(property.TemplateProperty);
                            TemplateInfo.Properties.Remove(property);
                            TemplateInfo.Properties.Add( property);
                            property.BringToFront();
                        });
                    }
                    evt.menu.AppendSeparator();
                }
                evt.menu.AppendAction($"✎{I18n.Editor.List.SetID}", delegate
                {
                    PropertyID.SetEnabled(true);
                    PropertyID.schedule.Execute(() =>
                    {
                        PropertyID.Focus(); ;
                        PropertyID.SelectAll();
                    }).ExecuteLater(50);

                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction($"✖{I18n.Editor.List.DeleteItem}/{I18n.Editor.Button.Confirm}", delegate
                {
                    PropertyElement.SetOutput(false);
                });
                evt.menu.AppendSeparator();
            }

        }
    }



}
