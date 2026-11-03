using System.Collections.Generic;
using HybridShooter.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.UI.Options
{
    /// <summary>
    /// 옵션 [조작] 탭의 키 안내 (읽기 전용). 리바인딩은 하지 않는다.
    /// 플레이어는 InputSystem_Actions 가 아니라 PlayerController.InputPlayerSettings 의 키를 직접 읽으므로 (2026-10-28 기준)
    /// 같은 기본값을 여기서 읽어 표시한다 → 키 기본값이 바뀌면 안내도 따라 바뀐다. 마우스·휠은 코드에 고정이라 문구로 둔다.
    /// 키 이름은 현재 키보드 배열의 표시 이름 (키보드가 없으면 Key 이름)
    /// </summary>
    public sealed class KeyGuideList : MonoBehaviour
    {
        [Tooltip("줄을 만들어 넣을 곳 (Grid/Vertical LayoutGroup)")]
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float fontSize = 21f;
        [Tooltip("설명 글자 (UiTheme Steel)")]
        [SerializeField] private Color labelColor = new Color(0.6f, 0.68f, 0.78f);
        [Tooltip("키 이름 (UiTheme 붉은 글자, 메인 메뉴 조작 힌트와 같은 색)")]
        [SerializeField] private Color keyColor = new Color(0.88f, 0.34f, 0.3f);

        private bool built;

        private void OnEnable() => Build();

        private void Build()
        {
            if (built || content == null) return;
            built = true;
            foreach (var (label, keys) in Rows()) CreateRow(label, keys);
        }

        /// <summary>표시 순서대로 (설명, 키)</summary>
        private static IEnumerable<(string, string)> Rows()
        {
            var k = new PlayerController.InputPlayerSettings();
            yield return ("이동", $"{Name(k.frontKey)} {Name(k.leftKey)} {Name(k.backKey)} {Name(k.rightKey)}");
            yield return ("달리기", Name(k.sprintKey));
            yield return ("점프", Name(k.jumpKey));
            yield return ("구르기", Name(k.RollKey));
            yield return ("사격", "마우스 왼쪽");
            yield return ("조준", "마우스 오른쪽");
            yield return ("재장전", Name(k.reloadKey));
            yield return ("사격 모드", Name(k.fireModeKey));
            yield return ("무기 교체", $"{Name(k.slotKeys[0])}~{Name(k.slotKeys[k.slotKeys.Length - 1])} / 휠");
            yield return ("수류탄", Name(k.grenadeKey));
            yield return ("회복", Name(k.healKey));
            yield return ("상호작용", Name(k.interactKey));
            yield return ("시점 전환", Name(k.viewToggleKey));
            yield return ("커서 풀기", Name(k.cursorToggleKey));
        }

        private static string Name(Key key)
        {
            if (key == Key.None) return "-";
            var keyboard = Keyboard.current;
            return keyboard != null ? keyboard[key].displayName : key.ToString();
        }

        /// <summary>왼쪽 설명 + 오른쪽 키. 크기는 content 의 LayoutGroup 이 정한다</summary>
        private void CreateRow(string label, string keys)
        {
            var row = new GameObject(label, typeof(RectTransform));
            row.transform.SetParent(content, false);
            CreateText(row.transform, "Label", label, labelColor, TextAlignmentOptions.MidlineLeft, 0f, 0.5f);
            CreateText(row.transform, "Key", keys, keyColor, TextAlignmentOptions.MidlineRight, 0.5f, 1f);
        }

        private void CreateText(Transform parent, string name, string value, Color color, TextAlignmentOptions align, float xMin, float xMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(xMin, 0f);
            rect.anchorMax = new Vector2(xMax, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }
}
