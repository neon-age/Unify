// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using UnityEditor.Callbacks;
using UnityEditorInternal;
using Unify.UIElementsReflection;

namespace UnityEditor.UIElements.Hijacked
{
    public enum DropdownMenuSearch
    {
        Auto,
        Never,
        Always
    }

    public class DropdownMenuDescriptor
    {
        public bool allowSubmenus { get; set; } = true;
        public bool autoClose { get; set; } = true;
        public bool expansion { get; set; } = true;
        public bool parseShortcuts { get; set; } = true;
        public DropdownMenuSearch search { get; set; } = DropdownMenuSearch.Auto;
        public string title { get; set; } = null;
    }

    public static class EditorMenuExtensions
    {
        const int k_SearchFieldTextLimit = 1024;
        // used for tests
        internal const float k_MaxMenuWidth = 512.0f;
        internal const float k_SubmenuExpandDelay = 0.35f;
        internal const string k_SearchShortcutId = "Main Menu/Edit/Find";

        internal static readonly Rect k_InvalidRect = new(0, 0, -1, -1);

        internal static readonly List<GenericDropdownMenu> s_ActiveMenus = new();
        internal static readonly List<EditorWindow> s_ActiveMenuWindows = new();
        static Delayer s_Delayer = null;
        static Rect s_CachedRect = k_InvalidRect;
        static bool s_AllowDelayedExpansion = true;
        internal static bool s_DebugMode = false;

        internal static readonly string searchUssClassName = GenericDropdownMenu.ussClassName + "__search";
        internal static readonly string searchCategoryUssClassName = GenericDropdownMenu.ussClassName + "__search-category";
        internal static readonly string shortcutUssClassName = GenericDropdownMenu.ussClassName + "__shortcut";

        internal static float maxMenuHeight
        {
            get
            {
                var height = Screen.mainWindowDisplayInfo.height;
                if (height == 0)
                    height = Screen.currentResolution.height;

                return height / EditorGUIUtility.pixelsPerPoint * 0.75f;
            }
        }

        internal static bool isEditorContextMenuActive => s_ActiveMenus.Count > 0;

        internal class ContextMenu : EditorWindow
        {
            const float k_OffsetAllowance = 2;

            const float k_WindowOffset = 9;

            EditorWindow m_ParentWindow;
            Rect m_ParentRect = k_InvalidRect;

            Rect GetAdjustedPosition() => GetAdjustedPosition(m_ParentRect, minSize);

            float m_MaxOuterContainerWidth;

            // This is static so it can be used in test code conveniently
            internal static Rect GetAdjustedPosition(Rect parentRect, Vector2 size, ScrollView scrollView = null)
            {
                var origin = new Vector2(parentRect.xMax, parentRect.y);
                var rect = new Rect(origin, size);
                var windowPos = ContainerWindow.FitRectToScreen(rect, true, true);

                // No height means rect is a point and there is no point for further adjustment
                if (parentRect.height < 1)
                    return windowPos;

                var R_verticalScroller = new R_VisualElement(scrollView?.verticalScroller);

                var scrollerWidth = R_verticalScroller.x == null ? 0 : R_verticalScroller.computedStyle.width.value;
                var displayStyle = R_verticalScroller.x == null ? DisplayStyle.None : R_verticalScroller.computedStyle.display;
                
                var scrollViewWidth =  displayStyle == DisplayStyle.Flex ? (scrollerWidth == 0 ? 0 : scrollerWidth) : 0;

                rect.x += k_WindowOffset + scrollViewWidth;
                windowPos = ContainerWindow.FitRectToScreen(rect, true, true);

                if (windowPos.x < parentRect.xMax + k_WindowOffset + scrollViewWidth - k_OffsetAllowance)
                    rect.x = parentRect.x - windowPos.width - k_WindowOffset;

                if (windowPos.y < parentRect.y)
                    rect.y = parentRect.yMax - windowPos.height;

                windowPos = ContainerWindow.FitRectToScreen(rect, true, true);
                return windowPos;
            }

            void Host(GenericDropdownMenu menu)
            {
                if(!s_DebugMode)
                    m_Parent.AddToAuxWindowList();

                m_Parent.window.m_DontSaveToLayout = true;
                menu.customFocusHandling = true;

                menu.outerContainer.style.paddingRight = 14;
                menu.outerContainer.style.paddingBottom = 14;

                menu.innerContainer.style.maxWidth = k_MaxMenuWidth;
                menu.outerContainer.style.maxHeight = maxMenuHeight;
                menu.outerContainer.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    // Set the outer container and the menu container to the size of the
                    // longest item in the list.
                    if (minSize.x < menu.outerContainer.worldBound.size.x)
                    {
                        m_MaxOuterContainerWidth = menu.outerContainer.worldBound.size.x;
                        menu.menuContainer.style.minWidth = m_MaxOuterContainerWidth;
                    }

                    maxSize = minSize = new Vector2(m_MaxOuterContainerWidth, menu.outerContainer.worldBound.size.y);
                    position = GetAdjustedPosition(m_ParentRect, minSize, menu.m_Parent?.scrollView);
                });

                menu.menuContainer.RegisterCallback<DetachFromPanelEvent>(e =>
                {
                    s_ActiveMenus.Remove(menu);

                    if (s_ActiveMenus.Count == 0)
                        s_CachedRect = k_InvalidRect;
                });
                rootVisualElement.Add(menu.menuContainer);
            }

            public static ContextMenu Show(Rect parent, GenericDropdownMenu menu)
            {
                // Registering active menu early so RecycledTextEditor doesn't end text editing state for input fields while entering context menus
                s_ActiveMenus.Add(menu);

                var menuWindow = CreateInstance<ContextMenu>();

                menuWindow.wantsLessLayoutEvents = true;

                var parentMenu = EditorWindow.focusedWindow as ContextMenu;
                var parentIsMenu = parentMenu != null;
                menuWindow.m_ParentWindow = parentIsMenu ? parentMenu.m_ParentWindow : EditorWindow.focusedWindow;

                // Reset loaded layout so doesn't mess up positioning (Linux specific).
                // Revise once Linux windowing is more robust and predictable.
                menuWindow.position = new Rect(parent.position, Vector2.one * 50);
                menuWindow.ShowPopup();

                menuWindow.m_Parent.window.m_DontSaveToLayout = true;

                var menuPanel = menuWindow.rootVisualElement.panel;
                var imguiContainer = menuPanel.visualTree.Q<IMGUIContainer>();
                imguiContainer?.parent?.Remove(imguiContainer);

                menuWindow.m_ParentRect = parent;
                menuWindow.Host(menu);
                menuWindow.Focus();
                menuWindow.Repaint();

                menu.menuContainer.RegisterCallback<DetachFromPanelEvent>(e =>
                {
                    menu.m_Parent?.innerContainer.Focus();

                    if (s_Shortcuts.ContainsKey(menu))
                        foreach (var shortcut in s_Shortcuts[menu])
                        {
                            shortcut.style.minWidth = StyleKeyword.Null;
                            s_ShortcutPool.Release(shortcut);
                        }

                    s_Shortcuts.Remove(menu);
                    s_MaxShortcutLength.Remove(menu);
                });
                menu.m_OnBeforePerformAction = (submenu, autoClose) =>
                {
                    if (!submenu && autoClose)
                    {
                        // If action is going to close menu, focus on the parent
                        // window to correctly forward possible command events.
                        menuWindow.m_ParentWindow?.Focus();

                        // When closing, menu will shift focus around prompting
                        // cleanup of aux windows. If context menu creates aux window,
                        // without temporary disable of cleanup, aux windows would be
                        // closed as soon as they are created.
                        InternalEditorUtility.RetainAuxWindows();

                        // close menu and all its parents
                        CloseContextMenuAndItsParents(menuWindow);
                    }
                };
                menu.m_OnBack = () =>
                {
                    if (s_ActiveMenuWindows != null && s_ActiveMenuWindows.Count > 1)
                        AuxCleanup(s_ActiveMenuWindows[^2].m_Parent);
                    else
                        CloseAllContextMenus();
                };

                s_ActiveMenuWindows.Add(menuWindow);
                return menuWindow;
            }

            void OnDestroy()
            {
                s_ActiveMenuWindows.Remove(this);
            }
        }

        [DidReloadScripts]
        static void FindAndCloseAllContextMenus()
        {
            EditorWindow lastFocusedWindow = null;

            while (true)
            {
                EditorWindow.FocusWindowIfItsOpen<ContextMenu>();

                if (lastFocusedWindow == EditorWindow.focusedWindow ||
                    EditorWindow.focusedWindow is not ContextMenu)
                    return;

                lastFocusedWindow = EditorWindow.focusedWindow;
                EditorWindow.focusedWindow.Close();
            }
        }

        internal static void CloseAllContextMenus()
        {
            for (int i = s_ActiveMenuWindows.Count - 1; i >= 0; i--)
                s_ActiveMenuWindows[i].m_Parent.window.Close();
        }

        internal static void CloseContextMenuAndItsParents(ContextMenu menuWindow)
        {
            var i = s_ActiveMenuWindows.IndexOf(menuWindow);
            var menu = s_ActiveMenus[i];
            if (menu.m_Parent != null && menu.m_Parent is GenericDropdownMenu parentMenu)
            {
                var j = s_ActiveMenus.IndexOf(parentMenu);
                var parentMenuWindow = s_ActiveMenuWindows[j] as ContextMenu;
                CloseContextMenuAndItsParents(parentMenuWindow);
            }
            menuWindow.Close();
        }

        static void AuxCleanup(GUIView view)
        {
            view?.Focus();

            // Help Aux window chain to clear sibling submenu windows (Mac specific)
            // Also allows for instant menu item action execution
            if (view is not HostView hostView)
                return;

            var index = s_ActiveMenuWindows.IndexOf(hostView.actualView);

            if (s_ActiveMenuWindows.Count <= index)
                return;

            for (int i = s_ActiveMenuWindows.Count - 1; i > index; i--)
                s_ActiveMenuWindows[i].m_Parent.window.Close();
        }

        // DropdownMenu
        public static GenericDropdownMenu PrepareMenu(DropdownMenu menu, EventBase triggerEvent, DropdownMenuDescriptor desc)
        {
            menu.PrepareForDisplay(triggerEvent);

            var genericMenu = new GenericDropdownMenu(desc.allowSubmenus);

            foreach (var item in menu.MenuItems())
            {
                if (item is DropdownMenuAction action)
                {
                    if ((action.status & DropdownMenuAction.Status.Hidden) == DropdownMenuAction.Status.Hidden
                        || action.status == 0)
                    {
                        continue;
                    }

                    var R_action = new R_DropdownMenuAction(action);

                    var isChecked = (action.status & DropdownMenuAction.Status.Checked) == DropdownMenuAction.Status.Checked;

                    if ((action.status & DropdownMenuAction.Status.Disabled) == DropdownMenuAction.Status.Disabled)
                    {
                        if (R_action.content == null)
                            genericMenu.AddDisabledItem(action.name, isChecked); //R_action.icon, R_action.tooltip
                        else
                            genericMenu.AddDisabledItem(action.name, R_action.content);
                    }
                    else
                    {
                        if (R_action.content == null)
                            genericMenu.AddItem(action.name, isChecked, () => action.Execute()); //R_action.icon, R_action.tooltip
                        else
                            genericMenu.AddItem(action.name, R_action.content);
                    }
                }
                else
                {
                    if (item is DropdownMenuSeparator separator)
                    {
                        genericMenu.AddSeparator(separator.subMenuPath);
                    }
                }
            }

            foreach (var item in new R_DropdownMenu(menu).HeaderItems())
            {
                if (item is not DropdownMenuAction action
                    || (action.status & DropdownMenuAction.Status.Hidden) == DropdownMenuAction.Status.Hidden
                    || action.status == 0)
                    continue;

                var isChecked = (action.status & DropdownMenuAction.Status.Checked) == DropdownMenuAction.Status.Checked;

                if ((action.status & DropdownMenuAction.Status.Disabled) != DropdownMenuAction.Status.Disabled)
                    genericMenu.AddItem(action.name, isChecked, () => action.Execute());
                    //genericMenu.AddHeaderItem(action.icon, action.tooltip, isChecked, () => action.Execute());
                else
                    genericMenu.AddDisabledItem(action.name, isChecked);
                    //genericMenu.AddDisabledHeaderItem(action.icon, action.tooltip, isChecked);
                    
            }

            return genericMenu;
        }

        public static void DisplayEditorMenu(this DropdownMenu menu, Rect rect)
        {
            var descriptor = new R_DropdownMenu(menu).m_Descriptor as DropdownMenuDescriptor ?? new DropdownMenuDescriptor();
            var genericMenu = PrepareMenu(menu, null, descriptor);
            genericMenu.DoDisplayGenericDropdownMenu(rect.position + Vector2.up * rect.height, descriptor);
        }

        public static void DisplayEditorMenu(this DropdownMenu menu, EventBase triggerEvent)
        {
            var descriptor = new R_DropdownMenu(menu).m_Descriptor as DropdownMenuDescriptor ?? new DropdownMenuDescriptor();
            var genericMenu = PrepareMenu(menu, triggerEvent, descriptor);

            var position = Vector2.zero;
            if (triggerEvent is IMouseEvent mouseEvent)
            {
                position = mouseEvent.mousePosition;
            }
            else if (triggerEvent is IPointerEvent pointerEvent)
            {
                position = pointerEvent.position;
            }
            else if (new R_EventBase(triggerEvent).elementTarget != null)
            {
                position = new R_EventBase(triggerEvent).elementTarget.layout.center;
            }

            genericMenu.DoDisplayGenericDropdownMenu(position, descriptor);
        }

        public static void SetDescriptor(this DropdownMenu menu, DropdownMenuDescriptor descriptor)
        {
            var R_menu = new R_DropdownMenu(menu);
            R_menu.m_Descriptor = descriptor;
        }

        // GenericDropdownMenu
        static readonly ObjectPool<ToolbarSearchField> s_SearchPool = new(() =>
        {
            var search = new ToolbarSearchField();
            search.textInputField.maxLength = k_SearchFieldTextLimit;

            search.AddToClassList(searchUssClassName);
            // Limit width to avoid stretching the menu too much
            search.RegisterCallback<GeometryChangedEvent>((evt) =>
            {
                if (evt.newRect.width > k_MaxMenuWidth)
                    search.style.width = k_MaxMenuWidth;
            });
            // Reset to default width when leaving the menu.
            search.RegisterCallback<DetachFromPanelEvent>(_ =>
                search.style.width = new StyleLength(StyleKeyword.Initial)
            );
            search.RegisterValueChangedCallback(e =>
            {
                string ClearHighlighting(string text)
                {
                    return Regex.Replace(text, "<.*?>", string.Empty);
                }

                string HighlightText(string text, string query)
                {
                    // Keep in sync with FuzzySearch.cs RichTextFormatter
                    return Regex.Replace(text, query, $"{(EditorGUIUtility.isProSkin ? "<color=#FF6100>" : "<color=#EE4400>")}$&</color>",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

                List<GenericDropdownMenu.MenuItem> GetSearchResults(string query, GenericDropdownMenu.MenuItem root)
                {
                    string GetPath(GenericDropdownMenu.MenuItem item)
                    {
                        var path = item.name;

                        while (!string.IsNullOrEmpty(item.parent?.name))
                        {
                            path = item.parent.name + " / " + path;
                            item = item.parent;
                        }

                        return path;
                    }

                    var results = new List<GenericDropdownMenu.MenuItem>();
                    var itemCount = 0;

                    foreach (var item in root.children)
                    {
                        if (item.isSubmenu || !item.name.ToLowerInvariant().Contains(query))
                            continue;

                        // Add category title
                        if (itemCount == 0 && !string.IsNullOrEmpty(root.name))
                        {
                            var path = GetPath(root);
                            var categoryLabel = new Label(path);
                            categoryLabel.pickingMode = PickingMode.Ignore;
                            categoryLabel.AddToClassList(searchCategoryUssClassName);
                            var categoryItem = new GenericDropdownMenu.MenuItem()
                            {
                                name = path,
                                element = categoryLabel
                            };
                            results.Add(categoryItem);
                        }

                        results.Add(item);
                        itemCount++;
                    }

                    // Add separator at the end
                    if (itemCount > 0)
                        results.Add(new GenericDropdownMenu.MenuItem() { name = "", element = GenericDropdownMenu.CreateSeparator() });

                    foreach (var item in root.children)
                    {
                        if (!item.isSubmenu)
                            continue;

                        foreach (var subItem in GetSearchResults(query, item))
                            results.Add(subItem);
                    }

                    return results;
                }

                GenericDropdownMenu.MenuItem BuildSearchMenu(string query, GenericDropdownMenu.MenuItem root)
                {
                    var searchQuery = query.ToLowerInvariant();
                    var results = GetSearchResults(searchQuery, root);

                    if (results.Count == 0)
                    {
                        var noResultLabel = new Label(string.Format(L10n.Tr("No search results for '{0}'"), query));
                        noResultLabel.pickingMode = PickingMode.Ignore;
                        noResultLabel.AddToClassList(GenericDropdownMenu.labelUssClassName);
                        var noResultItem = new GenericDropdownMenu.MenuItem()
                        {
                            name = "No Results",
                            element = noResultLabel
                        };
                        results.Add(noResultItem);
                    }

                    var searchMenu = new GenericDropdownMenu.MenuItem()
                    {
                        parent = root,
                        children = results,
                    };

                    foreach (var result in searchMenu.children)
                    {
                        var label = result.element.Q<Label>(className: GenericDropdownMenu.labelUssClassName);

                        if (label != null)
                            label.text = HighlightText(label.text, searchQuery);
                    }

                    return searchMenu;
                }

                void ResetHighlighting(GenericDropdownMenu.MenuItem root)
                {
                    foreach (var child in root.children)
                        ResetHighlighting(child);

                    var labels = root.element?.Query<Label>(className: GenericDropdownMenu.labelUssClassName).Build();

                    if (labels == null)
                        return;

                    foreach (var label in labels)
                        label.text = ClearHighlighting(label.text);
                }

                var menu = search.userData as GenericDropdownMenu;
                var newValue = Regex.Replace(e.newValue, "[^\\w ]+", "");
                search.SetValueWithoutNotify(newValue);

                ResetHighlighting(menu.root);

                if (string.IsNullOrWhiteSpace(menu.current.name))
                    menu.NavigateBack(false);

                // Allow whitespace so we can search for spaces too
                if (!string.IsNullOrEmpty(newValue))
                    menu.NavigateTo(BuildSearchMenu(newValue, menu.current));

                // Workaround for getting window content stretching artifacts
                // when resizing to fit search results on Mac.
                menu.menuContainer.MarkDirtyRepaint();
            });
            search.AddManipulator(new KeyboardNavigationManipulator((op, e) =>
            {
                var menu = search.userData as GenericDropdownMenu;

                switch (op)
                {
                    case KeyboardNavigationOperation.Next:
                    case KeyboardNavigationOperation.PageDown:
                    case KeyboardNavigationOperation.Begin:
                    case KeyboardNavigationOperation.Cancel:
                        menu.innerContainer.Focus();
                        menu.Apply(op, e);
                        break;
                }
            }));

            return search;
        }, GenericDropdownMenu.k_OptimizedMenus);
        
        static readonly ObjectPool<Label> s_ShortcutPool = new(() =>
        {
            var shortcut = new Label();
            shortcut.AddToClassList(shortcutUssClassName);
            shortcut.pickingMode = PickingMode.Ignore;
            shortcut.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (new R_EventBase(e).elementTarget.userData is not GenericDropdownMenu menu)
                    return;

                s_MaxShortcutLength.TryAdd(menu, 0f);
                s_MaxShortcutLength[menu] = Mathf.Max(s_MaxShortcutLength[menu], new R_EventBase(e).elementTarget.resolvedStyle.width);

                var shortcutLabels = menu.menuContainer.Query<Label>(className: shortcutUssClassName).Build();
                foreach (var label in shortcutLabels)
                    label.style.minWidth = s_MaxShortcutLength[menu];
            });

            return shortcut;
        }, GenericDropdownMenu.k_OptimizedElements);

        static readonly Dictionary<GenericDropdownMenu, List<Label>> s_Shortcuts = new();
        static readonly Dictionary<GenericDropdownMenu, float> s_MaxShortcutLength = new();

        internal static void AddSearchField(this GenericDropdownMenu menu)
        {
            var search = s_SearchPool.Get();
            search.userData = menu;

            menu.outerContainer.Insert(1, search);
            menu.outerContainer.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                search.value = string.Empty;
                search.userData = null;
                s_SearchPool.Release(search);
            });
            menu.outerContainer.RegisterCallback<KeyDownEvent>(e =>
            {
                var searchKeyCombination = FirstOrDefault(ShortcutManager.instance.GetShortcutBinding(k_SearchShortcutId).keyCombinationSequence);
                var currentKeyCombination = KeyCombination.FromInput(e.imguiEvent);

                if (!currentKeyCombination.Equals(searchKeyCombination))
                    return;

                search.Focus();
                e.StopPropagation();
            });

            menu.onKey += (c, code, mod) =>
            {
                if (c == '\0' || c == '\t')
                    return;

                search.Focus();
                search.SendEvent(KeyDownEvent.GetPooled(c, code, mod));
            };
        }

        internal static void ProcessShortcuts(this GenericDropdownMenu menu, bool parseShortcuts)
        {
            void ParseItem(string item, out string name, out string shortcut)
            {
                name = item;
                shortcut = string.Empty;

                var lastSpace = item.TrimEnd().LastIndexOf(' ');

                if (lastSpace == -1 || !KeyCombination.TryParseMenuItemBindingString(item[(lastSpace + 1)..], out var combination))
                    return;

                name = item[..lastSpace];
                shortcut = combination.ToString();
            }

            void AddShortcutsRecursive(GenericDropdownMenu.MenuItem root)
            {
                foreach (var child in root.children)
                {
                    if (child.isSubmenu)
                        AddShortcutsRecursive(child);

                    if (child.isSubmenu || child.isSeparator || child.isCustomContent)
                        continue;

                    ParseItem(child.name, out var name, out var shortcut);

                    using var elements = child.element.Children().GetEnumerator();
                    elements.MoveNext();
                    elements.MoveNext();

                    if (elements.Current is not Label itemName)
                        continue;

                    child.name = itemName.text = name;

                    if (!parseShortcuts)
                        continue;

                    elements.MoveNext();
                    var appendix = elements.Current;

                    if (appendix == null)
                        continue;

                    var shortcutLabel = s_ShortcutPool.Get();
                    shortcutLabel.text = shortcut;
                    shortcutLabel.userData = menu;

                    if (!s_Shortcuts.ContainsKey(menu) || s_Shortcuts[menu] == null)
                        s_Shortcuts[menu] = new List<Label> { shortcutLabel };
                    else
                        s_Shortcuts[menu].Add(shortcutLabel);

                    appendix.Add(shortcutLabel);
                }
            }

            AddShortcutsRecursive(menu.root);
        }

        internal static void MakeExpandable(this GenericDropdownMenu menu)
        {
            bool IsMenuItemOpened(GenericDropdownMenu.MenuItem item)
            {
                foreach (var activeMenu in s_ActiveMenus)
                {
                    if (activeMenu.root.children.Count > 0 && item.children.Count > 0
                        && item.children[0].guid.Equals(activeMenu.root.children[0].guid))
                    {
                        return true;
                    }
                }

                return false;
            }

            GenericDropdownMenu GetItemMenu(GenericDropdownMenu.MenuItem item)
            {
                foreach (var activeMenu in s_ActiveMenus)
                {
                    foreach (var menuItem in activeMenu.root.children)
                    {
                        if (menuItem == item)
                            return activeMenu;
                    }
                }

                return null;
            }

            // Auto expand submenus after pointer hovering on them. This one is recursive so don't do this while initializing submenus
            if (menu.m_Parent == null)
            {
                menu.ExtendItem(item =>
                {
                    item.element?.RegisterCallback<PointerEnterEvent>(e =>
                    {
                        bool ValidateExpansion()
                        {
                            if (!s_AllowDelayedExpansion || IsMenuItemOpened(item))
                                return false;

                            return true;
                        }

                        s_Delayer?.Dispose();
                        s_Delayer = Delayer.Debounce(o =>
                        {
                            if (item.isSubmenu && !ValidateExpansion())
                                return;

                            if(!s_DebugMode)
                            {
                                // Without this going back two or more submenu levels would fail to preserve auxiliary window chain
                                var view = o as GUIView;
                                AuxCleanup(view);

                                // Focus will have cleared all opened child submenus
                                var hostMenu = GetItemMenu(item);
                                if (hostMenu != null)
                                    hostMenu.m_Child = null;
                                item.element?.parent?.EnableInClassList(GenericDropdownMenu.latentUssClassName, false);
                            }

                            if (item.isSubmenu)
                            {
                                menu.m_OnBeforePerformAction?.Invoke(item.isSubmenu, menu.autoClose);
                                item.PerformAction();
                            }
                        }, TimeSpan.FromSeconds(k_SubmenuExpandDelay));

                        if (!item.isCustomContent)
                            s_CachedRect = GUIUtility.GUIToScreenRect(item.element.worldBound);

                        s_AllowDelayedExpansion = true;
                        s_Delayer.Execute(GUIView.mouseOverView);
                    });
                    item.element?.RegisterCallback<PointerLeaveEvent>(e =>
                    {
                        s_CachedRect = k_InvalidRect;
                        s_Delayer?.Abort();
                    });
                    item.element?.RegisterCallback<PointerDownEvent>(e =>
                    {
                        s_CachedRect = k_InvalidRect;
                        s_Delayer?.Abort();
                    });
                    item.element?.RegisterCallback<DetachFromPanelEvent>(e => s_Delayer?.Abort());
                });
            }

            menu.m_SubmenuOverride = (submenu, parent) =>
            {
                // Close child menu if its submenu is clicked again
                if (parent.m_Child != null && submenu.children == parent.m_Child.root.parent.children)
                {
                    // Transfer back the highlight
                    if (parent?.m_Parent?.m_Parent != null)
                        parent.outerContainer.AddToClassList(GenericDropdownMenu.descendantUssClassName);

                    parent.m_Child.Hide(false, false);
                    parent.m_Child = null;
                    parent.innerContainer.EnableInClassList(GenericDropdownMenu.latentUssClassName, false);
                    return;
                }

                // Close previous menu child if it is open to avoid multiples
                parent.m_Child?.Hide(false, false);
                parent.innerContainer.EnableInClassList(GenericDropdownMenu.latentUssClassName, false);

                var genericMenu = PrepareMenu(submenu, parent);
                genericMenu.DoDisplayGenericDropdownMenu(submenu.element.worldBound, new DropdownMenuDescriptor()
                {
                    search = DropdownMenuSearch.Never,
                    parseShortcuts = false,
                });

                // Highlight the deepest menu
                if (genericMenu?.m_Parent?.m_Parent != null)
                {
                    genericMenu.outerContainer.AddToClassList(GenericDropdownMenu.descendantUssClassName);

                    var parentMenu = genericMenu.m_Parent;
                    while (parentMenu != null)
                    {
                        parentMenu.outerContainer.RemoveFromClassList(GenericDropdownMenu.descendantUssClassName);
                        parentMenu = parentMenu.m_Parent;
                    }
                }

                parent.m_Child = genericMenu;
                parent.innerContainer.EnableInClassList(GenericDropdownMenu.latentUssClassName, parent.customFocusHandling);
                genericMenu?.innerContainer.EnableInClassList(GenericDropdownMenu.latentUssClassName, false);
            };

            // We don't use mouse hover rects to position keyboarding opened submenus
            menu.onKey += (a, b, c) =>
            {
                // Delayed expansion after keyboard interaction reproduces offset by the wrong GUIView very easily not to mention it feels weird.
                s_AllowDelayedExpansion = false;
            };

            menu.onKeyboardNavigationOperation += (op) =>
            {
                if (op == KeyboardNavigationOperation.MoveLeft || op == KeyboardNavigationOperation.Cancel)
                    menu.m_Parent?.innerContainer.EnableInClassList(GenericDropdownMenu.latentUssClassName, false);

                s_CachedRect = k_InvalidRect;
            };
        }

        static GenericDropdownMenu PrepareMenu(GenericDropdownMenu.MenuItem menu, GenericDropdownMenu parent)
        {
            var genericMenu = new GenericDropdownMenu(true);
            genericMenu.m_Parent = parent;
            genericMenu.root.parent = menu;

            foreach (var item in menu.children)
            {
                if(item.isSubmenu)
                {
                    item.action = () =>
                    {
                        if (genericMenu.m_SubmenuOverride != null)
                            genericMenu.m_SubmenuOverride.Invoke(item, genericMenu);
                        else
                            genericMenu.NavigateTo(item);
                    };
                }

                genericMenu.AddItem(item);
            }

            foreach (var item in menu.headerActions)
                genericMenu.AddHeaderItem(item);

            return genericMenu;
        }

        internal static void ApplyDescriptor(this GenericDropdownMenu menu, DropdownMenuDescriptor desc)
        {
            menu.ProcessShortcuts(desc.parseShortcuts);

            switch (desc.search)
            {
                case DropdownMenuSearch.Auto:
                    if (menu.root.children.Any(c => c.children.Count > 0))
                        menu.AddSearchField();
                    break;
                case DropdownMenuSearch.Never:
                    break;
                case DropdownMenuSearch.Always:
                    menu.AddSearchField();
                    break;
                default:
                    var message = string.Format(L10n.Tr("{0}.{1} enumeration value is not implemented."), nameof(DropdownMenuSearch), desc.search);
                    throw new NotImplementedException(message);
            }

            if (desc.expansion)
                menu.MakeExpandable();

            menu.root.name = desc.title;
            menu.allowBackButton = !desc.expansion;
            menu.autoClose = desc.autoClose;
        }

        internal static void DoDisplayGenericDropdownMenu(this GenericDropdownMenu menu, Vector2 position, DropdownMenuDescriptor desc)
        {
            menu.DoDisplayGenericDropdownMenu(new Rect(position, Vector2.zero), desc);
        }

        internal static ContextMenu DoDisplayGenericDropdownMenu(this GenericDropdownMenu menu, Rect parent, DropdownMenuDescriptor desc)
        {
            ApplyDescriptor(menu, desc);

            // Cannot use mouseOverView here because while keyboarding this view could be incorrect and thus close all context menus
            if(!s_DebugMode)
                AuxCleanup(GUIView.current);

            // For delayed actions we cache parent screen coordinates to avoid offsetting by the wrong GUIView
            parent = s_CachedRect == k_InvalidRect ? GUIUtility.GUIToScreenRect(parent) : s_CachedRect;
            return ContextMenu.Show(parent, menu);
        }

        // Avoid Linq
        static bool Any<T>(this List<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source)
            {
                if(predicate(item))
                    return true;
            }

            return false;
        }

        static T FirstOrDefault<T>(IEnumerable<T> source)
        {
            foreach (var item in source)
                return item;

            return default;
        }
    }
}