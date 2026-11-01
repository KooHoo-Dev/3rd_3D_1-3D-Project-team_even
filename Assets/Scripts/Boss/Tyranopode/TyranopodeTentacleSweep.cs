using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 촉수 폭풍의 늘어나는 촉수 (애니메이션이 끝난 뒤 LateUpdate 에서 뼈를 덮어쓴다).
    ///  - 늘이기: 촉수 뼈 사슬의 마디 간격만 벌린다 (굵기는 그대로)
    ///  - 겨누기: 사슬 뿌리를 돌려 촉수 끝이 aim 방향 바닥 가까이(tipHeight)를 향하게 → 낮게 쓸어 점프로 넘을 수 있다
    ///  - 모자란 길이는 촉수 끝에서 이어지는 생체 채찍(TyranopodeBioWhip)이 reach 까지 채운다 → 보이는 끝 = 판정 끝(WhipEnd)
    /// 앞 촉수가 부서졌으면 가운데 촉수로 쓴다. TentacleStormAttack 이 매 프레임 SetAim 으로 이끌고 Release 로 놓는다.
    /// 뼈 사슬은 TyranopodePrefabBuilder 가 채운다 (뿌리 → 끝)
    /// </summary>
    public sealed class TyranopodeTentacleSweep : MonoBehaviour
    {
        public enum Side { Left, Right }

        [SerializeField] private Transform[] frontLeft;
        [SerializeField] private Transform[] frontRight;
        [SerializeField] private Transform[] middleLeft;
        [SerializeField] private Transform[] middleRight;
        [SerializeField] private TyranopodeBioWhip whip;
        [Tooltip("최대로 늘였을 때 마디 간격 배율")]
        [SerializeField, Min(1f)] private float maxStretch = 2.2f;
        [Tooltip("쓸 때 촉수 끝·채찍 높이(m, 바닥 기준). 점프로 넘을 수 있을 만큼 낮게")]
        [SerializeField, Min(0f)] private float tipHeight = 0.6f;

        private Transform[] chain;
        private Vector3[] rest;
        private Vector3[] written;
        private Quaternion restRotation;
        private Quaternion writtenRotation;
        private Vector3 aim = Vector3.forward;
        private float reach;
        private float weight;
        private float stretch;

        public bool HasReferences => Valid(frontLeft) && Valid(frontRight) && Valid(middleLeft) && Valid(middleRight) && whip != null && whip.HasReferences;
        /// <summary>판정 끝: 채찍 끝 (채찍이 없으면 촉수 끝). 쓰는 중이 아니면 보스 위치</summary>
        public Vector3 WhipEnd { get; private set; }
        /// <summary>보스 중심에서 판정 끝까지 수평 거리</summary>
        public float CurrentReach { get; private set; }

        private static bool Valid(Transform[] bones) => bones != null && bones.Length >= 2 && System.Array.TrueForAll(bones, b => b != null);

        /// <summary>쓸 촉수를 고른다 (useMiddle = 앞 촉수가 부서졌을 때). 이전 촉수는 원래대로</summary>
        public void Raise(Side side, bool useMiddle)
        {
            ResetChain();
            chain = useMiddle ? (side == Side.Left ? middleLeft : middleRight) : (side == Side.Left ? frontLeft : frontRight);
            rest = new Vector3[chain.Length];
            written = new Vector3[chain.Length];
            for (int i = 0; i < chain.Length; i++)
            {
                rest[i] = chain[i].localPosition;
                written[i] = new Vector3(float.NaN, 0f, 0f);
            }
            restRotation = chain[0].localRotation;
            writtenRotation = new Quaternion(0f, 0f, 0f, 0f);
            weight = 0f;
            stretch = 0f;
        }

        /// <summary>
        /// 이번 프레임 자세: flatDirection 쪽으로 reach(m)까지. blend 0~1 = 애니메이션 → 겨눈 자세, stretch01 0~1 = 늘인 정도
        /// </summary>
        public void SetAim(Vector3 flatDirection, float reachDistance, float blend, float stretch01)
        {
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude > 0.0001f) aim = flatDirection.normalized;
            reach = reachDistance;
            weight = Mathf.Clamp01(blend);
            stretch = Mathf.Clamp01(stretch01);
        }

        /// <summary>촉수를 놓는다 (애니메이션 그대로, 채찍 숨김)</summary>
        public void Release()
        {
            ResetChain();
            chain = null;
            whip.Hide();
            WhipEnd = transform.position;
            CurrentReach = 0f;
        }

        private void ResetChain()
        {
            if (chain == null) return;
            for (int i = 1; i < chain.Length; i++)
                if (chain[i].localPosition == written[i]) chain[i].localPosition = rest[i];
            if (chain[0].localRotation == writtenRotation) chain[0].localRotation = restRotation;
        }

        private void LateUpdate()
        {
            if (chain == null) return;

            // 늘이기: 애니메이션이 이번 프레임 위치를 썼으면 그 값, 아니면(위치 곡선 없음) 처음 위치에서 다시 늘인다
            float scale = Mathf.Lerp(1f, maxStretch, stretch * weight);
            for (int i = 1; i < chain.Length; i++)
            {
                Vector3 current = chain[i].localPosition;
                Vector3 baseline = current == written[i] ? rest[i] : current;
                written[i] = baseline * scale;
                chain[i].localPosition = written[i];
            }

            // 겨누기: 뿌리 → 끝 방향을 aim 쪽 바닥 가까이로
            Vector3 center = transform.position;
            float groundY = TyranopodeGround.Height(center + aim * reach, center.y);
            Transform root = chain[0];
            // 위치와 같은 이유로: 애니메이션이 회전을 쓰지 않는 뼈면 지난 프레임에 돌린 만큼 쌓이지 않게 처음 회전에서
            if (root.localRotation == writtenRotation) root.localRotation = restRotation;
            Vector3 chord = chain[chain.Length - 1].position - root.position;
            float length = chord.magnitude;
            float dy = groundY + tipHeight - root.position.y;
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, length * length - dy * dy));
            Vector3 desired = aim * horizontal + Vector3.up * Mathf.Max(-length, Mathf.Min(length, dy));
            if (length > 0.01f && desired.sqrMagnitude > 0.0001f)
                root.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(chord, desired), weight) * root.rotation;
            writtenRotation = root.localRotation;

            // 모자란 길이는 채찍으로: 보스 중심에서 reach 까지
            Vector3 tip = chain[chain.Length - 1].position;
            Vector3 end = center + aim * reach;
            end.y = groundY + tipHeight;
            Vector3 tipFlat = tip - center;
            tipFlat.y = 0f;
            bool needWhip = tipFlat.magnitude < reach && weight > 0.01f;
            if (needWhip) whip.Draw(tip, end, weight);
            else whip.Hide();

            WhipEnd = Vector3.Lerp(tip, needWhip ? end : tip, weight);
            Vector3 flat = WhipEnd - center;
            flat.y = 0f;
            CurrentReach = flat.magnitude;
        }
    }
}
