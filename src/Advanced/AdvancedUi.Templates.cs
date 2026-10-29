using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    internal sealed partial class AdvancedUi
    {
        private void RefreshTemplates()
        {
            templateFiles = Directory.GetFiles(displayDirectory, "template-*.xml");
            Array.Sort(templateFiles, StringComparer.OrdinalIgnoreCase);
        }

        private void SaveDisplay(string path, string name)
        {
            string temporary = path + ".tmp";
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = new UTF8Encoding(false);
            using (XmlWriter w = XmlWriter.Create(temporary, settings))
            {
                w.WriteStartElement("GINAdvancedDisplay");
                w.WriteAttributeString("version", "1");
                w.WriteAttributeString("name", name);
                foreach (string key in hidden)
                {
                    w.WriteStartElement("hidden");
                    w.WriteString(key);
                    w.WriteEndElement();
                }

                foreach (string key in collapsed)
                {
                    w.WriteStartElement("collapsed");
                    w.WriteString(key);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private void LoadDisplay(string path)
        {
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.Load(path);
            if (doc.DocumentElement.Name != "GINAdvancedDisplay" || doc.DocumentElement.GetAttribute("version") != "1")
            {
                throw new IOException("Unrecognized display template");
            }

            HashSet<string> nextHidden = new HashSet<string>(StringComparer.Ordinal), nextCollapsed = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode n in doc.DocumentElement.ChildNodes)
            {
                if (n.Name == "hidden")
                {
                    nextHidden.Add(n.InnerText);
                }

                if (n.Name == "collapsed")
                {
                    nextCollapsed.Add(n.InnerText);
                }
            }

            hidden.Clear();
            collapsed.Clear();
            foreach (string s in nextHidden)
            {
                hidden.Add(s);
            }

            foreach (string s in nextCollapsed)
            {
                collapsed.Add(s);
            }
        }

        private static string DisplayName(string path)
        {
            try
            {
                XmlDocument d = new XmlDocument();
                d.XmlResolver = null;
                d.Load(path);
                return d.DocumentElement.GetAttribute("name");
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(path) + " (!)";
            }
        }

        private void DrawTemplates(int id)
        {
            GUI.SetNextControlName("GIN.Adv.Template.Name");
            templateName = GUILayout.TextField(templateName, 128);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save current display"))
            {
                if (String.IsNullOrEmpty(templateName.Trim()))
                {
                    message = "Enter a name";
                }
                else
                {
                    try
                    {
                        string path = Path.Combine(displayDirectory, "template-" + System.Guid.NewGuid().ToString("N") + ".xml");
                        SaveDisplay(path, templateName.Trim());
                        RefreshTemplates();
                        message = "Saved visibility only; values are unchanged";
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                    }
                }
            }

            if (GUILayout.Button("Close", GUILayout.Width(64)))
            {
                templatesOpen = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Hidden rows keep working. Templates store visibility only.");
            templatesScroll = GUILayout.BeginScrollView(templatesScroll);
            foreach (string file in templateFiles)
            {
                if (GUILayout.Button(DisplayName(file)))
                {
                    try
                    {
                        LoadDisplay(file);
                        Changed();
                        message = "Display template applied";
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Label(message);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            string focus = GUI.GetNameOfFocusedControl();
            if (!String.IsNullOrEmpty(focus) && focus.StartsWith("GIN.Adv.", StringComparison.Ordinal))
            {
                textInput = true;
                textFrame = Time.frameCount;
            }
        }
    }
}
