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
    // 트리거 = Any State 에서 그 상태로 들어가는 신호 (Attack = 공격 Active 시작, Death = 사망)
    private static readonly string[] triggerNames = { "Attack", "Death" };
    private const string IsMovingParameter = "IsMoving"; // Bool. Idle ↔ Move 전이 조건
    private const string IsStunnedParameter = "IsStunned"; // Bool. true 동안 Stunned, false 가 되면 Idle 로
    private const string IsDeadParameter = "IsDead"; // Bool. true 면 Any State → Attack/Stunned 를 막아 Death 에 고정
    private const string AttackSpeedParameter = "AttackSpeed"; // Float. Attack 상태 재생 배율 (클립 길이 ÷ Active 길이)
    private const float AttackExitTime = 1f; // Attack → Idle 이 시작되는 클립 정규화 시간. Play Mode 에서 조정

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
    /// 전이 규칙 전체. Attack → Idle 만 Exit Time(클립이 끝나면 스스로 복귀)이고 나머지는 조건으로만 넘어간다.
    /// Idle ↔ Move 는 IsMoving, Stunned 는 IsStunned 가 true 인 동안 유지되고 false 면 Idle 로 나온다.
    /// Attack/Stunned/Death 진입은 Any State. Any State 전이는 Death 에서도 검사되므로 Attack/Stunned 진입에 IsDead == false 를 붙여 Death 에서 나가지 않게 한다.
    /// Attack 상태 재생 배율은 AttackSpeed 파라미터가 정한다.
    /// </summary>
    private static void BuildParametersAndTransitions(AnimatorController controller)
    {
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = FindState(rootStateMachine, "Idle");
        AnimatorState moveState = FindState(rootStateMachine, "Move");
        AnimatorState attackState = FindState(rootStateMachine, "Attack");
        AnimatorState stunnedState = FindState(rootStateMachine, "Stunned");
        AnimatorState deathState = FindState(rootStateMachine, "Death");

        // 파라미터
        foreach (string triggerName in triggerNames)
            controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(IsMovingParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(IsStunnedParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(IsDeadParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = AttackSpeedParameter,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f,
        });

        // Attack 상태 재생 배율
        attackState.speedParameter = AttackSpeedParameter;
        attackState.speedParameterActive = true;

        // Idle ↔ Move
        AddStateTransition(idleState, moveState).AddCondition(AnimatorConditionMode.If, 0f, IsMovingParameter);
        AddStateTransition(moveState, idleState).AddCondition(AnimatorConditionMode.IfNot, 0f, IsMovingParameter);

        // Attack → Idle: 조건 없이 클립이 끝나면 (Exit Time)
        AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = AttackExitTime;
        attackToIdle.duration = TransitionDuration;

        // Stunned → Idle: 기절 시간이 끝나 코드가 IsStunned 를 false 로 넣으면
        AddStateTransition(stunnedState, idleState).AddCondition(AnimatorConditionMode.IfNot, 0f, IsStunnedParameter);

        // Any State → Attack / Stunned / Death
        AnimatorStateTransition anyToAttack = AddAnyStateTransition(rootStateMachine, attackState);
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        anyToAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, IsDeadParameter);

        AnimatorStateTransition anyToStunned = AddAnyStateTransition(rootStateMachine, stunnedState);
        anyToStunned.AddCondition(AnimatorConditionMode.If, 0f, IsStunnedParameter);
        anyToStunned.AddCondition(AnimatorConditionMode.IfNot, 0f, IsDeadParameter);

        AnimatorStateTransition anyToDeath = AddAnyStateTransition(rootStateMachine, deathState);
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Death");
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

    /// <summary>
    /// Any State → 상태 전이의 공통 설정만 한다 (같은 상태로 다시 들어가지 않음, Exit Time 없음, 블렌드 시간). 조건은 호출부에서 붙인다.
    /// 재진입 금지가 없으면 IsStunned 처럼 계속 참인 Bool 조건에서 매 프레임 처음부터 다시 들어간다.
    /// </summary>
    private static AnimatorStateTransition AddAnyStateTransition(AnimatorStateMachine rootStateMachine, AnimatorState to)
    {
        AnimatorStateTransition transition = rootStateMachine.AddAnyStateTransition(to);
        transition.canTransitionToSelf = false;
        transition.hasExitTime = false;
        transition.duration = TransitionDuration;
        return transition;
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
