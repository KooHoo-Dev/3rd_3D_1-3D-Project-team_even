using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 건너뛰기 장난. 건너뛰기 게이지를 다 채우면 건너뛰는 대신 "터질듯한 스킵 버튼?" 창이 뜨고,
    /// 흔들리다 폭발한 뒤 "터져서 없어짐" 이 잠깐 떠 있다 (EndingDirector.UpdateSkip 이 Request 를 부른다).
    /// 폭발한 뒤로는 다시 건너뛰기를 받지 않는다. 이 창의 연출은 모두 unscaled 시간
    /// </summary>
    public sealed class EndingSkipPrank : MonoBehaviour
    {
        /// <summary>건너뛰기 안내와 창에 같이 쓰는 문구</summary>
        public const string SkipQuestion = "터질듯한 스킵 버튼?";
        /// <summary>폭발 뒤 가운데에 잠깐 뜨는 문구</summary>
        public const string GoneCaption = "터져서 없어짐";

        /// <summary>폰트 아틀라스에 미리 구울 글자 (EndingSceneBuilder)</summary>
        public static string AllCharacters => SkipQuestion + GoneCaption;

        [Header("화면")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("SkipQuestion 이 적힌 창 (흔들리다 꺼지고 조각으로 바뀐다)")]
        [SerializeField] private RectTransform panel;
        [Tooltip("폭발 때 날아가는 창 조각 (창과 같은 자리에 꺼져 있다)")]
        [SerializeField] private RectTransform[] shards;
        [Tooltip("폭발 섬광 (둥근 빛)")]
        [SerializeField] private Graphic boom;
        [Tooltip("폭발 뒤 가운데에 잠깐 뜨는 GoneCaption (꺼져 있다)")]
        [SerializeField] private TMP_Text caption;

        [Header("소리 (SoundTable 키)")]
        [SerializeField] private string explodeSound = "sfx_explosion_large";
        [SerializeField] private string explodeTailSound = "sfx_fuelcell_burn";

        [Header("시간 (초)")]
        [SerializeField] private float openTime = 0.2f;
        [Tooltip("폭발 전 점점 세게 흔들리는 시간")]
        [SerializeField] private float shakeTime = 0.9f;
        [SerializeField] private float shakeAmount = 18f;
        [SerializeField] private float shardTime = 1.6f;
        [Tooltip("폭발 뒤 '터져서 없어짐' 이 뜨기까지")]
        [SerializeField] private float captionDelay = 0.3f;
        [Tooltip("'터져서 없어짐' 이 다 보이는 시간")]
        [SerializeField] private float captionTime = 1.8f;
        [SerializeField] private float captionFade = 0.4f;

        private enum Phase { Hidden, Shaking, Exploding }

        private Phase phase;
        private float phaseTime;
        private bool exploded;
        private Vector2 panelHome;
        private Vector2[] shardHome;
        private Vector2[] shardVelocity;
        private float[] shardSpin;
        private Graphic[] shardGraphics;

        /// <summary>건너뛰기를 한 번 시도했다 (폭발 연출 중 포함) — 이후로는 건너뛰기 안내도 게이지도 없다</summary>
        public bool Exploded => exploded;

        private void Awake()
        {
            panelHome = panel.anchoredPosition;
            shardHome = new Vector2[shards.Length];
            shardVelocity = new Vector2[shards.Length];
            shardSpin = new float[shards.Length];
            shardGraphics = new Graphic[shards.Length];
            for (int i = 0; i < shards.Length; i++)
            {
                shardHome[i] = shards[i].anchoredPosition;
                shardGraphics[i] = shards[i].GetComponent<Graphic>();
                shards[i].gameObject.SetActive(false);
            }
            boom.gameObject.SetActive(false);
            caption.gameObject.SetActive(false);
            group.alpha = 0f;
        }

        /// <summary>건너뛰기 게이지가 다 찼다: 창을 띄우고 흔들다 폭발시킨다 (한 번만)</summary>
        public void Request()
        {
            if (exploded) return;
            exploded = true;
            phase = Phase.Shaking;
            phaseTime = 0f;
        }

        /// <summary>엔딩이 끝나 메인 메뉴로 넘어간다: 창을 거둔다</summary>
        public void Dismiss()
        {
            phase = Phase.Hidden;
            group.alpha = 0f;
            caption.gameObject.SetActive(false);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            phaseTime += dt;

            if (phase != Phase.Exploding)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, phase == Phase.Shaking ? 1f : 0f, dt / openTime);
                panel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Mathf.SmoothStep(0f, 1f, group.alpha));
            }

            switch (phase)
            {
                case Phase.Shaking:
                    float amount = shakeAmount * Mathf.Clamp01(phaseTime / shakeTime);
                    panel.anchoredPosition = panelHome + Random.insideUnitCircle * amount;
                    if (phaseTime >= shakeTime) Explode();
                    break;
                case Phase.Exploding: UpdateExplosion(dt); break;
            }
        }

        // ───────────────────────── 폭발 ─────────────────────────

        private void Explode()
        {
            phase = Phase.Exploding;
            phaseTime = 0f;
            group.alpha = 1f;
            panel.gameObject.SetActive(false);
            for (int i = 0; i < shards.Length; i++)
            {
                var shard = shards[i];
                shard.gameObject.SetActive(true);
                shard.anchoredPosition = shardHome[i];
                shard.localRotation = Quaternion.identity;
                // 가운데에서 바깥으로 + 위로 조금 튄다
                var outward = shardHome[i].sqrMagnitude > 1f ? shardHome[i].normalized : Random.insideUnitCircle.normalized;
                shardVelocity[i] = outward * Random.Range(500f, 1100f) + Vector2.up * Random.Range(200f, 500f);
                shardSpin[i] = Random.Range(-720f, 720f);
            }
            boom.gameObject.SetActive(true);
            caption.gameObject.SetActive(true);
            SetAlpha(caption, 0f);
            Play(explodeSound);
            Play(explodeTailSound);
        }

        private void UpdateExplosion(float dt)
        {
            float fade = 1f - Mathf.Clamp01(phaseTime / shardTime);
            for (int i = 0; i < shards.Length; i++)
            {
                shardVelocity[i] += Vector2.down * 1800f * dt;
                shards[i].anchoredPosition += shardVelocity[i] * dt;
                shards[i].Rotate(0f, 0f, shardSpin[i] * dt);
                SetAlpha(shardGraphics[i], fade);
            }
            boom.rectTransform.localScale = Vector3.one * (0.3f + phaseTime * 6f);
            SetAlpha(boom, Mathf.Exp(-phaseTime / 0.18f));

            // '터져서 없어짐': 섬광이 걷힐 즈음 떠서 captionTime 동안 있다가 사라진다
            float captionEnd = captionDelay + captionTime + captionFade;
            float shown = Mathf.Min(Mathf.Clamp01((phaseTime - captionDelay) / openTime),
                                    1f - Mathf.Clamp01((phaseTime - captionDelay - captionTime) / captionFade));
            SetAlpha(caption, shown);

            // 조각과 문구가 모두 끝나야 창을 거둔다
            if (phaseTime < Mathf.Max(shardTime, captionEnd)) return;
            phase = Phase.Hidden;
            group.alpha = 0f;
            foreach (var shard in shards) shard.gameObject.SetActive(false);
            boom.gameObject.SetActive(false);
            caption.gameObject.SetActive(false);
        }

        // ───────────────────────── 공통 ─────────────────────────

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
