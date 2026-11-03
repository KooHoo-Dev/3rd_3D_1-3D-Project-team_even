using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 대사·목표 문구·단계 수치 (기획: Docs/Design/Tutorial_Proposal_v3.md, 대사본: Tutorial_VoiceScript.md).
    /// 시나리오 담당이 인스펙터에서 고친다. 대사 Id 는 음성 파일 이름이자 SoundTable 키다 (바꾸지 않는다).
    /// 몬스터·보급·강화 자체의 값은 각 테이블이 기준이고, 여기에는 "튜토리얼에서 몇 개·무엇을 쓰는지"만 둔다.
    /// 에셋: Tools > Tutorial > Setup Tutorial Scene 이 Assets/Data/Tutorial/TutorialScript.asset 으로 만들고 대사본 TSV 로 채운다
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Tutorial Script", fileName = "TutorialScript")]
    public sealed class TutorialScript : ScriptableObject
    {
        [Serializable]
        public sealed class Line
        {
            [Tooltip("대사 Id = 음성 파일 이름 = SoundTable 키 (예: vo_tut_01_move)")]
            public string Id;
            [Tooltip("화면 자막")]
            [TextArea(2, 4)] public string Subtitle;
        }

        [Serializable]
        public sealed class Objective
        {
            [Tooltip("목표 Id (예: obj_move)")]
            public string Id;
            [Tooltip("목표 패널 문구. {0} = 진행 수, {1} = 목표 수 (진행 표시가 없는 목표는 비워 둔다)")]
            [TextArea(1, 3)] public string Text;
        }

        [Header("대사 (자막 + 음성 키)")]
        public List<Line> Lines = new List<Line>();
        [Header("목표 패널")]
        public List<Objective> Objectives = new List<Objective>();

        [Header("① 이동 · 질주 · 구르기")]
        [Tooltip("질주를 모두 합쳐 이만큼(초) 해야 다음으로")]
        [Min(0.5f)] public float SprintSeconds = 2f;
        [Tooltip("차단막에 그냥 닿았을 때 피해")]
        [Min(1)] public int BarrierDamage = 10;

        [Header("휴면 개체 (②·②-B·③·④)")]
        [Tooltip("MonsterTable Id. 기본 1001 부포모프 (지상 최약체)")]
        public int DormantMonsterId = 1001;
        [Tooltip("② 사격 표적 수 (aim 소환 지점 수보다 많으면 지점 수만큼)")]
        [Min(1)] public int AimTargetCount = 3;
        [Tooltip("②-B 무기 교체 뒤 주무기로 잡을 표적 수 (aim 소환 지점 수보다 많으면 지점 수만큼)")]
        [Min(1)] public int SwitchTargetCount = 3;
        [Tooltip("③ 수류탄 무리 수")]
        [Min(1)] public int GrenadeGroupCount = 5;
        [Tooltip("③ 수류탄이 없을 때 채워 줄 개수")]
        [Min(1)] public int GrenadeGrant = 2;
        [Tooltip("④ 보급 무기 무리 수")]
        [Min(1)] public int WeaponGroupCount = 5;

        [Header("④ 보급 (SupplyTable Id)")]
        public int HealSupplyId = 4002;
        public int GrenadeSupplyId = 4001;
        public int WeaponSupplyId = 4101;
        [Tooltip("보급 과목 시작 때 체력이 이 비율을 넘으면 훈련 충격으로 HealthAfterShock 까지 낮춘다")]
        [Range(0f, 1f)] public float ShockAboveRatio = 0.7f;
        [Range(0.1f, 1f)] public float HealthAfterShock = 0.5f;

        private Dictionary<string, Line> lineMap;
        private Dictionary<string, Objective> objectiveMap;

        public Line FindLine(string id)
        {
            if (lineMap == null) lineMap = Build(Lines, l => l.Id);
            return id != null && lineMap.TryGetValue(id, out var line) ? line : null;
        }

        /// <summary>목표 문구. 없으면 Id 그대로 (빠진 문구를 화면에서 바로 알아보게)</summary>
        public string ObjectiveText(string id, int progress = 0, int goal = 0)
        {
            if (objectiveMap == null) objectiveMap = Build(Objectives, o => o.Id);
            if (id == null || objectiveMap.TryGetValue(id, out var objective) == false || string.IsNullOrEmpty(objective.Text)) return id;
            return goal > 0 ? string.Format(objective.Text, progress, goal) : objective.Text;
        }

        private void OnValidate()
        {
            lineMap = null;
            objectiveMap = null;
        }

        private static Dictionary<string, T> Build<T>(List<T> items, Func<T, string> key)
        {
            var map = new Dictionary<string, T>();
            foreach (var item in items)
                if (item != null && string.IsNullOrEmpty(key(item)) == false) map[key(item)] = item;
            return map;
        }
    }
}
