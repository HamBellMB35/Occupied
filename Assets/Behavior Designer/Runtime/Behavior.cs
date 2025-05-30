using UnityEngine;
#if ENABLE_MULTIPLAYER
using UnityEngine.Networking;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using BehaviorDesigner.Runtime.Tasks;

namespace BehaviorDesigner.Runtime
{
    [System.Serializable]
    public abstract class Behavior : MonoBehaviour, IBehavior
    {
        [SerializeField] [UnityEngine.Tooltip("If true, the behavior tree will start running when the component is enabled.")]
        private bool startWhenEnabled = true;
        public bool StartWhenEnabled { get { return startWhenEnabled; } set { startWhenEnabled = value; } }
        [SerializeField] [UnityEngine.Tooltip("Specifies if the behavior tree should load in a separate thread." +
                                              "Because Unity does not allow for API calls to be made on worker threads this option should be disabled if you are using property mappings" +
                                              "for the shared variables.")]
        private bool asynchronousLoad = false;
        public bool AsynchronousLoad { get { return asynchronousLoad; } set { asynchronousLoad = value; } }

        [SerializeField] [UnityEngine.Tooltip("If true, the behavior tree will pause when the component is disabled. If false, the behavior tree will end.")]
        private bool pauseWhenDisabled = false;
        public bool PauseWhenDisabled { get { return pauseWhenDisabled; } set { pauseWhenDisabled = value; } }

        [SerializeField] [UnityEngine.Tooltip("If true, the behavior tree will restart from the beginning when it has completed execution. If false, the behavior tree will end.")]
        private bool restartWhenComplete = false;
        public bool RestartWhenComplete { get { return restartWhenComplete; } set { restartWhenComplete = value; } }

        [SerializeField] [UnityEngine.Tooltip("Used for debugging. If enabled, the behavior tree will output any time a task status changes, such as it starting or stopping.")]
        private bool logTaskChanges = false;
        public bool LogTaskChanges { get { return logTaskChanges; } set { logTaskChanges = value; } }

        [SerializeField] [UnityEngine.Tooltip("A numerical grouping of behavior trees. Can be used to easily find behavior trees.")]
        private int group = 0;
        public int Group { get { return group; } set { group = value; } }

        [SerializeField] [UnityEngine.Tooltip("If true, the variables and task public variables will be reset to their original values when the tree restarts.")]
        private bool resetValuesOnRestart = false;
        public bool ResetValuesOnRestart { get { return resetValuesOnRestart; } set { resetValuesOnRestart = value; } }

        // reference to an external behavior tree, useful if creating a behavior tree from script
        [SerializeField] [UnityEngine.Tooltip("A field to specify the external behavior tree that should be run when this behavior tree starts.")]
        private ExternalBehavior externalBehavior;
        public ExternalBehavior ExternalBehavior
        {
            get { return externalBehavior; }
            set
            {
                if (externalBehavior == value) {
                    return;
                }
                if (BehaviorManager.instance != null) {
                    BehaviorManager.instance.DisableBehavior(this);
                }
                // If the external tree has been initialized then it has been manually deserialized. Don't deserialize again.
                if (value != null && value.Initialized) {
                    // Store a reference to the current tree's variables so they can inherit the external tree variables.
                    var variables = mBehaviorSource.GetAllVariables();
                    mBehaviorSource = value.BehaviorSource;
                    mBehaviorSource.HasSerialized = true;
                    if (variables != null) {
                        for (int i = 0; i < variables.Count; ++i) {
                            if (variables[i] == null) {
                                continue;
                            }
                            mBehaviorSource.SetVariable(variables[i].Name, variables[i]);
                        }
                    }
                } else {
                    mBehaviorSource.HasSerialized = false;
                    hasInheritedVariables = false;
                }
                initialized = false;
                externalBehavior = value;
                if (startWhenEnabled) {
                    EnableBehavior();
                }
            }
        }
        private bool hasInheritedVariables = false;
        public bool HasInheritedVariables { get { return hasInheritedVariables; } set { hasInheritedVariables = value; } }
        public string BehaviorName { get { return mBehaviorSource.behaviorName; } set { mBehaviorSource.behaviorName = value; } }
        public string BehaviorDescription { get { return mBehaviorSource.behaviorDescription; } set { mBehaviorSource.behaviorDescription = value; } }

        [SerializeField]
        private BehaviorSource mBehaviorSource;
        public BehaviorSource GetBehaviorSource() { return mBehaviorSource; }
        public void SetBehaviorSource(BehaviorSource behaviorSource) { mBehaviorSource = behaviorSource; }
        public UnityEngine.Object GetObject() { return this; }
        public string GetOwnerName() { return gameObject.name; }

        private bool isPaused = false;
        private TaskStatus executionStatus = TaskStatus.Inactive;
        public TaskStatus ExecutionStatus { get { return executionStatus; } set { executionStatus = value; } }
        private bool initialized = false;
        private bool isAsyncLoading = false;
        public bool IsAsyncLoading { get { return isAsyncLoading; } set { isAsyncLoading = value; } }

        private Dictionary<Task, Dictionary<string, object>> defaultValues;
        private Dictionary<SharedVariable, object> defaultVariableValues;

        // events
        public enum EventTypes { OnCollisionEnter, OnCollisionExit, OnTriggerEnter, OnTriggerExit,
                                 OnCollisionEnter2D, OnCollisionExit2D, OnTriggerEnter2D, OnTriggerExit2D, 
                                 OnControllerColliderHit, OnLateUpdate, OnFixedUpdate, OnAnimatorIK, None }
        private bool[] hasEvent = new bool[(int)EventTypes.None];
        public bool[] HasEvent { get { return hasEvent; } }

        // coroutines
        private Dictionary<string, List<TaskCoroutine>> activeTaskCoroutines = null;

        // events
        public delegate void BehaviorHandler(Behavior behavior);
        public event BehaviorHandler OnBehaviorStart;
        public event BehaviorHandler OnBehaviorRestart;
        public event BehaviorHandler OnBehaviorEnd;
        private Dictionary<Type, Dictionary<string, Delegate>> eventTable;

#if UNITY_EDITOR || DLL_DEBUG || DLL_RELEASE
        // gizmo drawings
        public enum GizmoViewMode { Running, Always, Selected, Never }
        public GizmoViewMode gizmoViewMode;
        public bool showBehaviorDesignerGizmo = true;
#endif

        public Behavior()
        {
            mBehaviorSource = new BehaviorSource(this);
        }

        public void Start()
        {
            if (startWhenEnabled) {
                EnableBehavior();
            }
        }

        public void EnableBehavior()
        {
            // create the behavior manager if it doesn't already exist
            CreateBehaviorManager();
            if (BehaviorManager.instance != null) {
                BehaviorManager.instance.EnableBehavior(this);
            }
        }

        public void DisableBehavior()
        {
            if (BehaviorManager.instance != null) {
                BehaviorManager.instance.DisableBehavior(this, pauseWhenDisabled);
                isPaused = pauseWhenDisabled;
            }
        }

        public void DisableBehavior(bool pause)
        {
            if (BehaviorManager.instance != null) {
                BehaviorManager.instance.DisableBehavior(this, pause);
                isPaused = pause;
            }
        }

        public void OnEnable()
        {
            if (BehaviorManager.instance != null && isPaused) {
                BehaviorManager.instance.EnableBehavior(this);
                isPaused = false;
            } else if (startWhenEnabled && initialized) {
                EnableBehavior();
            }
        }

        public void OnDisable()
        {
            DisableBehavior();
        }

        public void OnDestroy()
        {
            if (BehaviorManager.instance != null) {
                BehaviorManager.instance.DestroyBehavior(this);
            }
        }

        // Support blackboard variables:
        public SharedVariable GetVariable(string name)
        {
            CheckForSerialization();
            return mBehaviorSource.GetVariable(name);
        }

        public void SetVariable(string name, SharedVariable item)
        {
            CheckForSerialization();
            mBehaviorSource.SetVariable(name, item);
        }

        public void SetVariableValue(string name, object value)
        {
            var sharedVariable = GetVariable(name);
            if (sharedVariable != null) {
                if (value is SharedVariable) {
                    var sharedVariableValue = value as SharedVariable;
                    if (!string.IsNullOrEmpty(sharedVariableValue.PropertyMapping)) {
                        sharedVariable.PropertyMapping = sharedVariableValue.PropertyMapping;
                        sharedVariable.PropertyMappingOwner = sharedVariableValue.PropertyMappingOwner;
                        sharedVariable.InitializePropertyMapping(mBehaviorSource);
                    } else {
                        sharedVariable.SetValue(sharedVariableValue.GetValue());
                    }
                } else {
                    sharedVariable.SetValue(value);
                }
            } else {
                if (value is SharedVariable) {
                    // Add the new variable
                    var sharedVariableValue = value as SharedVariable;
                    var variable = TaskUtility.CreateInstance(sharedVariableValue.GetType()) as SharedVariable;
                    variable.Name = sharedVariableValue.Name;
                    variable.IsShared = sharedVariableValue.IsShared;
                    variable.IsGlobal = sharedVariableValue.IsGlobal;
                    if (!string.IsNullOrEmpty(sharedVariableValue.PropertyMapping)) {
                        variable.PropertyMapping = sharedVariableValue.PropertyMapping;
                        variable.PropertyMappingOwner = sharedVariableValue.PropertyMappingOwner;
                        variable.InitializePropertyMapping(mBehaviorSource);
                    } else {
                        variable.SetValue(sharedVariableValue.GetValue());
                    }
                    mBehaviorSource.SetVariable(name, variable);
                } else {
                    Debug.LogError("Error: No variable exists with name " + name);
                }
            }
        }

        public List<SharedVariable> GetAllVariables()
        {
            CheckForSerialization();
            return mBehaviorSource.GetAllVariables();
        }

        public void CheckForSerialization()
        {
            CheckForSerialization(false, Application.isPlaying);
        }

        public void CheckForSerialization(bool forceSerialization, bool isPlaying)
        {
            if (externalBehavior != null) {
                var hasSerialized = mBehaviorSource.HasSerialized;
                mBehaviorSource.CheckForSerialization(forceSerialization || !hasSerialized, null, isPlaying);
                var variables = mBehaviorSource.GetAllVariables();
                hasInheritedVariables = variables != null && variables.Count > 0;
                externalBehavior.BehaviorSource.Owner = ExternalBehavior;
                externalBehavior.BehaviorSource.CheckForSerialization(forceSerialization || !hasSerialized, GetBehaviorSource(), isPlaying);
                externalBehavior.BehaviorSource.EntryTask = mBehaviorSource.EntryTask;
                if (hasInheritedVariables) {
                    for (int i = 0; i < variables.Count; ++i) {
                        if (variables[i] == null) {
                            continue;
                        }
                        mBehaviorSource.SetVariable(variables[i].Name, variables[i]);
                    }
                }
            } else {
                mBehaviorSource.CheckForSerialization(false, null, isPlaying);
            }
        }

        // Support collisions/triggers:
        public void OnCollisionEnter(Collision collision)
        {
            if (hasEvent[(int)EventTypes.OnCollisionEnter] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnCollisionEnter(collision, this);
            }
        }

        public void OnCollisionExit(Collision collision)
        {
            if (hasEvent[(int)EventTypes.OnCollisionExit] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnCollisionExit(collision, this);
            }
        }

        public void OnTriggerEnter(Collider other)
        {
            if (hasEvent[(int)EventTypes.OnTriggerEnter] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnTriggerEnter(other, this);
            }
        }

        public void OnTriggerExit(Collider other)
        {
            if (hasEvent[(int)EventTypes.OnTriggerExit] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnTriggerExit(other, this);
            }
        }

        public void OnCollisionEnter2D(Collision2D collision)
        {
            if (hasEvent[(int)EventTypes.OnCollisionEnter2D] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnCollisionEnter2D(collision, this);
            }
        }

        public void OnCollisionExit2D(Collision2D collision)
        {
            if (hasEvent[(int)EventTypes.OnCollisionExit2D] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnCollisionExit2D(collision, this);
            }
        }

        public void OnTriggerEnter2D(Collider2D other)
        {
            if (hasEvent[(int)EventTypes.OnTriggerEnter2D] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnTriggerEnter2D(other, this);
            }
        }

        public void OnTriggerExit2D(Collider2D other)
        {
            if (hasEvent[(int)EventTypes.OnTriggerExit2D] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnTriggerExit2D(other, this);
            }
        }

        public void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hasEvent[(int)EventTypes.OnControllerColliderHit] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnControllerColliderHit(hit, this);
            }
        }

        public void OnAnimatorIK()
        {
            if (hasEvent[(int)EventTypes.OnAnimatorIK] && BehaviorManager.instance != null) {
                BehaviorManager.instance.BehaviorOnAnimatorIK(this);
            }
        }

#if UNITY_EDITOR || DLL_DEBUG || DLL_RELEASE
        public void OnDrawGizmos()
        {
            DrawTaskGizmos(false);
        }

        public void OnDrawGizmosSelected()
        {
            if (showBehaviorDesignerGizmo) {
                Gizmos.DrawIcon(transform.position, "Behavior Designer Scene Icon.png");
            }
            DrawTaskGizmos(true);
        }

        private void DrawTaskGizmos(bool selected)
        {
            if (gizmoViewMode == GizmoViewMode.Never || (gizmoViewMode == GizmoViewMode.Selected && !selected)) {
                return;
            }

            if (gizmoViewMode == GizmoViewMode.Running || gizmoViewMode == GizmoViewMode.Always || (Application.isPlaying && ExecutionStatus == TaskStatus.Running) || !Application.isPlaying) {
                CheckForSerialization();

                DrawTaskGizmos(mBehaviorSource.RootTask);

                var detachedTasks = mBehaviorSource.DetachedTasks;
                if (detachedTasks != null) {
                    for (int i = 0; i < detachedTasks.Count; ++i) {
                        DrawTaskGizmos(detachedTasks[i]);
                    }
                }
            }
        }

        private void DrawTaskGizmos(Task task)
        {
            if (task == null) {
                return;
            }

            // If the view mode is Running then only draw the gizmo when the task is reevaluating (with conditional aborts) or is currently running.
            if (gizmoViewMode == GizmoViewMode.Running && (!task.NodeData.IsReevaluating && (task.NodeData.IsReevaluating || task.NodeData.ExecutionStatus != TaskStatus.Running))) {
                return;
            }

            task.OnDrawGizmos();

            if (task is ParentTask) {
                var parentTask = task as ParentTask;
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        DrawTaskGizmos(parentTask.Children[i]);
                    }
                }
            }
        }
#endif

        public T FindTask<T>() where T : Task
        {
            CheckForSerialization();

            return FindTask<T>(mBehaviorSource.RootTask);
        }

        private T FindTask<T>(Task task) where T : Task
        {
            if (task.GetType().Equals(typeof(T))) {
                return (T)task;
            }

            ParentTask parentTask;
            if ((parentTask = task as ParentTask) != null) {
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        T foundTask = null;
                        if ((foundTask = FindTask<T>(parentTask.Children[i])) != null) {
                            return foundTask;
                        }
                    }
                }
            }

            return null;
        }

        public List<T> FindTasks<T>() where T : Task
        {
            CheckForSerialization();

            List<T> tasks = new List<T>();
            FindTasks<T>(mBehaviorSource.RootTask, ref tasks);
            return tasks;
        }

        private void FindTasks<T>(Task task, ref List<T> taskList) where T : Task
        {
            if (typeof(T).IsAssignableFrom(task.GetType())) {
                taskList.Add((T)task);
            }

            ParentTask parentTask;
            if ((parentTask = task as ParentTask) != null) {
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        FindTasks<T>(parentTask.Children[i], ref taskList);
                    }
                }
            }
        }

        public Task FindTaskWithName(string taskName)
        {
            CheckForSerialization();

            return FindTaskWithName(taskName, mBehaviorSource.RootTask);
        }

        private Task FindTaskWithName(string taskName, Task task)
        {
            if (task.FriendlyName.Equals(taskName)) {
                return task;
            }

            ParentTask parentTask;
            if ((parentTask = task as ParentTask) != null) {
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        Task foundTask = null;
                        if ((foundTask = FindTaskWithName(taskName, parentTask.Children[i])) != null) {
                            return foundTask;
                        }
                    }
                }
            }

            return null;
        }

        public List<Task> FindTasksWithName(string taskName)
        {
            CheckForSerialization();

            List<Task> tasks = new List<Task>();
            FindTasksWithName(taskName, mBehaviorSource.RootTask, ref tasks);
            return tasks;
        }

        private void FindTasksWithName(string taskName, Task task, ref List<Task> taskList)
        {
            if (task.FriendlyName.Equals(taskName)) {
                taskList.Add(task);
            }

            ParentTask parentTask;
            if ((parentTask = task as ParentTask) != null) {
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        FindTasksWithName(taskName, parentTask.Children[i], ref taskList);
                    }
                }
            }
        }

        public List<Task> GetActiveTasks()
        {
            if (BehaviorManager.instance == null) {
                return null;
            }
            return BehaviorManager.instance.GetActiveTasks(this);
        }

        // System.objects don't normally support coroutines. Add that support here.
        public Coroutine StartTaskCoroutine(Task task, string methodName)
        {
#if !UNITY_EDITOR && (UNITY_WSA_8_0 || UNITY_WSA_8_1)
            var method = task.GetType().GetMethod(methodName, System.BindingFlags.Public |System.BindingFlags.NonPublic | System.BindingFlags.Instance);
#else
            var method = task.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
#endif
            if (method == null) {
                Debug.LogError("Unable to start coroutine " + methodName + ": method not found");
                return null;
            }
            if (activeTaskCoroutines == null) {
                activeTaskCoroutines = new Dictionary<string, List<TaskCoroutine>>();
            }
            var taskCoroutine = new TaskCoroutine(this, (IEnumerator)method.Invoke(task, new object[] { }), methodName);
            if (activeTaskCoroutines.ContainsKey(methodName)) {
                var taskCoroutines = activeTaskCoroutines[methodName];
                taskCoroutines.Add(taskCoroutine);
                activeTaskCoroutines[methodName] = taskCoroutines;
            } else {
                var taskCoroutines = new List<TaskCoroutine>();
                taskCoroutines.Add(taskCoroutine);
                activeTaskCoroutines.Add(methodName, taskCoroutines);
            }
            return taskCoroutine.Coroutine;
        }

        public Coroutine StartTaskCoroutine(Task task, string methodName, object value)
        {
#if !UNITY_EDITOR && (UNITY_WSA_8_0 || UNITY_WSA_8_1)
            var method = task.GetType().GetMethod(methodName, System.BindingFlags.Public |System.BindingFlags.NonPublic | System.BindingFlags.Instance);
#else
            var method = task.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
#endif
            if (method == null) {
                Debug.LogError("Unable to start coroutine " + methodName + ": method not found");
                return null;
            }
            if (activeTaskCoroutines == null) {
                activeTaskCoroutines = new Dictionary<string, List<TaskCoroutine>>();
            }
            var taskCoroutine = new TaskCoroutine(this, (IEnumerator)method.Invoke(task, new object[] { value }), methodName);
            if (activeTaskCoroutines.ContainsKey(methodName)) {
                var taskCoroutines = activeTaskCoroutines[methodName];
                taskCoroutines.Add(taskCoroutine);
                activeTaskCoroutines[methodName] = taskCoroutines;
            } else {
                var taskCoroutines = new List<TaskCoroutine>();
                taskCoroutines.Add(taskCoroutine);
                activeTaskCoroutines.Add(methodName, taskCoroutines);
            }
            return taskCoroutine.Coroutine;
        }

        public void StopTaskCoroutine(string methodName)
        {
            if (!activeTaskCoroutines.ContainsKey(methodName)) {
                return;
            }

            var taskCoroutines = activeTaskCoroutines[methodName];
            for (int i = 0; i < taskCoroutines.Count; ++i) {
                taskCoroutines[i].Stop();
            }
        }

        public void StopAllTaskCoroutines()
        {
            StopAllCoroutines();

            if (activeTaskCoroutines == null) {
                return;
            }

            foreach (var entry in activeTaskCoroutines) {
                var taskCoroutines = entry.Value;
                for (int i = 0; i < taskCoroutines.Count; ++i) {
                    taskCoroutines[i].Stop();
                }
            }
        }

        public void TaskCoroutineEnded(TaskCoroutine taskCoroutine, string coroutineName)
        {
            if (activeTaskCoroutines.ContainsKey(coroutineName)) {
                var taskCoroutines = activeTaskCoroutines[coroutineName];
                if (taskCoroutines.Count == 1) {
                    activeTaskCoroutines.Remove(coroutineName);
                } else {
                    taskCoroutines.Remove(taskCoroutine);
                    activeTaskCoroutines[coroutineName] = taskCoroutines;
                }
            }
        }

        public void OnBehaviorStarted()
        {
            if (!initialized) {
                CheckForEventMethods(new HashSet<Type>(), mBehaviorSource.RootTask);
                initialized = true;
            }

            if (OnBehaviorStart != null) {
                OnBehaviorStart(this);
            }
        }

        private void CheckForEventMethods(HashSet<Type> typesChecked, Task task)
        {
            if (task == null) {
                return;
            }

            if (!typesChecked.Contains(task.GetType())) {
#if !UNITY_EDITOR && (UNITY_WSA_8_0 || UNITY_WSA_8_1)
                var method = task.GetType().GetMethods(System.BindingFlags.Public |System.BindingFlags.NonPublic | System.BindingFlags.Instance | System.BindingFlags.DeclaredOnly);
#else
                var methods = task.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
#endif
                if (methods != null) {
                    for (int i = 0; i < (int)EventTypes.None; ++i) {
                        if (hasEvent[i]) {
                            continue;
                        }

                        var methodName = ((EventTypes)i).ToString();
                        for (int j = 0; j < methods.Length; ++j) {
                            hasEvent[i] = methods[j].Name.Equals(methodName);
                            if (hasEvent[i]) {
                                break;
                            }
                        }
                        typesChecked.Add(task.GetType());
                    }
                }
            }

            if (task is ParentTask) {
                var parentTask = task as ParentTask;
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        CheckForEventMethods(typesChecked, parentTask.Children[i]);
                    }
                }
            }
        }

        public void OnBehaviorRestarted()
        {
            if (OnBehaviorRestart != null) {
                OnBehaviorRestart(this);
            }
        }

        public void OnBehaviorEnded()
        {
            if (OnBehaviorEnd != null) {
                OnBehaviorEnd(this);
            }
        }

        private void RegisterEvent(string name, Delegate handler)
        {
            if (eventTable == null) {
                eventTable = new Dictionary<Type, Dictionary<string, Delegate>>();
            }

            Dictionary<string, Delegate> eventHandlers;
            if (!eventTable.TryGetValue(handler.GetType(), out eventHandlers)) {
                eventHandlers = new Dictionary<string, Delegate>();
                eventTable.Add(handler.GetType(), eventHandlers);
            }

            Delegate prevHandlers;
            if (eventHandlers.TryGetValue(name, out prevHandlers)) {
                eventHandlers[name] = Delegate.Combine(prevHandlers, handler);
            } else {
                eventHandlers.Add(name, handler);
            }
        }

        public void RegisterEvent(string name, System.Action handler)
        {
            RegisterEvent(name, (Delegate)handler);
        }

        public void RegisterEvent<T>(string name, System.Action<T> handler)
        {
            RegisterEvent(name, (Delegate)handler);
        }

        public void RegisterEvent<T, U>(string name, System.Action<T, U> handler)
        {
            RegisterEvent(name, (Delegate)handler);
        }

        public void RegisterEvent<T, U, V>(string name, System.Action<T, U, V> handler)
        {
            RegisterEvent(name, (Delegate)handler);
        }

        private Delegate GetDelegate(string name, Type type)
        {
            Dictionary<string, Delegate> eventHandlers;
            if (eventTable != null && eventTable.TryGetValue(type, out eventHandlers)) {
                Delegate handler;
                if (eventHandlers.TryGetValue(name, out handler)) {
                    return handler;
                }
            }
            return null;
        }

        public void SendEvent(string name)
        {
            var handler = GetDelegate(name, typeof(System.Action)) as System.Action;
            if (handler != null) {
                handler();
            }
        }

        public void SendEvent<T>(string name, T arg1)
        {
            var handler = GetDelegate(name, typeof(System.Action<T>)) as System.Action<T>;
            if (handler != null) {
                handler(arg1);
            }
        }

        public void SendEvent<T, U>(string name, T arg1, U arg2)
        {
            var handler = GetDelegate(name, typeof(System.Action<T, U>)) as System.Action<T, U>;
            if (handler != null) {
                handler(arg1, arg2);
            }
        }

        public void SendEvent<T, U, V>(string name, T arg1, U arg2, V arg3)
        {
            var handler = GetDelegate(name, typeof(System.Action<T, U, V>)) as System.Action<T, U, V>;
            if (handler != null) {
                handler(arg1, arg2, arg3);
            }
        }

        private void UnregisterEvent(string name, Delegate handler)
        {
            if (eventTable == null) {
                return;
            }
            Dictionary<string, Delegate> eventHandlers;
            if (eventTable.TryGetValue(handler.GetType(), out eventHandlers)) {
                Delegate prevHandlers;
                if (eventHandlers.TryGetValue(name, out prevHandlers)) {
                    eventHandlers[name] = Delegate.Remove(prevHandlers, handler);
                }
            }
        }

        public void UnregisterEvent(string name, System.Action handler)
        {
            UnregisterEvent(name, (Delegate)handler);
        }

        public void UnregisterEvent<T>(string name, System.Action<T> handler)
        {
            UnregisterEvent(name, (Delegate)handler);
        }

        public void UnregisterEvent<T, U>(string name, System.Action<T, U> handler)
        {
            UnregisterEvent(name, (Delegate)handler);
        }

        public void UnregisterEvent<T, U, V>(string name, System.Action<T, U, V> handler)
        {
            UnregisterEvent(name, (Delegate)handler);
        }

        public void SaveResetValues()
        {
            if (defaultValues == null) {
                CheckForSerialization();

                defaultValues = new Dictionary<Task, Dictionary<string, object>>();
                defaultVariableValues = new Dictionary<SharedVariable, object>();

                SaveValues();
            } else {
                ResetValues();
            }
        }

        private void SaveValues()
        {
            var variables = mBehaviorSource.GetAllVariables();
            if (variables != null) {
                for (int i = 0; i < variables.Count; ++i) {
                    defaultVariableValues.Add(variables[i], variables[i].GetValue());
                }
            }

            SaveValue(mBehaviorSource.RootTask);
        }

        private void SaveValue(Task task)
        {
            if (task == null) {
                return;
            }

            var fields = TaskUtility.GetPublicFields(task.GetType());
            var taskValues = new Dictionary<string, object>();
            for (int i = 0; i < fields.Length; ++i) {
                var value = fields[i].GetValue(task);
                if (value is SharedVariable) {
                    var sharedVariable = value as SharedVariable;
                    if (sharedVariable.IsGlobal || sharedVariable.IsShared) {
                        continue;
                    }
                }

                taskValues.Add(fields[i].Name, fields[i].GetValue(task));
            }

            defaultValues.Add(task, taskValues);

            if (task is ParentTask) {
                var parentTask = task as ParentTask;
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        SaveValue(parentTask.Children[i]);
                    }
                }
            }
        }

        private void ResetValues()
        {
            foreach (var variableValue in defaultVariableValues) {
                SetVariableValue(variableValue.Key.Name, variableValue.Value);
            }
            
            ResetValue(mBehaviorSource.RootTask);
        }

        private void ResetValue(Task task)
        {
            if (task == null) {
                return;
            }

            Dictionary<string, object> taskValues;
            if (!defaultValues.TryGetValue(task, out taskValues)) {
                return;
            }

            foreach (var taskValue in taskValues) {
                var field = task.GetType().GetField(taskValue.Key);
                if (field != null) {
                    field.SetValue(task, taskValue.Value);
                }
            }

            if (task is ParentTask) {
                var parentTask = task as ParentTask;
                if (parentTask.Children != null) {
                    for (int i = 0; i < parentTask.Children.Count; ++i) {
                        ResetValue(parentTask.Children[i]);
                    }
                }
            }
        }

        public override string ToString()
        {
            return mBehaviorSource.ToString();
        }

        public static BehaviorManager CreateBehaviorManager()
        {
            if (BehaviorManager.instance == null && Application.isPlaying) {
                var behaviorManager = new GameObject();
                //behaviorManager.hideFlags = HideFlags.HideAndDontSave;
                behaviorManager.name = "Behavior Manager";
                return behaviorManager.AddComponent<BehaviorManager>();
            }
            return null;
        }

        /// <summary>
        /// Reset the static variables for domain reloading.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DomainReset()
        {
#if UNITY_2023_1_OR_NEWER
            var behaviors = UnityEngine.Object.FindObjectsByType<Behavior>(FindObjectsSortMode.None);
#else
            var behaviors = UnityEngine.Object.FindObjectsOfType<Behavior>();
#endif
            if (behaviors == null) {
                return;
            }
            for (int i = 0; i < behaviors.Length; ++i) {
                behaviors[i].mBehaviorSource.HasSerialized = false;
            }
        }
    }
}