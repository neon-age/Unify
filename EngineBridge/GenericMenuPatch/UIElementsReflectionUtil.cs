using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unify.UIElementsReflection
{
    internal class ObjectPool<T> where T : new()
    {
        private readonly Stack<T> m_Stack = new Stack<T>();

        private int m_MaxSize;

        internal Func<T> CreateFunc;

        public int maxSize
        {
            get
            {
                return m_MaxSize;
            }
            set
            {
                m_MaxSize = Math.Max(0, value);
                while (Size() > m_MaxSize)
                {
                    Get();
                }
            }
        }

        public ObjectPool(Func<T> CreateFunc, int maxSize = 100)
        {
            this.maxSize = maxSize;
            if (CreateFunc == null)
            {
                this.CreateFunc = () => new T();
            }
            else
            {
                this.CreateFunc = CreateFunc;
            }
        }

        public int Size()
        {
            return m_Stack.Count;
        }

        public void Clear()
        {
            m_Stack.Clear();
        }

        public T Get()
        {
            return (m_Stack.Count == 0) ? CreateFunc() : m_Stack.Pop();
        }

        public void Release(T element)
        {
            if (m_Stack.Count > 0 && (object)m_Stack.Peek() == (object)element)
            {
                Debug.LogError("Internal error. Trying to destroy object that is already released to pool.");
            }

            if (m_Stack.Count < maxSize)
            {
                m_Stack.Push(element);
            }
        }
    }
    
    public struct R_DropdownMenu
    {
        public static Type Type = R_VisualElement.GetUIElementsType("DropdownMenu");
        public DropdownMenu x; public R_DropdownMenu(DropdownMenu x) => this.x = x;

        static FieldInfo R_m_Descriptor = Type.GetField("m_Descriptor", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo R_HeaderItems = Type.GetMethod("HeaderItems", BindingFlags.NonPublic | BindingFlags.Instance);

        public object m_Descriptor 
        {
            get => R_m_Descriptor.GetValue(x);
            set => R_m_Descriptor.SetValue(x, value);
        }
        public List<DropdownMenuItem> HeaderItems() => (List<DropdownMenuItem>)R_HeaderItems.Invoke(x, null);
    }

    public struct R_DropdownMenuAction
    {
        public static Type Type = R_VisualElement.GetUIElementsType("DropdownMenuAction");
        public DropdownMenuAction x; public R_DropdownMenuAction(DropdownMenuAction x) => this.x = x;

        static PropertyInfo R_content = Type.GetProperty("content", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo R_actionCallback = Type.GetField("actionCallback", BindingFlags.NonPublic | BindingFlags.Instance);

        public VisualElement content => (VisualElement)R_content.GetValue(x);
        public Action<DropdownMenuAction> actionCallback => (Action<DropdownMenuAction>)R_actionCallback.GetValue(x);
    }

    [Flags]
    public enum PseudoStates
    {
        Active = 1,
        Hover = 2,
        Checked = 8,
        Disabled = 0x20,
        Focus = 0x40,
        Root = 0x80
    }

    public struct R_VisualElement
    {
        public static Assembly Asm = typeof(VisualElement).Assembly;
        public static Type Type = typeof(VisualElement);
        public static Type GetUIElementsType(string type) => Asm.GetType($"UnityEngine.UIElements.{type}");

        public VisualElement x; public R_VisualElement(VisualElement x) => this.x = x;

        static object[] Args1 = new object[1];
        public static object[] GetArgs1(object arg1)
        {
            Args1[0] = arg1;
            return Args1;
        }

        public static Type PseudoStatesType = GetUIElementsType("PseudoStates");

        static FieldInfo R_m_CallbackRegistry = Type.GetField("m_CallbackRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
        static PropertyInfo R_pseudoStates = Type.GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo R_computedStyle = Type.GetField("m_Style", BindingFlags.NonPublic | BindingFlags.Instance);
        static MethodInfo R_GetRootVisualContainer = Type.GetMethod("GetRootVisualContainer", BindingFlags.NonPublic | BindingFlags.Instance);

        public PseudoStates pseudoStates
        {
            get => (PseudoStates)R_pseudoStates.GetValue(x);
            set => R_pseudoStates.SetValue(x, Enum.ToObject(PseudoStatesType, value));
        }

        public R_ComputedStyle computedStyle // this struct is used by ref, so you must always use set after changes are done
        {
            get => new(R_computedStyle.GetValue(x));
            set => R_computedStyle.SetValue(x, value.x);
        } 

        public VisualElement GetRootVisualContainer() => (VisualElement)R_GetRootVisualContainer.Invoke(x, null);

        public R_EventCallbackRegistry m_CallbackRegistry => new(R_m_CallbackRegistry.GetValue(x));
    }

    public struct R_ComputedStyle
    {
        public static Type Type = R_VisualElement.GetUIElementsType("ComputedStyle");
        public object x; public R_ComputedStyle(object x) => this.x = x;

        static PropertyInfo R_width => Type.GetProperty("width");
        static PropertyInfo R_fontSize => Type.GetProperty("fontSize");
        static PropertyInfo R_unityFont => Type.GetProperty("unityFont");
        static PropertyInfo R_unityFontDefinition => Type.GetProperty("unityFontDefinition");
        static PropertyInfo R_display => Type.GetProperty("display");

        public Length width
        {
            get => (Length)R_width.GetValue(x);
            set => R_width.SetValue(x, value);
        }
        public Length fontSize
        {
            get => (Length)R_fontSize.GetValue(x);
            set => R_fontSize.SetValue(x, value);
        }
        public Font unityFont
        {
            get => (Font)R_unityFont.GetValue(x);
            set => R_unityFont.SetValue(x, value);
        } 
        public FontDefinition unityFontDefinition
        {
            get => (FontDefinition)R_unityFontDefinition.GetValue(x);
            set => R_unityFontDefinition.SetValue(x, value);
        } 
        public DisplayStyle display
        {
            get => (DisplayStyle)R_display.GetValue(x);
            set => R_display.SetValue(x, value);
        }
    }

    public struct R_EventCallbackList
    {
        public static Type Type = R_VisualElement.GetUIElementsType("EventCallbackList");
        public object x; public R_EventCallbackList(object x) => this.x = x;

        static MethodInfo R_Clear = Type.GetMethod("Clear");

        public void Clear() => R_Clear.Invoke(x, null);
    }

    public struct R_EventCallbackRegistry //EventCallbackRegistry
    {
        public static Type Type = R_VisualElement.GetUIElementsType("EventCallbackRegistry");
        public object x; public R_EventCallbackRegistry(object x) => this.x = x;
        
        public struct R_DynamicCallbackList
        {
            public static Type Type = R_EventCallbackRegistry.Type.GetNestedType("DynamicCallbackList", BindingFlags.NonPublic);
            public object x; public R_DynamicCallbackList(object x) => this.x = x;

            static MethodInfo R_GetCallbackListForWriting = Type.GetMethod("GetCallbackListForWriting", BindingFlags.Public | BindingFlags.Instance);
        
            public R_EventCallbackList GetCallbackListForWriting() 
            {
                return new(R_GetCallbackListForWriting.Invoke(x, null));
            }
        }

        //DynamicCallbackList
        static FieldInfo R_BubbleUpCallbacks = Type.GetField("m_BubbleUpCallbacks", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo R_TrickleDownCallbacks = Type.GetField("m_TrickleDownCallbacks", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public R_DynamicCallbackList m_BubbleUpCallbacks => new(R_BubbleUpCallbacks.GetValue(x));
        public R_DynamicCallbackList m_TrickleDownCallbacks => new(R_BubbleUpCallbacks.GetValue(x));
        //object m_BubbleUpCallbacks //DynamicCallbackList
    }

    public struct R_EventBase
    {
        public static Type Type = R_VisualElement.GetUIElementsType("EventBase");
        public object x; public R_EventBase(object x) => this.x = x;

        static PropertyInfo R_elementTarget = Type.GetProperty("elementTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        public VisualElement elementTarget 
        { 
            get => (VisualElement)R_elementTarget.GetValue(x);
            set => R_elementTarget.SetValue(x, value);
        }
    }

    public struct R_PointerDispatchState
    {
        public static Type Type = R_VisualElement.GetUIElementsType("PointerDispatchState");
        public object x; public R_PointerDispatchState(object x) => this.x = x;

        static MethodInfo R_PreventCompatibilityMouseEvents = Type.GetMethod("PreventCompatibilityMouseEvents");

        public void PreventCompatibilityMouseEvents(int pointerId) => R_PreventCompatibilityMouseEvents.Invoke(x, R_VisualElement.GetArgs1(pointerId));
    }

    public struct R_EventDispatcher
    {
        public static Type Type = R_VisualElement.GetUIElementsType("EventDispatcher");
        public object x; public R_EventDispatcher(object x) => this.x = x;

        static PropertyInfo R_pointerState { get; } = Type.GetProperty("pointerState", BindingFlags.NonPublic | BindingFlags.Instance);

        public R_PointerDispatchState pointerState => new(R_pointerState.GetValue(x));
    }

    public struct R_IPanel
    {
        public static Type Type = typeof(IPanel);
        public IPanel x; public R_IPanel(IPanel x) => this.x = x;

        static PropertyInfo R_elementTarget = Type.GetProperty("elementTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        public void PreventCompatibilityMouseEvents(int pointerId)
        {
            var dispatcher = x.dispatcher;
            if (dispatcher != null)
                new R_EventDispatcher(dispatcher).pointerState.PreventCompatibilityMouseEvents(pointerId);
        }

        public VisualElement elementTarget 
        { 
            get => (VisualElement)R_elementTarget.GetValue(x);
            set => R_elementTarget.SetValue(x, value);
        }
    }
}