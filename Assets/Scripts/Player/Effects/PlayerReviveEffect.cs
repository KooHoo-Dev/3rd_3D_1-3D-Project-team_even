using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.Player
{
    /// <summary>
    /// 부활 연출 "가상 공간이 현실을 다시 쓴다". PlayerController.IsReviving·ReviveProgress 를 읽어 그린다 (상태는 건드리지 않음).
    ///  1) 덮어쓰기 (0 ~ ReviveBodyRestoreAt): 머리 위에서 발끝으로 홀로그램이 몸을 덮고, 글리치가 점점 세진다. 화면도 글리치
    ///  2) 다시 쓰기 순간: 글리치 최대 + 파편 폭발. 이때 몸이 선 자세로 바뀐다 (홀로그램이 덮고 있어 튀어 보이지 않음)
    ///  3) 복원 (~ 1): 발끝에서 머리로 홀로그램이 걷히며 현실(원래 재질)이 돌아오고, 발밑 원에서 디지털 파편이 피어오른다
    ///  4) 부활 직후 보호: 몸·화면 가장자리에 옅은 잔광, 움직이면 꼬리처럼 남는 파편. 끝나기 직전 깜빡인다
    /// 몸 렌더러에는 연출 동안만 홀로그램 재질을 하나 더 붙였다가 뗀다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerReviveEffect : MonoBehaviour
    {
        [Header("재질 (Assets/Scenes/MS/Effects/Revive)")]
        [SerializeField] private Material hologramMaterial;
        [SerializeField] private Material screenMaterial;
        [SerializeField] private Material bitsMaterial;

        [Header("몸")]
        [Tooltip("홀로그램을 씌울 렌더러를 찾는 뿌리. 비우면 자식 Animator 의 오브젝트")]
        [SerializeField] private Transform bodyRoot;
        [Tooltip("발(플레이어 위치)에서 머리 위까지 높이(m). 홀로그램 경계가 이 범위를 훑는다")]
        [SerializeField] private float bodyHeight = 1.95f;
        [Tooltip("평소 글리치 세기. 다시 쓰기 순간에는 1 까지 오른다")]
        [SerializeField, Range(0f, 1f)] private float baseGlitch = 0.25f;

        [Header("화면")]
        [Tooltip("화면 오버레이 전체 불투명도 배율 (글자는 영향 없음). 낮을수록 화면이 덜 가려진다")]
        [SerializeField, Range(0f, 1f)] private float screenOpacity = 0.5f;
        [Tooltip("3인칭에서는 몸이 보이므로 화면 오버레이를 이만큼 약하게")]
        [SerializeField, Range(0f, 1f)] private float thirdPersonScreenScale = 0.45f;
        [SerializeField] private bool showText = true;

        [Header("파편")]
        [SerializeField] private Color bitsColorA = new Color(0.35f, 1f, 1f, 1f);
        [SerializeField] private Color bitsColorB = new Color(0.6f, 0.75f, 1f, 1f);
        [Tooltip("복원 중 초당 피어오르는 파편 수")]
        [SerializeField] private float curtainRate = 90f;
        [Tooltip("다시 쓰기 순간 터지는 파편 수")]
        [SerializeField] private int burstCount = 90;

        [Header("부활 직후 보호 (피해 경감·스태미나 무한·이속 증가)")]
        [Tooltip("몸에 남는 홀로그램 잔광 세기")]
        [SerializeField, Range(0f, 1f)] private float buffBodyIntensity = 0.22f;
        [Tooltip("1인칭 화면 가장자리 잔광 세기 (3인칭은 thirdPersonScreenScale 배)")]
        [SerializeField, Range(0f, 1f)] private float buffScreenIntensity = 0.22f;
        [Tooltip("움직이면 꼬리처럼 남는 파편 (초당)")]
        [SerializeField] private float buffTrailRate = 40f;
        [Tooltip("끝나기 이 시간(초) 전부터 깜빡여 곧 끝남을 알린다")]
        [SerializeField] private float buffWarningTime = 1.2f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BuildHeightId = Shader.PropertyToID("_BuildHeight");
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        private static readonly int ScanId = Shader.PropertyToID("_Scan");

        private PlayerController player;
        private Material hologram;
        private Material screen;
        private readonly List<Renderer> covered = new List<Renderer>();
        private bool active;

        private GameObject canvasObject;
        private TextMeshProUGUI label;
        private ParticleSystem curtain;
        private ParticleSystem burst;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            if (bodyRoot == null)
            {
                var animator = GetComponentInChildren<Animator>();
                bodyRoot = animator != null ? animator.transform : transform;
            }
            if (hologramMaterial != null) hologram = new Material(hologramMaterial) { name = hologramMaterial.name + " (Revive)" };
            if (screenMaterial != null) screen = new Material(screenMaterial) { name = screenMaterial.name + " (Revive)" };
            CreateScreen();
            curtain = CreateCurtain();
            burst = CreateBurst();
        }

        private void OnEnable()
        {
            if (player != null) player.ReviveBodyRestored += OnBodyRestored;
        }

        private void OnDisable()
        {
            if (player != null) player.ReviveBodyRestored -= OnBodyRestored;
            End();
        }

        private void OnDestroy()
        {
            if (hologram != null) Destroy(hologram);
            if (screen != null) Destroy(screen);
        }

        private void LateUpdate()
        {
            if (player == null) return;
            bool want = player.IsReviving || player.IsReviveBuffActive;
            if (want && active == false) Begin();
            else if (want == false && active) End();
            if (active == false) return;
            if (player.IsReviving) Apply(player.ReviveProgress, player.ReviveBodyRestoreAt);
            else ApplyBuff();
        }

        /// <summary>부활 직후 보호: 몸 전체에 옅은 잔광, 화면 가장자리 잔광, 움직이면 남는 파편. 끝나기 직전 깜빡임</summary>
        private void ApplyBuff()
        {
            float remaining = player.ReviveBuffRemainingTime;
            bool warning = remaining < buffWarningTime;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
            float blink = warning ? (Mathf.Repeat(Time.time * 8f, 1f) < 0.5f ? 1f : 0.25f) : 1f;
            float fade = Mathf.Clamp01(remaining / 0.3f); // 마지막 0.3초 동안 사라짐

            if (hologram != null)
            {
                hologram.SetFloat(IntensityId, buffBodyIntensity * pulse * blink * fade);
                hologram.SetFloat(BuildHeightId, transform.position.y - 1f); // 몸 전체
                hologram.SetFloat(GlitchId, warning ? 0.3f : 0.08f);
            }

            float screenIntensity = buffScreenIntensity * pulse * blink * fade;
            if (player.IsFirstPerson == false) screenIntensity *= thirdPersonScreenScale;
            if (screen != null)
            {
                screen.SetFloat(IntensityId, screenIntensity * screenOpacity);
                screen.SetFloat(GlitchId, warning ? 0.25f : 0f);
                screen.SetFloat(ScanId, Mathf.Repeat(Time.time * 0.5f, 1f));
            }

            if (label != null)
            {
                label.text = $"OVERCLOCK  {remaining:0.0}s";
                label.alpha = 0.6f * blink * fade;
            }

            if (curtain != null)
            {
                curtain.transform.position = transform.position + Vector3.up * 0.02f;
                SetRate(curtain, buffTrailRate * fade);
            }
        }

        private void Begin()
        {
            active = true;
            CoverBody();
            if (canvasObject != null) canvasObject.SetActive(true);
        }

        private void End()
        {
            if (active == false) return;
            active = false;
            UncoverBody();
            if (canvasObject != null) canvasObject.SetActive(false);
            if (curtain != null) SetRate(curtain, 0f);
        }

        private void OnBodyRestored()
        {
            if (burst == null) return;
            burst.transform.position = transform.position + Vector3.up * (bodyHeight * 0.5f);
            burst.Emit(burstCount);
        }

        /// <summary>진행도 p(0~1), 몸 복구 지점 r 에 맞춰 재질·화면·파편 값을 정한다</summary>
        private void Apply(float p, float r)
        {
            float t1 = r > 0f ? Mathf.Clamp01(p / r) : 1f;              // 덮어쓰기 진행
            float t2 = r < 1f ? Mathf.Clamp01((p - r) / (1f - r)) : 1f; // 복원 진행
            bool restoring = p >= r;

            float feet = transform.position.y - 0.05f;
            float top = transform.position.y + bodyHeight + 0.1f;
            float build = restoring
                ? Mathf.Lerp(feet, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t2 / 0.9f)))
                : Mathf.Lerp(top, feet, 1f - (1f - t1) * (1f - t1));

            float peak = Mathf.Exp(-Mathf.Pow((p - r) / 0.07f, 2f));
            float glitch = Mathf.Clamp01(baseGlitch * (restoring ? 1f - t2 : 1f) + peak * (1f - baseGlitch));
            float bodyIntensity = restoring ? 1f - Mathf.Clamp01((t2 - 0.9f) / 0.1f) : Mathf.Clamp01(t1 * 3f);

            if (hologram != null)
            {
                hologram.SetFloat(IntensityId, bodyIntensity);
                hologram.SetFloat(BuildHeightId, build);
                hologram.SetFloat(GlitchId, glitch);
            }

            float screenIntensity = restoring ? 1f - Mathf.Pow(t2, 1.5f) : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t1 * 1.5f));
            if (player.IsFirstPerson == false) screenIntensity *= thirdPersonScreenScale;
            if (screen != null)
            {
                screen.SetFloat(IntensityId, screenIntensity * screenOpacity);
                screen.SetFloat(GlitchId, glitch);
                screen.SetFloat(ScanId, Mathf.Repeat(Time.time * 0.9f, 1f));
            }

            if (label != null)
            {
                label.text = restoring ? $"RECONSTRUCTING  {Mathf.RoundToInt(t2 * 100f)}%" : "REALITY OVERRIDE";
                bool blink = Random.value < glitch * 0.35f;
                label.alpha = blink ? 0.15f : Mathf.Clamp01(screenIntensity * 2f);
            }

            if (curtain != null)
            {
                curtain.transform.position = transform.position + Vector3.up * 0.02f;
                SetRate(curtain, restoring && t2 < 0.95f ? curtainRate : curtainRate * 0.25f * t1);
            }
        }

        #region 몸 재질
        private void CoverBody()
        {
            covered.Clear();
            if (hologram == null || bodyRoot == null) return;
            var renderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r is SkinnedMeshRenderer == false && r is MeshRenderer == false) continue;
                Material[] mats = r.sharedMaterials;
                if (System.Array.IndexOf(mats, hologram) >= 0) continue;
                var withHologram = new Material[mats.Length + 1];
                mats.CopyTo(withHologram, 0);
                withHologram[mats.Length] = hologram;
                r.sharedMaterials = withHologram;
                covered.Add(r);
            }
        }

        /// <summary>붙인 홀로그램만 뺀다 (그 사이 다른 재질이 바뀌었어도 되돌리지 않음)</summary>
        private void UncoverBody()
        {
            for (int i = 0; i < covered.Count; i++)
            {
                Renderer r = covered[i];
                if (r == null) continue;
                Material[] mats = r.sharedMaterials;
                var kept = new List<Material>(mats.Length);
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != hologram) kept.Add(mats[m]);
                r.sharedMaterials = kept.ToArray();
            }
            covered.Clear();
        }
        #endregion

        #region 화면 · 파편 만들기
        private void CreateScreen()
        {
            if (screen == null) return;
            canvasObject = new GameObject("ReviveScreen", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var image = new GameObject("Overlay", typeof(RectTransform)).AddComponent<RawImage>();
            image.transform.SetParent(canvasObject.transform, false);
            Stretch(image.rectTransform);
            image.material = screen;
            image.raycastTarget = false;

            if (showText && TMP_Settings.defaultFontAsset != null)
            {
                label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                label.transform.SetParent(canvasObject.transform, false);
                RectTransform rt = label.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.18f);
                rt.anchorMax = new Vector2(1f, 0.26f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                // UI 리디자인: 숫자·영문 글꼴(Aldrich) · 달빛 흰색 (글꼴이 없으면 TMP 기본)
                label.font = UI.Theme.UiTheme.NumberFont != null ? UI.Theme.UiTheme.NumberFont : TMP_Settings.defaultFontAsset;
                label.fontSize = 30f;
                label.characterSpacing = 18f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = UI.Theme.UiTheme.MoonWhite;
                label.raycastTarget = false;
            }
            canvasObject.SetActive(false);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>발밑 원 테두리에서 위로 곧게 피어오르는 파편</summary>
        private ParticleSystem CreateCurtain()
        {
            ParticleSystem ps = CreateSystem("ReviveCurtain", out var renderer);
            if (ps == null) return null;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone; // 각도 0 원뿔 = 원 테두리에서 위로 곧게
            shape.angle = 0f;
            shape.radius = 0.55f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var noise = ps.noise; // 위로 오르며 디지털하게 떨림
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 2.5f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            renderer.sortingFudge = -10f;
            ps.gameObject.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>다시 쓰기 순간 가슴 높이에서 사방으로 터지는 파편</summary>
        private ParticleSystem CreateBurst()
        {
            ParticleSystem ps = CreateSystem("ReviveBurst", out _);
            if (ps == null) return null;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.gravityModifier = 0.2f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.5f;
            limit.dampen = 0.12f;

            ps.gameObject.SetActive(true);
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateSystem(string objectName, out ParticleSystemRenderer renderer)
        {
            renderer = null;
            if (bitsMaterial == null) return null;
            var go = new GameObject(objectName);
            go.SetActive(false); // 설정이 끝나기 전에 재생되지 않게
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(bitsColorA, bitsColorB);
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = bitsMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }
        #endregion
    }
}
