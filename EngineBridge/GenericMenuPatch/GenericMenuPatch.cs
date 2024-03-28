#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;
using HarmonyLib;
using System;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.UIElements.Hijacked;
using System.Collections;
using System.Collections.Generic;
using Unify.UIElementsReflection;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using GenericDropdownMenu = UnityEditor.UIElements.Hijacked.GenericDropdownMenu;
using UnityEditor.Actions;
using UnityEditor.SceneManagement;
using System.Linq;

namespace Unify
{
// https://forum.unity.com/threads/new-contextual-menu-is-not-easy-to-read.1495076
public static class GenericMenuPatch
{
    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        var harmony = new Harmony(nameof(GenericMenuPatch));

        var R_DropDown = typeof(GenericMenu).GetMethod("DropDown", BindingFlags.Public | BindingFlags.Instance);
        var R_ObjectContextDropDown = typeof(GenericMenu).GetMethod("ObjectContextDropDown", BindingFlags.NonPublic | BindingFlags.Instance);
        var R_DisplayPopupMenu = typeof(EditorUtility).GetMethod("DisplayPopupMenu", BindingFlags.Public | BindingFlags.Static);

        var R_DisplayObjectContextMenu = typeof(EditorUtility).GetMethod("DisplayObjectContextMenu", BindingFlags.NonPublic | BindingFlags.Static,
            null, new Type[] { typeof(Rect), typeof(Object[]), typeof(int) }, null); 
            
        var R_DisplayCustomMenuWithSeparators = typeof(EditorUtility).GetMethod("DisplayCustomMenuWithSeparators", BindingFlags.NonPublic | BindingFlags.Static,
            null, new Type[] { typeof(Rect), typeof(string[]), typeof(bool[]), typeof(bool[]), typeof(int[]), 
            typeof(EditorUtility.SelectMenuItemFunction), typeof(object), typeof(bool), typeof(bool), typeof(bool) }, null);

        //harmony.Patch(R_DropDown, prefix: GetPatch(nameof(_DropDown)) );
        harmony.Patch(R_ObjectContextDropDown, prefix: GetPatch(nameof(_ObjectContextDropDown)) );
        harmony.Patch(R_DisplayPopupMenu, prefix: GetPatch(nameof(_DisplayPopupMenu)) );
        harmony.Patch(R_DisplayCustomMenuWithSeparators, prefix: GetPatch(nameof(_DisplayCustomMenuWithSeparators)) );
    }

    static HarmonyMethod GetPatch(string methodName)
    {
        return new HarmonyMethod(typeof(GenericMenuPatch).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static));
    }

    static bool _DropDown(GenericMenu __instance, Rect position)
    {
        if (position.position != Event.current.mousePosition)
        {
            position.x -= 10;
        }
        var dropdownMenu = ConvertGenericMenuToDropdownMenu(__instance);
        ShowAsGenericDropdownMenu(dropdownMenu, position, null);
        return false;
    }

     static bool _DisplayPopupMenu(Rect position, string menuItemPath, MenuCommand command)
    {
        //Debug.Log((position, menuItemPath, command));
        var dropdownMenu = new DropdownMenu();

        menuItemPath = menuItemPath.Replace("Assets/Create", "Assets/Create/");

        FillDropdownMenu(dropdownMenu, menuItemPath); //"Assets/"
        //var root = EditorWindow.focusedWindow.baseRootVisualElement;
        ShowAsGenericDropdownMenu(dropdownMenu, position, null);
        return false;
    }

    static bool _DisplayCustomMenuWithSeparators(Rect position, string[] options, bool[] enabled, bool[] separator, int[] selected,
    EditorUtility.SelectMenuItemFunction callback, object userData, bool showHotkey, bool allowDisplayNames, bool shouldDiscardMenuOnSecondClick = false)
    {
        //Vector2 temp = GUIUtility.GUIToScreenPoint(new Vector2(position.x, position.y));
        //position.x = temp.x;
        //position.y = temp.y;
        var menu = new GenericMenu();
        for (int i = 0; i < options.Length; i++)
        {
            var path = options[i];
            var isSelected = selected?.Contains(i) ?? false;
            var isSeparator = separator[i];
            var isEnabled = enabled[i];

            var idx = i;

            if (isSeparator)
                menu.AddSeparator(path);
            else
            {
                if (isEnabled)
                    menu.AddItem(new GUIContent(options[i]), isSelected, () => callback?.Invoke(userData, options, idx));
                else
                    menu.AddDisabledItem(new GUIContent(options[i]), isSelected);
            }
        }

        position.width = 0;
        position.yMin = position.yMax;
        //position = new Rect(Event.current.mousePosition, Vector2.zero);

        var dropdownMenu = ConvertGenericMenuToDropdownMenu(menu);
        ShowAsGenericDropdownMenu(dropdownMenu, position, null);
        return false;
    }
    static bool _ObjectContextDropDown(GenericMenu __instance, Rect position, Object[] context, int contextUserData)
    {
        position = new Rect(Event.current.mousePosition, Vector2.zero);
        var dropdownMenu = ConvertGenericMenuToDropdownMenu(__instance);
        
        foreach (var t in GetInheritanceHierarchy(context[0].GetType()))
        {
            ContextMenuUtility.AddMenuItemsForType(dropdownMenu, t, context);
        }
        ShowAsGenericDropdownMenu(dropdownMenu, position, null);
        return false;
    }

    static IEnumerable<Type> GetInheritanceHierarchy(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
            yield return current;
    }

    static void ShowAsGenericDropdownMenu(DropdownMenu dropdownMenu, Rect position, VisualElement root)
    {
        var genericDropdownMenu = ConvertDropdownMenuToGenericDropdownMenu(dropdownMenu);
        //var genericDropdownMenu = new GenericDropdownMenu(true);
        //EditorMenuExtensions.DisplayEditorMenu(dropdownMenu, position);
        ShowGenericMenu(genericDropdownMenu, position, root, true, true);
    }

    static void FillDropdownMenu(DropdownMenu dropdownMenu, string menuItemPath)
    {
        var items = Menu.GetMenuItems(menuItemPath, true, true);
        var context = Selection.objects;

        int lastItemPriority = -1;
        foreach (var m in items)
        {
            var path = m.path;

            var pathWithoutRoot = path.Replace(menuItemPath, "");

            // Add separator between items with priority differing by over 10 points
            if (lastItemPriority != -1 && m.priority > lastItemPriority + 10)
                dropdownMenu.AppendSeparator(pathWithoutRoot.Substring(0, pathWithoutRoot.LastIndexOf('/') + 1));
            lastItemPriority = m.priority;

            if (m.isSeparator)
            {
                //dropdownMenu.AppendSeparator(pathWithoutRoot);
            }
            else
            {
                var enabled = Menu.GetEnabledWithContext(path, context);
                //Debug.Log((path, enabled));

                //var contextArray = ToArray(context);
                string shortcut = Menu.GetHotkey(path);
                path = path + (string.IsNullOrEmpty(shortcut) ? "" : " " + shortcut);

                AddAction(dropdownMenu, pathWithoutRoot, () => ExecuteMenuItem(context, path), enabled);
                //genericMenu.AddItem(pathWithoutRoot, false, () => ExecuteMenuItem(Selection.objects, path));
            }
        }
    }

    static readonly Dictionary<DropdownMenuAction.Status, Func<DropdownMenuAction, DropdownMenuAction.Status>> s_StatusCallbacks = new()
    {
        { DropdownMenuAction.Status.Normal, DropdownMenuAction.AlwaysEnabled },
        { DropdownMenuAction.Status.Disabled, DropdownMenuAction.AlwaysDisabled },
        { DropdownMenuAction.Status.Checked, a => DropdownMenuAction.Status.Checked },
        { DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Disabled, a => DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Disabled },
        { DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Normal, a => DropdownMenuAction.Status.Checked | DropdownMenuAction.Status.Normal },
    };


    static Func<DropdownMenuAction, DropdownMenuAction.Status> MenuItemToActionStatusCallback(GenericMenu.MenuItem menuItem)
    {
        var status = DropdownMenuAction.Status.None;

        if (menuItem.func != null || menuItem.func2 != null)
        {
            status |= DropdownMenuAction.Status.Normal;
        }
        else
        {
            status |= DropdownMenuAction.Status.Disabled;
        }

        if (menuItem.on)
            status |= DropdownMenuAction.Status.Checked;

        // Cached callbacks
        if (!s_StatusCallbacks.TryGetValue(status, out var callback))
        {
            callback = action => status;
            s_StatusCallbacks[status] = callback;
        }

        return callback;
    }
    

    static DropdownMenu ConvertGenericMenuToDropdownMenu(GenericMenu menu)
    {
        var dropdownMenu = new DropdownMenu();

        foreach (var menuItem in menu.menuItems)
        {
            if (menuItem.separator)
            {
                dropdownMenu.AppendSeparator(menuItem.content.text);
            }
            else
            {
                var statusCallback = MenuItemToActionStatusCallback(menuItem);
                //Debug.Log((menuItem.content.text, menuItem.func != null, menuItem.func2 != null));

                if (menuItem.func != null)
                {
                    dropdownMenu.AppendAction(menuItem.content.text, _ => menuItem.func(), statusCallback, menuItem.userData);
                }
                else if (menuItem.func2 != null)
                {
                    dropdownMenu.AppendAction(menuItem.content.text, action => menuItem.func2(action.userData), statusCallback, menuItem.userData);
                }
                else
                {
                    dropdownMenu.AppendAction(menuItem.content.text, null, statusCallback, menuItem.userData);
                }
            }
        }

        return dropdownMenu;
    }

    static GenericDropdownMenu ConvertDropdownMenuToGenericDropdownMenu(DropdownMenu menu)
    {
        var dropdownMenu = new GenericDropdownMenu(true);

        foreach (var menuItem in menu.MenuItems())
        {
            if (menuItem is DropdownMenuSeparator menuSeparator)
            {
                dropdownMenu.AddSeparator(menuSeparator.subMenuPath);
            }
            if (menuItem is DropdownMenuAction menuAction)
            {
                //var statusCallback = MenuItemToActionStatusCallback(action.name);
                var R_menuAction = new R_DropdownMenuAction(menuAction);

                menuAction.UpdateActionStatus(null);
                var status = menuAction.status;
                var isChecked = (status & DropdownMenuAction.Status.Checked) != 0;
                var isDisabled = (status & DropdownMenuAction.Status.Disabled) != 0;

                if ((status & DropdownMenuAction.Status.Hidden) != 0)
                    continue;

                if (R_menuAction.actionCallback != null) 
                {
                    if (isDisabled)
                        dropdownMenu.AddDisabledItem(menuAction.name, isChecked);
                    else
                        dropdownMenu.AddItem(menuAction.name, isChecked, () => menuAction.Execute());
                }
                else if (menuAction.userData != null)
                {
                    // userData is stored inside item's VisualElement
                    dropdownMenu.AddItem(menuAction.name, isChecked, action => menuAction.Execute(), data: menuAction.userData);
                }
                else
                {
                    //if (isDisabled)
                    dropdownMenu.AddDisabledItem(menuAction.name, isChecked);
                    //else
                    //    dropdownMenu.AddItem(menuAction.name, isChecked, null);
                }
            }
        }

        return dropdownMenu;
    }

    static void AddMenuItems(DropdownMenu menu, string componentName, ScriptingMenuItem[] items, IEnumerable<Object> targets, string submenu)
    {
        string context = $"CONTEXT/{componentName}/";
        if (!string.IsNullOrEmpty(submenu) && submenu[^1] != '/')
            submenu += '/';

        int lastItemPriority = -1;
        foreach (var menuItem in items)
        {
            var menuPath = menuItem.path;
            var newPath = $"{submenu}{menuPath.Substring(context.Length)}";

            // Add separator between items with priority differing by over 10 points
            if (lastItemPriority != -1 && menuItem.priority > lastItemPriority + 10)
                menu.AppendSeparator(newPath.Substring(0, newPath.LastIndexOf('/') + 1));
            lastItemPriority = menuItem.priority;

            //if (!menuItem.isSeparator)
                AddMenuItemWithContext(menu, targets, menuPath, newPath);
        }
    }

    public static void AddMenuItemWithContext(DropdownMenu menu, IEnumerable<Object> context, string menuItemPath, string contextMenuPath = "")
    {
        var contextArray = ToArray(context);
        var enabled = Menu.GetEnabledWithContext(menuItemPath, contextArray);
        AddMenuItemWithContext(menu, context, enabled, menuItemPath, contextMenuPath);
    }

    internal static void AddMenuItemWithContext(DropdownMenu menu, IEnumerable<Object> context, bool enabled, string menuItemPath, string contextMenuPath = "")
    {
        var contextArray = ToArray(context);
        string shortcut = Menu.GetHotkey(menuItemPath);
        string path = (string.IsNullOrEmpty(contextMenuPath) ? menuItemPath : contextMenuPath)
            + (string.IsNullOrEmpty(shortcut) ? "" : " " + shortcut);

        AddAction(menu, path, () => ExecuteMenuItem(contextArray, menuItemPath), enabled);
    }

    static void AddAction(DropdownMenu menu, string path, Action action, bool active = true, Texture2D icon = null, string tooltip = "")
    {
        menu.AppendAction(path, (item) => action?.Invoke(),
            statusAction =>
            {
                //statusAction.tooltip = tooltip;
                return active ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            },
            null); //icon
    }

    static void ExecuteMenuItem(Object[] context, string menuItemPath)
    {
        // Claudia Antoun, 05-16-23, can safely be removed when UW-153 is fixed.
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.Focus();

        EditorApplication.ExecuteMenuItemWithTemporaryContext(menuItemPath, context);
    }

    static T[] ToArray<T>(IEnumerable<T> enumerable) where T : Object
    {
        if (enumerable == null)
            return null;

        if (enumerable is T[] arr)
            return arr;

        var size = 0;
        foreach (var item in enumerable)
            size++;

        T[] items = new T[size];
        var index = 0;
        foreach (var item in enumerable)
        {
            items[index] = item;
            index++;
        }

        return items;
    }
    
    private static void ShowGenericMenu(GenericDropdownMenu menu, Rect position, VisualElement target, bool parseShortcuts, bool autoClose)
    {
        target = EditorWindow.focusedWindow?.rootVisualElement;

        var genericDropdownMenu = menu as GenericDropdownMenu;

        var contextMenu = genericDropdownMenu.DoDisplayGenericDropdownMenu(position, new DropdownMenuDescriptor()
        {
            search = DropdownMenuSearch.Auto,
            parseShortcuts = parseShortcuts,
            autoClose = autoClose
        });

        if(target != null)
        {
            contextMenu.rootVisualElement.styleSheets.Clear();
            InheritStyleSheets(contextMenu.rootVisualElement, target);
        }
    }

    const string k_BuilderCanvas = "Unity.UI.Builder.BuilderCanvas";

    static void InheritStyleSheets(VisualElement receiver, VisualElement parent)
    {
        if (receiver == null || parent == null)
            return;

        do
        {
            for (int i = 0; i < parent.styleSheets.count; i++)
                receiver.styleSheets.Add(parent.styleSheets[i]);

            parent = parent.parent;
        } while (parent != null && parent.GetType().FullName != k_BuilderCanvas);
    }
}
}
#endif
