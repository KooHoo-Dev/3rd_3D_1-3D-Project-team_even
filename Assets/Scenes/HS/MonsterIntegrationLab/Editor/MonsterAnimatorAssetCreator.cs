using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 일회용 에셋 생성 메뉴.
/// 몬스터 공용 Base 애니메이터 컨트롤러 1개와 몬스터별 AnimatorOverrideController 8개를 만든다.
/// 실제 클립 연결은 사용자가 Override 컨트롤러 Inspector 에서 직접 한다.
/// </summary>
public static class MonsterAnimatorAssetCreator
{
    private const string ParentFolder = "Assets/Scenes/HS/MonsterIntegrationLab/Prefabs";
    private const string OutputFolderName = "MonsterAnimators";
    private const string OutputFolder = ParentFolder + "/" + OutputFolderName;
    private const string BaseControllerPath = OutputFolder + "/Monster Base Controller.controller";
    private const float TransitionDuration = 0.15f; // 초

    private static readonly string[] stateNames = { "Idle", "Move", "Attack", "Stunned", "Death" };

    // ── 파라미터 ──
    // Idle 트리거 = Attack/Stunned 에서 Idle 로 돌아가는 신호. 나머지 트리거 = Any State 에서 그 상태로 들어가는 신호
    private static readonly string[] triggerNames = { "Idle", "Attack", "Stunned", "Death" };
    private const string IsMovingParameter = "IsMoving"; // Bool. Idle ↔ Move 전이 조건

    private static readonly string[] monsterNames =
    {
        "Tyranopode", "Muscomorph", "Clypeosaurus", "GiantSlug",
        "Letalobrach", "Bufomorph", "Pardathrox", "Densoptere",
    };

    /// <summary>
    /// 메뉴 진입점. 폴더 → 임시 클립 → Base 컨트롤러 → Override 컨트롤러 순으로 만든다.
    /// Base 컨트롤러가 이미 있으면 사용자가 연결한 클립을 지키기 위해 아무것도 만들지 않는다.
    /// </summary>
    [MenuItem("Tools/Monster/공용 애니메이터 에셋 생성")]
    private static void CreateAll()
    {
        bool baseControllerExists = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath) != null;
        if (baseControllerExists)
        {
            Debug.LogError($"이미 있음: {BaseControllerPath}. 다시 만들려면 MonsterAnimators 폴더를 직접 지운 뒤 실행한다.");
            return;
        }

        CreateOutputFolder();
        AnimationClip[] placeholderClips = CreatePlaceholderClips();
        AnimatorController baseController = CreateBaseController(placeholderClips);
        CreateOverrideControllers(baseController);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"몬스터 애니메이터 에셋 생성 완료: {OutputFolder}");
    }

    /// <summary>
    /// 이미 있는 Base 컨트롤러의 파라미터와 전이만 지우고 BuildParametersAndTransitions 로 다시 만든다.
    /// 상태와 클립은 그대로라 Override 컨트롤러 연결과 넣어 둔 클립이 유지된다.
    /// </summary>
    [MenuItem("Tools/Monster/공용 애니메이터 전이 다시 만들기")]
    private static void RebuildTransitions()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
        ClearParametersAndTransitions(controller);
        BuildParametersAndTransitions(controller);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"몬스터 Base 컨트롤러 전이 재구성 완료: {BaseControllerPath}");
    }

    /// <summary>MonsterAnimators 폴더가 없으면 만든다</summary>
    private static void CreateOutputFolder()
    {
        if (AssetDatabase.IsValidFolder(OutputFolder)) return;
        AssetDatabase.CreateFolder(ParentFolder, OutputFolderName);
    }

    /// <summary>
    /// 상태마다 서로 다른 빈 임시 클립을 만든다.
    /// Override 는 클립 단위로 교체되므로 두 상태가 같은 클립을 쓰면 함께 바뀐다.
    /// </summary>
    private static AnimationClip[] CreatePlaceholderClips()
    {
        var placeholderClips = new AnimationClip[stateNames.Length];
        for (int i = 0; i < stateNames.Length; i++)
        {
            var clip = new AnimationClip { name = "Placeholder_" + stateNames[i] };
            AssetDatabase.CreateAsset(clip, $"{OutputFolder}/{clip.name}.anim");
            placeholderClips[i] = clip;
        }
        return placeholderClips;
    }

    /// <summary>Base 컨트롤러: 상태 5개(기본 Idle)를 만들고 파라미터와 전이를 붙인다</summary>
    private static AnimatorController CreateBaseController(AnimationClip[] placeholderClips)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        for (int i = 0; i < stateNames.Length; i++)
        {
            AnimatorState state = rootStateMachine.AddState(stateNames[i], new Vector3(300f, 60f * i, 0f));
            state.motion = placeholderClips[i];
        }
        rootStateMachine.defaultState = rootStateMachine.states[0].state;

        BuildParametersAndTransitions(controller);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// 전이 규칙 전체. 모두 Exit Time 없이 조건으로만 넘어간다.
    /// Idle ↔ Move 는 IsMoving Bool, Attack/Stunned/Death 진입은 Any State + 트리거,
    /// Attack/Stunned 에서 나갈 때는 Idle 트리거로 항상 Idle 을 거친다. Death 는 나가는 전이가 없다.
    /// </summary>
    private static void BuildParametersAndTransitions(AnimatorController controller)
    {
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = FindState(rootStateMachine, "Idle");
        AnimatorState moveState = FindState(rootStateMachine, "Move");
        AnimatorState attackState = FindState(rootStateMachine, "Attack");
        AnimatorState stunnedState = FindState(rootStateMachine, "Stunned");
        AnimatorState deathState = FindState(rootStateMachine, "Death");

        foreach (string triggerName in triggerNames)
            controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(IsMovingParameter, AnimatorControllerParameterType.Bool);

        // Idle ↔ Move
        AddStateTransition(idleState, moveState).AddCondition(AnimatorConditionMode.If, 0f, IsMovingParameter);
        AddStateTransition(moveState, idleState).AddCondition(AnimatorConditionMode.IfNot, 0f, IsMovingParameter);

        // Attack / Stunned → Idle
        AddStateTransition(attackState, idleState).AddCondition(AnimatorConditionMode.If, 0f, "Idle");
        AddStateTransition(stunnedState, idleState).AddCondition(AnimatorConditionMode.If, 0f, "Idle");

        // Any State → Attack / Stunned / Death
        AddAnyStateTransition(rootStateMachine, attackState, "Attack");
        AddAnyStateTransition(rootStateMachine, stunnedState, "Stunned");
        AddAnyStateTransition(rootStateMachine, deathState, "Death");
    }

    /// <summary>재구성 전에 기존 파라미터, Any State 전이, 상태 간 전이를 모두 지운다</summary>
    private static void ClearParametersAndTransitions(AnimatorController controller)
    {
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        while (controller.parameters.Length > 0)
            controller.RemoveParameter(0);

        foreach (AnimatorStateTransition transition in rootStateMachine.anyStateTransitions)
            rootStateMachine.RemoveAnyStateTransition(transition);

        foreach (ChildAnimatorState childState in rootStateMachine.states)
        {
            foreach (AnimatorStateTransition transition in childState.state.transitions)
                childState.state.RemoveTransition(transition);
        }
    }

    /// <summary>이름으로 상태를 찾는다. 없으면 상태 이름이 바뀐 것이므로 예외</summary>
    private static AnimatorState FindState(AnimatorStateMachine rootStateMachine, string stateName)
    {
        foreach (ChildAnimatorState childState in rootStateMachine.states)
        {
            if (childState.state.name == stateName) return childState.state;
        }
        throw new System.InvalidOperationException($"Base 컨트롤러에 {stateName} 상태가 없다");
    }

    /// <summary>상태 → 상태 전이. Exit Time 없이 조건으로만 넘어간다</summary>
    private static AnimatorStateTransition AddStateTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = TransitionDuration;
        return transition;
    }

    /// <summary>Any State → 상태 전이. 같은 상태로 다시 들어가지 않는다</summary>
    private static void AddAnyStateTransition(AnimatorStateMachine rootStateMachine, AnimatorState to, string triggerName)
    {
        AnimatorStateTransition transition = rootStateMachine.AddAnyStateTransition(to);
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
        transition.canTransitionToSelf = false;
        transition.hasExitTime = false;
        transition.duration = TransitionDuration;
    }

    /// <summary>몬스터별 Override 컨트롤러를 만든다. 클립 칸은 비워 두고, 파일이 이미 있으면 건너뛴다</summary>
    private static void CreateOverrideControllers(AnimatorController baseController)
    {
        foreach (string monsterName in monsterNames)
        {
            string overridePath = $"{OutputFolder}/{monsterName} Controller.overrideController";
            bool overrideExists = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath) != null;
            if (overrideExists)
            {
                Debug.LogWarning($"이미 있어서 건너뜀: {overridePath}");
                continue;
            }

            var overrideController = new AnimatorOverrideController(baseController);
            AssetDatabase.CreateAsset(overrideController, overridePath);
        }
    }
}
