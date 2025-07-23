using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class NodePrefabInfo : VisualElement
    {
        public NodePrefabGraphView GraphView;

        public List<PrefabProperty> Data => GraphView.AssetData.Properties;


        public List<NodePrefabInfoProperty> Properties;
        public VisualElement PropertiesElement;
        public NodePrefabInfo(NodePrefabGraphView graphView)
        {
            GraphView = graphView;
            Init();
        }

        public void Init()
        {
            styleSheets.Add(ResourcesUtil.LoadStyleSheet("NodePrefabInfo"));
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
                GraphView.Window.History.AddStep();
            });
        }

        public void UpdateProperties()
        {
            List<NodePrefabInfoProperty> delete = new();
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
                Data.Remove(delete[i].PrefabProperty);
                Remove(delete[i]);
            }
        }



        public void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }
        public void Add(PrefabProperty property)
        {
            NodePrefabInfoProperty nodePrefabInfoProperty = new(this, property);
            Properties.Add(nodePrefabInfoProperty);
            PropertiesElement.Add(nodePrefabInfoProperty);
        }
        public void Add(PropertyElement element)
        {
            PrefabProperty property = element.PrefabProperty;
            property.ID = GetNewID();
            NodePrefabInfoProperty nodePrefabInfoProperty = new(this, property)
            {
                PropertyElement = element,
                ViewNode = element.ViewNode
            };

            List<NodePrefabInfoProperty> delete = new();
            for (int i = 0; i < Properties.Count; i++)
            {
                NodePrefabInfoProperty infoProperty = Properties[i];
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
            Properties.Add(nodePrefabInfoProperty);
            PropertiesElement.Add(nodePrefabInfoProperty);

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
                if (Properties.Find(p => p.PrefabProperty.ID == id) == null)
                {
                    return id;
                }
                index++;
            }
        }


        public void Remove(PrefabProperty property)
        {
            NodePrefabInfoProperty prefabInfoProperty = FindProperty(property);
            if (prefabInfoProperty != null)
            {
                Properties.Remove(prefabInfoProperty);
                PropertiesElement.Remove(prefabInfoProperty);
            }
        }
        public void Remove(NodePrefabInfoProperty property)
        {
            Properties.Remove(property);
            PropertiesElement.Remove(property);
        }

        public NodePrefabInfoProperty FindProperty(PrefabProperty property)
        {
            return Properties.Find(p => p.PrefabProperty == property);
        }
        public PropertyElement FindPropertyElement(PrefabProperty property)
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



    public class NodePrefabInfoProperty : VisualElement
    {
        public PrefabProperty PrefabProperty;
        public NodePrefabInfo NodePrefabInfo;
        public ViewNode ViewNode;
        public PropertyElement PropertyElement;

        public TextField PropertyName;
        public TextField PropertyID;
        //bool inited;
        public NodePrefabInfoProperty(NodePrefabInfo nodePrefabInfo, PrefabProperty property)
        {
            NodePrefabInfo = nodePrefabInfo;
            PrefabProperty = property;
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
                PrefabProperty.Name = evt.newValue;
                NodePrefabInfo.GraphView.Window.History.AddStep();
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
            if (string.IsNullOrEmpty(PropertyID.value)) { PropertyID.value = PrefabProperty.ID; return; }
            string value = PropertyID.value.Trim();
            if (string.IsNullOrWhiteSpace(value)) { PropertyID.value = PrefabProperty.ID; return; }
            if (value == PrefabProperty.ID) { return; }
            for (int i = 0; i < NodePrefabInfo.Properties.Count; i++)
            {
                if (NodePrefabInfo.Properties[i] != this && NodePrefabInfo.Properties[i].PrefabProperty.ID == value)
                {
                    PropertyID.value = PrefabProperty.ID;
                    return;
                }
            }
            PrefabProperty.ID = value;
            PropertyID.value = value;
            NodePrefabInfo.GraphView.Window.History.AddStep();
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
            if (NodePrefabInfo.GraphView.ViewNodes.Contains(ViewNode))
            {
                PropertyElement.PrefabProperty.Path = PropertyElement.GetGlobalPath();
                PropertyName.label = GetPathName(PrefabProperty.Path);
                return true;
            }
            return false;
        }
        private void OnMouseOut(MouseOutEvent evt)
        {
            //InitPropertyElement();
            PropertyElement.RemoveFromClassList("PrefabPropertyHover");
        }

        private void OnMouseEnter(MouseOverEvent evt)
        {
            //InitPropertyElement();
            PropertyElement.AddToClassList("PrefabPropertyHover");
        }

        public void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is NodePrefabInfoProperty property)
            {


                int count = NodePrefabInfo.Data.Count;
                int index = NodePrefabInfo.Data.IndexOf(property.PrefabProperty);
                if (count > 1)
                {
                    if (index > 0)
                    {
                        evt.menu.AppendAction($"▲{I18n.Move2Top}", delegate
                         {
                             NodePrefabInfo.Data.Remove(property.PrefabProperty);
                             NodePrefabInfo.Data.Insert(0,property.PrefabProperty);
                             NodePrefabInfo.Properties.Remove(property);
                             NodePrefabInfo.Properties.Insert(0,property);
                             property.SendToBack();
                             NodePrefabInfo.GraphView.Window.History.AddStep();

                         });
                        evt.menu.AppendAction($"▲{I18n.MoveUp}", delegate
                        {
                            int index = NodePrefabInfo.Data.IndexOf(property.PrefabProperty);
                            (NodePrefabInfo.Data[index], NodePrefabInfo.Data[index-1]) = (NodePrefabInfo.Data[index - 1], NodePrefabInfo.Data[index]);
                            (NodePrefabInfo.Properties[index], NodePrefabInfo.Properties[index - 1]) = (NodePrefabInfo.Properties[index - 1], NodePrefabInfo.Properties[index]);
                            NodePrefabInfo.PropertiesElement.Remove(property);
                            NodePrefabInfo.PropertiesElement.Insert(index - 1, property);
                            NodePrefabInfo.GraphView.Window.History.AddStep();
                        });
                    }
                    if (index < count - 1)
                    {
                        evt.menu.AppendAction($"▼{I18n.MoveDown}", delegate
                        {
                            int index = NodePrefabInfo.Data.IndexOf(property.PrefabProperty);
                            (NodePrefabInfo.Data[index], NodePrefabInfo.Data[index + 1]) = (NodePrefabInfo.Data[index + 1], NodePrefabInfo.Data[index]);
                            (NodePrefabInfo.Properties[index], NodePrefabInfo.Properties[index + 1]) = (NodePrefabInfo.Properties[index + 1], NodePrefabInfo.Properties[index]);
                            NodePrefabInfo.PropertiesElement.Remove(property);
                            NodePrefabInfo.PropertiesElement.Insert(index + 1, property);
                            NodePrefabInfo.GraphView.Window.History.AddStep();
                        });
                        evt.menu.AppendAction($"▼{I18n.Move2Bottom}", delegate
                        {
                            NodePrefabInfo.Data.Remove(property.PrefabProperty);
                            NodePrefabInfo.Data.Add(property.PrefabProperty);
                            NodePrefabInfo.Properties.Remove(property);
                            NodePrefabInfo.Properties.Add( property);
                            property.BringToFront();
                            NodePrefabInfo.GraphView.Window.History.AddStep();
                        });
                    }
                    evt.menu.AppendSeparator();
                }
                evt.menu.AppendAction($"✎{I18n.SetID}", delegate
                {
                    PropertyID.SetEnabled(true);
                    PropertyID.schedule.Execute(() =>
                    {
                        PropertyID.Focus(); ;
                        PropertyID.SelectAll();
                    }).ExecuteLater(50);

                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction($"✖{I18n.DeleteItem}/{I18n.Confirm}", delegate
                {
                    PropertyElement.SetOutput(false);
                    NodePrefabInfo.GraphView.Window.History.AddStep();
                });
                evt.menu.AppendSeparator();
            }

        }
    }



}
