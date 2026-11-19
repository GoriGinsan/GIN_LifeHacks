using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GinTimelineHack
{
    // Optional neighbours: GIN Advance mode life hack, KKPE and ConfigurationManager.
    // None of them is a compile-time reference. Their types are located once and cached,
    // because the focus checks that use them run several times per frame.
    public sealed partial class Plugin
    {
        const float CompanionRetrySeconds = 5f;

        static readonly Dictionary<string, Type> companionTypes = new Dictionary<string, Type>();
        static readonly Dictionary<string, float> companionRetryAt = new Dictionary<string, float>();
        static readonly Dictionary<string, PropertyInfo> advanceProperties = new Dictionary<string, PropertyInfo>();
        static object settingsWindow;
        static PropertyInfo settingsWindowOpen, settingsRebinding;
        static bool settingsSearched;

        // Find a loaded type by full name. A missing type is searched again only every few seconds.
        internal static Type CompanionType(string fullName)
        {
            Type type;
            if (companionTypes.TryGetValue(fullName, out type))
            {
                return type;
            }

            float retry;
            if (companionRetryAt.TryGetValue(fullName, out retry) && Time.unscaledTime < retry)
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    companionTypes[fullName] = type;
                    return type;
                }
            }

            companionRetryAt[fullName] = Time.unscaledTime + CompanionRetrySeconds;
            return null;
        }

        // Static bool property published by GIN Advance mode life hack; null when that plugin is absent.
        static bool? AdvanceProperty(string name)
        {
            Type plugin = CompanionType("GinAdvanceHack.Plugin");
            if (plugin == null)
            {
                return null;
            }

            PropertyInfo property;
            if (!advanceProperties.TryGetValue(name, out property))
            {
                property = plugin.GetProperty(name, StaticFlags);
                advanceProperties[name] = property;
            }

            if (property == null || property.PropertyType != typeof(bool))
            {
                return null;
            }

            return (bool)property.GetValue(null, null);
        }

        // KKPE main window instance, or null when KKPE is absent or not created yet.
        static object KkpeMainWindow()
        {
            Type main = CompanionType("HSPE.MainWindow");
            FieldInfo self = main == null ? null : main.GetField("_self", StaticFlags);
            return self == null ? null : self.GetValue(null);
        }

        static bool KkpeEditing()
        {
            bool? fromAdvance = AdvanceProperty("IsAdvancedVisible");
            if (fromAdvance.HasValue)
            {
                return fromAdvance.Value;
            }

            // Without the companion plugin, read KKPE's own advanced-mode state.
            object main = KkpeMainWindow();
            if (main == null)
            {
                return false;
            }

            FieldInfo target = main.GetType().GetField("_poseTarget", StaticFlags | Flags);
            if (target == null || target.GetValue(main) == null)
            {
                return false;
            }

            Type pose = CompanionType("HSPE.PoseController");
            FieldInfo draw = pose == null ? null : pose.GetField("_drawAdvancedMode", StaticFlags);
            return draw != null && (bool)draw.GetValue(null);
        }

        static bool PointerOverNativeAdvanced()
        {
            if (!KkpeEditing())
            {
                return false;
            }

            object main = KkpeMainWindow();
            FieldInfo rect = main == null ? null : main.GetType().GetField("_advancedModeRect", Flags);
            if (rect == null)
            {
                return false;
            }

            Vector2 pointer = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return ((Rect)rect.GetValue(main)).Contains(pointer);
        }

        // True while the F1 settings window is open or is waiting for a key to rebind.
        internal static bool SettingsOpen()
        {
            if (!settingsSearched)
            {
                settingsSearched = true;
                FindSettingsWindow();
            }

            if (settingsWindow == null)
            {
                return false;
            }

            if (settingsWindowOpen != null && (bool)settingsWindowOpen.GetValue(settingsWindow, null))
            {
                return true;
            }

            return settingsRebinding != null && (bool)settingsRebinding.GetValue(null, null);
        }

        static void FindSettingsWindow()
        {
            foreach (BepInEx.PluginInfo info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Instance.GetType().FullName != "ConfigurationManager.ConfigurationManager")
                {
                    continue;
                }

                Type type = info.Instance.GetType();
                settingsWindow = info.Instance;
                settingsWindowOpen = BoolProperty(type.GetProperty("DisplayingWindow", Flags));
                Type drawer = type.Assembly.GetType("ConfigurationManager.SettingFieldDrawer", false);
                settingsRebinding = drawer == null ? null : BoolProperty(drawer.GetProperty("SettingKeyboardShortcut", StaticFlags));
                return;
            }
        }

        static PropertyInfo BoolProperty(PropertyInfo property)
        {
            return property != null && property.PropertyType == typeof(bool) ? property : null;
        }
    }
}
