using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 건너뛰기 장난. 건너뛰기 게이지를 다 채우면 건너뛰는 대신 질문창이 뜬다 (EndingDirector.UpdateSkip 이 Request 를 부른다)
    ///   1회째: "2배로 보시겠습니까?" → 무엇을 골라도 ×1/2, "2배는 너무 적다" 는 ×1/3
    ///   2회째부터: "2배 빨라지길 원하나요?" → 이대로 / 고객센터 문의 안내만 / 원래대로(×1)
    ///   원래대로 돌린 뒤 한 번 더: 질문창이 흔들리다 폭발하고, 다시는 건너뛰기를 받지 않는다
    /// 속도는 Time.timeScale(입자·애니메이터·카메라)과 Timeline 루트 재생 속도(음악 음정까지)를 함께 바꾼다.
    /// Timeline 은 UnscaledGameTime 이라 두 번 느려지지 않는다. 이 창의 연출·입력은 모두 unscaled 시간
    /// </summary>
    public sealed class EndingSkipPrank : MonoBehaviour
    {
        public const string SkipQuestion = "정말 스킵할거야?";
        private const string FirstQuestion = "엔딩 씬을 2배로 보시는것에 동의 하십니까?";
        private static readonly string[] FirstChoices = { "1. 동의함", "2. 찬성함", "3. 지지함", "4. 2배는 너무 적다." };
        private const string SecondQuestion = "2배로 즐기시는것에 동의하셨습니다.\n2배 빨라지시는걸 원하시나요?";
        private static readonly string[] SecondChoices = { "1. 이대로 좋다", "2. 당연하다", "3. 원래대로 돌려달라." };
        private const string CustomerService = "고객센터에 문의해주세요";

        /// <summary>폰트 아틀라스에 미리 구울 글자 (EndingSceneBuilder)</summary>
        public static string AllCharacters =>
            SkipQuestion + FirstQuestion + string.Concat(FirstChoices) + SecondQuestion + string.Concat(SecondChoices) + CustomerService;

        [SerializeField] private PlayableDirector timeline;

        [Header("화면")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform panel;
        [SerializeField] private TMP_Text question;
        [SerializeField] private TMP_Text[] choices;
        [SerializeField] private TMP_Text message;
        [Tooltip("폭발 때 날아가는 창 조각 (창과 같은 자리에 꺼져 있다)")]
        [SerializeField] private RectTransform[] shards;
        [Tooltip("폭발 섬광 (둥근 빛)")]
        [SerializeField] private Graphic boom;
        [SerializeField] private Color normalColor = new Color(0.6f, 0.68f, 0.78f);
        [SerializeField] private Color selectedColor = new Color(1f, 0.83f, 0.34f);

        [Header("소리 (SoundTable 키)")]
        [SerializeField] private string cursorSound = "ui_menu_cursor";
        [SerializeField] private string selectSound = "ui_menu_select";
        [SerializeField] private string explodeSound = "sfx_explosion_large";
        [SerializeField] private string explodeTailSound = "sfx_fuelcell_burn";

        [Header("시간 (초)")]
        [SerializeField] private float openTime = 0.2f;
        [Tooltip("'고객센터에 문의해주세요' 가 떠 있는 시간")]
        [SerializeField] private float messageTime = 2.2f;
        [Tooltip("폭발 전 점점 세게 흔들리는 시간")]
        [SerializeField] private float shakeTime = 0.9f;
        [SerializeField] private float shakeAmount = 18f;
        [SerializeField] private float shardTime = 1.6f;

        private enum Stage { FirstAsk, SlowAsk, Restored, Exploded }
        private enum Phase { Hidden, Choosing, Message, Shaking, Exploding }

        private Stage stage;
        private Phase phase;
        private float phaseTime;
        private int selected;
        private int choiceCount;
        private Vector2 panelHome;
        private Vector2[] shardHome;
        private Vector2[] shardVelocity;
        private float[] shardSpin;
        private Graphic[] shardGraphics;

        /// <summary>질문창이 떠 있다 (폭발 연출 중 포함). 이 동안 EndingDirector 는 건너뛰기 게이지를 받지 않는다</summary>
        public bool IsOpen => phase != Phase.Hidden;
        /// <summary>창이 폭발했다 — 이후로는 건너뛰기 안내도 게이지도 없다</summary>
        public bool Exploded => stage == Stage.Exploded;

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
            group.alpha = 0f;
        }

        private void OnDestroy()
        {
            // 느려진 채로 메인 메뉴·게임으로 넘어가지 않게 (RunDirector 는 일시정지 해제 때 이전 timeScale 을 되살린다)
            Time.timeScale = 1f;
        }

        /// <summary>건너뛰기 게이지가 다 찼다</summary>
        public void Request()
        {
            if (IsOpen) return;
            switch (stage)
            {
                case Stage.FirstAsk: Ask(FirstQuestion, FirstChoices); break;
                case Stage.SlowAsk: Ask(SecondQuestion, SecondChoices); break;
                case Stage.Restored:
                    stage = Stage.Exploded;
                    Open(Phase.Shaking, SkipQuestion, 0);
                    break;
            }
        }

        /// <summary>엔딩이 끝나 메인 메뉴로 넘어간다: 창을 거두고 속도를 되돌린다</summary>
        public void Dismiss()
        {
            phase = Phase.Hidden;
            group.alpha = 0f;
            SetSpeed(1f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            phaseTime += dt;

            bool shown = phase == Phase.Choosing || phase == Phase.Message || phase == Phase.Shaking;
            if (phase != Phase.Exploding)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, shown ? 1f : 0f, dt / openTime);
                panel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Mathf.SmoothStep(0f, 1f, group.alpha));
            }

            switch (phase)
            {
                case Phase.Choosing: UpdateChoosing(); break;
                case Phase.Message:
                    if (phaseTime >= messageTime) phase = Phase.Hidden;
                    break;
                case Phase.Shaking:
                    float amount = shakeAmount * Mathf.Clamp01(phaseTime / shakeTime);
                    panel.anchoredPosition = panelHome + Random.insideUnitCircle * amount;
                    if (phaseTime >= shakeTime) Explode();
                    break;
                case Phase.Exploding: UpdateExplosion(dt); break;
            }
        }

        // ───────────────────────── 질문 ─────────────────────────

        private void Ask(string text, string[] options)
        {
            for (int i = 0; i < choices.Length; i++)
                choices[i].text = i < options.Length ? options[i] : "";
            Open(Phase.Choosing, text, options.Length);
        }

        private void Open(Phase next, string text, int count)
        {
            phase = next;
            phaseTime = 0f;
            choiceCount = count;
            selected = 0;
            question.text = text;
            question.gameObject.SetActive(true);
            message.gameObject.SetActive(false);
            for (int i = 0; i < choices.Length; i++) choices[i].gameObject.SetActive(i < count);
            panel.anchoredPosition = panelHome;
            panel.gameObject.SetActive(true);
            Highlight();
        }

        private void UpdateChoosing()
        {
            // 게이지를 채우던 키는 이미 눌린 채라 wasPressedThisFrame 에 걸리지 않는다 — 창이 뜨자마자 골라지지 않는다
            int pick = -1;
            for (int i = 0; i < choiceCount; i++)
                if (DigitPressed(i)) pick = i;

            int move = (UpPressed() ? -1 : 0) + (DownPressed() ? 1 : 0);
            if (move != 0)
            {
                selected = (selected + move + choiceCount) % choiceCount;
                Highlight();
                Play(cursorSound);
            }
            if (pick < 0 && ConfirmPressed()) pick = selected;
            if (pick >= 0) Choose(pick);
        }

        private void Choose(int pick)
        {
            selected = pick;
            Highlight();
            Play(selectSound);

            if (stage == Stage.FirstAsk)
            {
                SetSpeed(pick == 3 ? 1f / 3f : 0.5f);
                stage = Stage.SlowAsk;
                phase = Phase.Hidden;
                return;
            }

            switch (pick)
            {
                case 0: phase = Phase.Hidden; break;
                case 1: ShowMessage(CustomerService); break;
                default:
                    SetSpeed(1f);
                    stage = Stage.Restored;
                    phase = Phase.Hidden;
                    break;
            }
        }

        /// <summary>질문·선택지를 지우고 문구 하나만 띄운다</summary>
        private void ShowMessage(string text)
        {
            phase = Phase.Message;
            phaseTime = 0f;
            question.gameObject.SetActive(false);
            foreach (var choice in choices) choice.gameObject.SetActive(false);
            message.text = text;
            message.gameObject.SetActive(true);
        }

        private void Highlight()
        {
            for (int i = 0; i < choices.Length; i++)
                choices[i].color = i == selected ? selectedColor : normalColor;
        }

        private void SetSpeed(float speed)
        {
            Time.timeScale = speed;
            if (timeline.playableGraph.IsValid()) timeline.playableGraph.GetRootPlayable(0).SetSpeed(speed);
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

            if (phaseTime < shardTime) return;
            phase = Phase.Hidden;
            group.alpha = 0f;
            foreach (var shard in shards) shard.gameObject.SetActive(false);
            boom.gameObject.SetActive(false);
        }

        // ───────────────────────── 입력·공통 ─────────────────────────

        private static bool DigitPressed(int index)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return index switch
            {
                0 => keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame,
                1 => keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame,
                2 => keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame,
                3 => keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame,
                _ => false,
            };
        }

        private static bool UpPressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame))
                || (pad != null && pad.dpad.up.wasPressedThisFrame);
        }

        private static bool DownPressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame))
                || (pad != null && pad.dpad.down.wasPressedThisFrame);
        }

        private static bool ConfirmPressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

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
