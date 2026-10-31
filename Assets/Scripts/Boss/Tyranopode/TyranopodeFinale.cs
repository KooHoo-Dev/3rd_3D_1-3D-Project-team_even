using System;
using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.UI;
using HybridShooter.UI.MainMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>최후 연출 시간표·문구 (TyranopodeController 인스펙터)</summary>
    [Serializable]
    public struct TyranopodeFinaleSettings
    {
        [Tooltip("보스가 커지는 시간(초)")] public float GrowTime;
        [Tooltip("최종 크기 배율")] public float GrowScale;
        [Tooltip("지원 도착 문구가 뜨는 시각(초)")] public float MessageTime;
        [Tooltip("조준 레이저가 내려오는 시각(초)")] public float LaserTime;
        [Tooltip("궤도 타격 시각(초)")] public float StrikeTime;
        [Tooltip("타격 뒤 클리어 화면까지(초)")] public float AftermathTime;
        public string SupportTitle;
        public string SupportSubtitle;
        public string ClearTitle;
        public string ClearSubtitle;
        public string MainMenuButton;
        [Tooltip("메인 메뉴 씬 이름 (Build Settings)")] public string MainMenuScene;

        public static TyranopodeFinaleSettings Default => new TyranopodeFinaleSettings
        {
            GrowTime = 4.5f,
            GrowScale = 1.35f,
            MessageTime = 2.5f,
            LaserTime = 4.8f,
            StrikeTime = 6.2f,
            AftermathTime = 3.5f,
            SupportTitle = "궤도 지원 도착",
            SupportSubtitle = "목표 고정 — 정밀 타격 개시",
            ClearTitle = "작전 성공",
            ClearSubtitle = "GAME CLEAR",
            MainMenuButton = "메인 메뉴로",
            MainMenuScene = "Test_MainMenu",
        };
    }

    /// <summary>최후 연출에 쓰는 에셋 (TyranopodeController 인스펙터, 빌더가 채운다)</summary>
    [Serializable]
    public struct TyranopodeFinaleAssets
    {
        [Tooltip("보스 둘레 땅에서 힘이 빨려 드는 연출 (반복)")] public GameObject GroundEnergyVfx;
        [Tooltip("궤도 타격 착탄 (섬광·충격파·파편)")] public GameObject StrikeVfx;
        [Tooltip("레이저·빔 머티리얼 (가산 블렌드)")] public Material BeamMaterial;
        [Tooltip("문구 글꼴 (한글)")] public TMP_FontAsset Font;

        public bool IsComplete => GroundEnergyVfx != null && StrikeVfx != null && BeamMaterial != null && Font != null;
    }

    /// <summary>
    /// Tyranopode 최후 연출 (체력이 다하는 순간 TyranopodeController 가 만든다). 보스와 따로 씬에 있어 보스가 사라져도 끝까지 진행한다.
    ///  1) 시네마틱 카메라로 전환 (위아래 검은 띠, 플레이어 입력 차단·무적). 보스가 포효하며 땅의 힘을 빨아들이고 점점 커진다
    ///  2) "궤도 지원 도착" 문구
    ///  3) 하늘에서 조준 레이저 → 거대한 궤도 빔이 내리꽂혀 보스 처치 (BossController 사망 → 기존 보상·사운드 흐름)
    ///  4) 잔해를 비추다 클리어 화면 (GameClearedEvent, [메인 메뉴로])
    /// </summary>
    public sealed class TyranopodeFinale : MonoBehaviour
    {
        private const float SkyHeight = 300f;
        private const float BeamWidth = 14f;

        private TyranopodeController boss;
        private TyranopodeFinaleSettings settings;
        private TyranopodeFinaleAssets assets;
        private GroundTelegraph threat;

        private TyranopodeCinematicRig rig;
        private Vector3 focus;
        private Vector3 bossForward;
        private float startTime;

        private GameObject energy;
        private LineRenderer laser;
        private LineRenderer beam;
        private CanvasGroup message;
        private CanvasGroup clear;

        private bool messageShown;
        private bool laserShown;
        private bool struck;
        private bool cleared;

        public void Begin(TyranopodeController controller, TyranopodeFinaleSettings finale, TyranopodeFinaleAssets finaleAssets, GroundTelegraph threatTelegraph)
        {
            boss = controller;
            settings = finale;
            assets = finaleAssets;
            threat = threatTelegraph;
            startTime = Time.time;
            focus = controller.transform.position;
            bossForward = controller.transform.forward;

            ServiceLocator.Get<IPlayerCinematic>().SetCinematic(true);
            rig = new TyranopodeCinematicRig(transform, "Finale");
            PlaceCamera(0f);
            BuildUi();
            energy = Instantiate(assets.GroundEnergyVfx, focus, Quaternion.identity);
            laser = NewLine("OrbitalLaser", 0.35f, new Color(1f, 0.15f, 0.1f, 0.9f));
            beam = NewLine("OrbitalBeam", 0f, new Color(1f, 0.9f, 0.7f, 1f));
            rig.Shake(0.08f, settings.StrikeTime);
        }

        // ───────────────────────── 시간표 ─────────────────────────

        private void Update()
        {
            float t = Time.time - startTime;

            if (struck == false && boss != null)
            {
                float grow = Mathf.SmoothStep(0f, 1f, t / settings.GrowTime);
                boss.FinaleGrow(Mathf.Lerp(1f, settings.GrowScale, grow));
                // 힘을 모을수록 흔들림이 커진다
                rig.ShakeStrength = Mathf.Lerp(0.05f, 0.25f, grow);
            }

            if (messageShown == false && t >= settings.MessageTime)
            {
                messageShown = true;
                message.gameObject.SetActive(true);
            }
            if (message.gameObject.activeSelf)
                message.alpha = struck ? Mathf.MoveTowards(message.alpha, 0f, Time.deltaTime * 3f) : Mathf.MoveTowards(message.alpha, 1f, Time.deltaTime * 2.5f);

            if (laserShown == false && t >= settings.LaserTime)
            {
                laserShown = true;
                laser.enabled = true;
            }
            if (laserShown && struck == false)
            {
                // 깜빡이며 조준
                laser.widthMultiplier = 0.35f * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 25f)));
                SetLine(laser, focus);
            }

            if (struck == false && t >= settings.StrikeTime) Strike();
            if (struck) UpdateBeam(t - settings.StrikeTime);

            if (cleared == false && struck && t >= settings.StrikeTime + settings.AftermathTime) ShowClear();
            if (cleared) clear.alpha = Mathf.MoveTowards(clear.alpha, 1f, Time.unscaledDeltaTime * 1.5f);

            rig.SetBars(t / 0.6f);
        }

        private void Strike()
        {
            struck = true;
            laser.enabled = false;
            beam.enabled = true;
            SetLine(beam, focus);
            threat.Hide();
            Destroy(energy);
            Destroy(Instantiate(assets.StrikeVfx, focus, Quaternion.identity), 8f);
            rig.Shake(1f, 1.6f);
            if (boss != null) boss.FinaleKill();
        }

        private void UpdateBeam(float since)
        {
            // 0.25초 굵어짐 → 0.6초 유지 → 0.8초 사라짐
            float width = since < 0.25f ? since / 0.25f : since < 0.85f ? 1f : Mathf.Clamp01(1f - (since - 0.85f) / 0.8f);
            beam.widthMultiplier = BeamWidth * width * (0.9f + 0.1f * Mathf.Sin(Time.time * 40f));
            if (width <= 0f) beam.enabled = false;
        }

        private void ShowClear()
        {
            cleared = true;
            clear.gameObject.SetActive(true);
            EventBus<GameClearedEvent>.Publish(new GameClearedEvent());
        }

        private void GoToMainMenu()
        {
            ServiceLocator.Get<IPlayerCinematic>().SetCinematic(false);
            MainMenuController.SkipPressAnyKey = true;
            SceneTransition.LoadAsync(settings.MainMenuScene, 0.6f, this.GetCancellationTokenOnDestroy()).Forget();
        }

        // ───────────────────────── 카메라 ─────────────────────────

        private void LateUpdate() => PlaceCamera(Time.time - startTime);

        /// <summary>
        /// 컷 A(힘을 모음): 낮은 앵글로 천천히 돌며 다가간다 / 컷 B(조준·타격): 높은 곳에서 내려다본다 / 컷 C(잔해): 천천히 물러난다
        /// </summary>
        private void PlaceCamera(float t)
        {
            float scale = boss != null ? boss.transform.localScale.x : settings.GrowScale;
            Vector3 center = focus + Vector3.up * 4f * scale;
            Quaternion around = Quaternion.LookRotation(bossForward);
            Vector3 position;

            if (t < settings.LaserTime)
            {
                float k = t / settings.LaserTime;
                float yaw = Mathf.Lerp(25f, 70f, k);
                float distance = Mathf.Lerp(30f, 22f, k) * scale;
                position = center + around * (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward) * distance + Vector3.up * Mathf.Lerp(3f, 6f, k);
            }
            else if (t < settings.StrikeTime + 1.2f)
            {
                position = center + around * new Vector3(0.4f, 0f, 1f).normalized * 40f + Vector3.up * 32f;
            }
            else
            {
                float k = Mathf.Clamp01((t - settings.StrikeTime - 1.2f) / settings.AftermathTime);
                position = center + around * (Quaternion.Euler(0f, Mathf.Lerp(-20f, -45f, k), 0f) * Vector3.forward) * Mathf.Lerp(28f, 40f, k) + Vector3.up * Mathf.Lerp(10f, 16f, k);
            }

            rig.Place(position, Quaternion.LookRotation(center + Vector3.up * 2f - position));
            if (struck && rig.IsShaking) rig.ShakeStrength = Mathf.MoveTowards(rig.ShakeStrength, 0f, Time.deltaTime * 0.6f);
        }

        // ───────────────────────── 빔 ─────────────────────────

        private LineRenderer NewLine(string lineName, float width, Color color)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = assets.BeamMaterial;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.enabled = false;
            return line;
        }

        private static void SetLine(LineRenderer line, Vector3 ground)
        {
            line.SetPosition(0, ground + Vector3.up * SkyHeight);
            line.SetPosition(1, ground);
        }

        // ───────────────────────── 화면 문구 ─────────────────────────

        private void BuildUi()
        {
            LoadoutPanel.EnsureEventSystem();
            var root = rig.Canvas;

            message = Group(root, "SupportMessage");
            Text(message.transform, settings.SupportTitle, 64f, new Color(1f, 0.85f, 0.4f), new Vector2(0f, 120f));
            Text(message.transform, settings.SupportSubtitle, 30f, Color.white, new Vector2(0f, 60f));
            message.alpha = 0f;
            message.gameObject.SetActive(false);

            clear = Group(root, "ClearScreen");
            var dim = clear.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);
            Text(clear.transform, settings.ClearTitle, 80f, new Color(1f, 0.85f, 0.4f), new Vector2(0f, 90f));
            Text(clear.transform, settings.ClearSubtitle, 36f, Color.white, new Vector2(0f, 10f));
            Button(clear.transform, settings.MainMenuButton, new Vector2(0f, -110f));
            clear.alpha = 0f;
            clear.gameObject.SetActive(false);
        }

        private static CanvasGroup Group(RectTransform parent, string groupName)
        {
            var go = new GameObject(groupName, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.GetComponent<CanvasGroup>();
        }

        private TextMeshProUGUI Text(Transform parent, string content, float size, Color color, Vector2 position)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = assets.Font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(1600f, size * 1.6f);
            return text;
        }

        private void Button(Transform parent, string label, Vector2 position)
        {
            var go = new GameObject("MainMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.1f, 0.95f);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(360f, 80f);
            go.GetComponent<Button>().onClick.AddListener(GoToMainMenu);
            var text = Text(go.transform, label, 32f, Color.white, Vector2.zero);
            ((RectTransform)text.transform).sizeDelta = rect.sizeDelta;
        }

        private void OnDestroy()
        {
            if (energy != null) Object.Destroy(energy);
        }
    }
}
