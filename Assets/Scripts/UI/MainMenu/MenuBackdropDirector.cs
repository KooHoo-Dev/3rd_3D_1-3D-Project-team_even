using System.Collections.Generic;
using DG.Tweening;
using DG.Tweening.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>메뉴에서 선택(호버)된 버튼</summary>
    public enum MenuHover { None, GameStart, Options, Quit }

    /// <summary>
    /// 메뉴 버튼 호버에 배경(대치 장면)이 반응하는 연출. MainMenuController 가 선택이 바뀔 때 SetHover 를 부른다.
    ///  - GAME START: 플레이어가 무기를 들어 조준(Aim), 카메라 살짝 줌인
    ///  - OPTIONS: 카메라가 옆으로 비켜 섬, 장면 채도 낮춤 (optionsVolume 가중치)
    ///  - QUIT: 몬스터 몇 마리가 포효(애니+소리)하며 눈이 더 빛남, 화면 가장자리 붉은 비네트 (quitVolume 가중치)
    /// 모든 값은 호버가 바뀔 때마다 지금 값에서 새 목표로 보간한다 (이전 트윈은 Kill) → 빨리 옮겨 다녀도 튀지 않는다.
    /// 포효는 한 번 시작하면 끝까지 재생하고, 쿨다운 동안 다시 QUIT 에 올라가도 새로 포효하지 않는다
    /// </summary>
    public sealed class MenuBackdropDirector : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private CinemachineCamera menuCamera;
        [SerializeField] private MenuCameraSway cameraSway;
        [SerializeField] private DisplayActor player;
        [Tooltip("포효 후보 (비우면 이 아래 DisplayActor 를 찾는다)")]
        [SerializeField] private Transform crowd;
        [Tooltip("OPTIONS 호버 때 켜는 볼륨 (채도 낮춤)")]
        [SerializeField] private Volume optionsVolume;
        [Tooltip("QUIT 호버 때 켜는 볼륨 (붉은 비네트)")]
        [SerializeField] private Volume quitVolume;

        [Header("보간")]
        [Tooltip("호버 반응 보간 시간(초, unscaled)")]
        [SerializeField] private float transitionTime = 0.6f;

        [Header("GAME START")]
        [Tooltip("플레이어 Animator 조준 파라미터 (float, 0~1)")]
        [SerializeField] private string aimParameter = "Aim";
        [Tooltip("줌인 양 (FOV 감소, 도)")]
        [SerializeField] private float gameStartZoom = 4f;
        [Tooltip("카메라 이동 (카메라 기준 로컬, m)")]
        [SerializeField] private Vector3 gameStartOffset = new Vector3(0f, 0f, 0.25f);

        [Header("OPTIONS")]
        [SerializeField] private Vector3 optionsOffset = new Vector3(0.7f, 0.05f, 0f);

        [Header("QUIT")]
        [Tooltip("한 번에 포효하는 몬스터 수")]
        [SerializeField] private int roarCount = 4;
        [Tooltip("포효 시작을 흩뜨리는 시간(초)")]
        [SerializeField] private float roarSpread = 0.5f;
        [Tooltip("다시 포효할 수 있을 때까지(초)")]
        [SerializeField] private float roarCooldown = 4f;
        [Tooltip("QUIT 호버 중 몬스터 발광 배율 (DisplayActor 기본값 대신)")]
        [SerializeField] private float quitEmissionBoost = 5f;

        private readonly List<DisplayActor> monsters = new List<DisplayActor>();
        private MenuHover hover;
        private float baseFov;
        private float nextRoarTime;

        public MenuHover Hover => hover;

        private void Awake()
        {
            if (menuCamera != null) baseFov = menuCamera.Lens.FieldOfView;
            if (crowd != null)
                foreach (var actor in crowd.GetComponentsInChildren<DisplayActor>())
                    if (actor.HasThreat) monsters.Add(actor);
            SetWeight(optionsVolume, 0f);
            SetWeight(quitVolume, 0f);
        }

        private void OnDestroy() => DOTween.Kill(this);

        public void SetHover(MenuHover next)
        {
            if (next == hover) return;
            hover = next;
            DOTween.Kill(this);

            float aim = next == MenuHover.GameStart ? 1f : 0f;
            float fov = baseFov - (next == MenuHover.GameStart ? gameStartZoom : 0f);
            var offset = next == MenuHover.GameStart ? gameStartOffset : next == MenuHover.Options ? optionsOffset : Vector3.zero;

            if (player != null)
                Tween(() => player.GetFloat(aimParameter), v => player.SetFloat(aimParameter, v), aim);
            if (menuCamera != null)
                Tween(() => menuCamera.Lens.FieldOfView, SetFov, fov);
            if (cameraSway != null)
                DOTween.To(() => cameraSway.Offset, v => cameraSway.Offset = v, offset, transitionTime)
                    .SetEase(Ease.InOutSine).SetUpdate(true).SetTarget(this);
            if (optionsVolume != null)
                Tween(() => optionsVolume.weight, v => optionsVolume.weight = v, next == MenuHover.Options ? 1f : 0f);
            if (quitVolume != null)
                Tween(() => quitVolume.weight, v => quitVolume.weight = v, next == MenuHover.Quit ? 1f : 0f);
            foreach (var monster in monsters)
            {
                var m = monster;
                Tween(() => m.EmissionBoost, v => m.EmissionBoost = v, next == MenuHover.Quit ? quitEmissionBoost : m.DefaultEmissionBoost);
            }

            if (next == MenuHover.Quit) Roar();
        }

        /// <summary>무작위 몬스터 몇 마리가 조금씩 엇갈려 포효 (쿨다운 중이면 건너뜀)</summary>
        private void Roar()
        {
            if (monsters.Count == 0 || Time.unscaledTime < nextRoarTime) return;
            nextRoarTime = Time.unscaledTime + roarCooldown;

            int count = Mathf.Min(roarCount, monsters.Count);
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(i, monsters.Count); // 앞쪽 i 개는 이미 뽑힌 것 (부분 셔플)
                (monsters[i], monsters[pick]) = (monsters[pick], monsters[i]);
                var monster = monsters[i];
                // 포효는 호버가 바뀌어도 끝까지 가도록 SetTarget(this) 를 하지 않는다 (SetHover 의 Kill 대상에서 빠짐)
                DOVirtual.DelayedCall(Random.value * roarSpread, () => { if (monster != null) monster.PlayThreat(); }, true);
            }
        }

        private void SetFov(float fov)
        {
            var lens = menuCamera.Lens;
            lens.FieldOfView = fov;
            menuCamera.Lens = lens;
        }

        private void Tween(DOGetter<float> getter, DOSetter<float> setter, float to)
        {
            DOTween.To(getter, setter, to, transitionTime).SetEase(Ease.InOutSine).SetUpdate(true).SetTarget(this);
        }

        private static void SetWeight(Volume volume, float weight)
        {
            if (volume != null) volume.weight = weight;
        }
    }
}
