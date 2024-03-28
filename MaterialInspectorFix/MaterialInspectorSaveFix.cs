#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Reflection;

namespace Unify
{
// https://forum.unity.com/threads/srp-b10-material-inspector-does-not-respond-to-clicks-after-saving-project.1556822/
public class MaterialInspectorSaveFix : AssetModificationProcessor // is there a better callback method for OnSave?
{
    struct InspectorWindowData
    {
        public EditorWindow window;
        public Dictionary<int, float> editorsHeight;

        static MethodInfo OnSelectionChangedMethod;
      
        public VisualElement QueryEditorsList()
        {
            return window.rootVisualElement.Query(className: "unity-inspector-editors-list").First();
        }

        public void InvokeOnSelectionChanged()
        {
            if (OnSelectionChangedMethod == null)
                OnSelectionChangedMethod = window.GetType().GetMethod("OnSelectionChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            OnSelectionChangedMethod.Invoke(window, null);
        }
    }

    static MethodInfo LayoutSetMethod;

    static void SetLayoutProperty(VisualElement element, Rect rect)
    {
        if (LayoutSetMethod == null)
            LayoutSetMethod = typeof(VisualElement).GetProperty("layout", BindingFlags.Public | BindingFlags.Instance).GetSetMethod(true);

        LayoutSetMethod.Invoke(element, new object[] { rect });
    }
    
    static string[] OnWillSaveAssets (string[] paths) 
    {
        var selection = Selection.objects;

        bool hasAnyMaterialSelected = false;
        foreach (var obj in selection)
        {
            if (obj is Material)
            {
                hasAnyMaterialSelected = true;
                break;
            }
        }
        if (!hasAnyMaterialSelected)
            return paths;
        
        var allWindows = Resources.FindObjectsOfTypeAll(typeof(EditorWindow));
        var inspectorWindows = new List<InspectorWindowData>(1);
        foreach (EditorWindow window in allWindows)
        {
            var type = window.GetType();
            if (type.FullName == "UnityEditor.InspectorWindow")
            {
                var data = new InspectorWindowData() { window = window, editorsHeight = new Dictionary<int, float>() };

                foreach (var editor in data.QueryEditorsList().Children())
                {
                    data.editorsHeight.Add(editor.GetHashCode(), editor.layout.height);
                }

                inspectorWindows.Add(data);
            }
        }
        
        // We DO NOT want to mess with Selection, as it'll register garbage Undo.
        //Selection.objects = null;

        //foreach (var inspector in inspectorWindows)
        //    inspector.InvokeOnSelectionChanged();

        //Selection.objects = selection; 

        foreach (var inspector in inspectorWindows)
        {
            inspector.InvokeOnSelectionChanged();

            foreach (var editor in inspector.QueryEditorsList().Children())
            {
                var layout = editor.layout;
                if (inspector.editorsHeight.TryGetValue(editor.GetHashCode(), out var height))
                {
                    // dunno where EditorElement sets the layout, so we'll do it manually
                    layout.height = height;
                    SetLayoutProperty(editor, layout);
                }
            }
        }
        
        return paths;
    }
}
}
#endif