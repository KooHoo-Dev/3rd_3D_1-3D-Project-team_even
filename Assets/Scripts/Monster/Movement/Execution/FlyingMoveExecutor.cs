using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 공중 몬스터용 실행기. MonsterMovement 가 넘긴 MoveIntent 의 목적지로 수평 등속 이동하고, 높이는 순항 고도(지면 평균 + 순항 높이)로 맞춘다.
    /// 경로 탐색·장애물 회피는 하지 않는다. 급강하(DiveAttack)는 MoveBy 로 몸을 직접 옮기고, 그동안 Stop 으로 고도 보정을 끈다.
    /// </summary>
    public sealed class FlyingMoveExecutor : MonoBehaviour, IMoveExecutor
    {
        // ════════ 설정 (Inspector) ════════
        [Header("순항 고도")]
        [Tooltip("지면 평균 높이 위로 유지할 높이 (m)")]
        [SerializeField, Min(0f)] private float cruiseHeight = 4f;
        [Tooltip("순항 고도로 올라가거나 내려가는 속도 (m/s)")]
        [SerializeField, Min(0.1f)] private float climbSpeed = 3f;
        [Tooltip("지면 평균을 구할 때 자기 XZ 중심에서 둘레 4점까지의 반경 (m)")]
        [SerializeField, Min(0f)] private float groundSampleRadius = 2f;
        [Header("몸·도착")]
        [Tooltip("몸 반경 (m). 군중 제어의 슬롯 장애물 판정용")]
        [SerializeField, Min(0f)] private float radius = 0.5f;
        [Tooltip("목적지와 수평 거리가 이 이하면 도착 (m)")]
        [SerializeField, Min(0f)] private float arrivalDistance = 0.5f;

        // ════════ 이동 상태 ════════
        private Vector3 destination;      // Apply 가 저장한 목적지 (월드)
        private float speed;              // m/s, 수평 이동 속도
        private bool hasDestination;      // 목적지로 이동 중인가
        private bool holdsAltitude;       // 순항 고도를 맞추는가. Apply = true, Stop(정지 요청) = false
        private Vector3 velocity;         // m/s, 이번 프레임 Update 이동량으로 계산

        // ════════ 외부 공개 ════════
        public Vector3 Velocity => velocity;
        public float Radius => radius;
        // 지금 위치에서 맞출 순항 고도 (월드 y, m). DiveAttack 의 재정비 판정도 읽는다
        public float CruiseAltitude => GroundAverageY() + cruiseHeight;

        // 목적지 없음 Idle, 수평 거리 arrivalDistance 이하 Arrived, 그 외 Moving
        public MoveExecutionState ExecutionState
        {
            get
            {
                if (hasDestination == false) return MoveExecutionState.Idle;
                float horizontalDistance = FlatDistance(transform.position, destination);
                if (horizontalDistance <= arrivalDistance) return MoveExecutionState.Arrived;
                return MoveExecutionState.Moving;
            }
        }

        /// <summary>
        /// 매 프레임 수평 이동과 고도 맞추기를 한다. 정지 요청(Stop) 뒤에는 아무것도 하지 않는다 (급강하가 몸을 직접 옮긴다).
        /// </summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (holdsAltitude == false)
            {
                velocity = Vector3.zero;
                return;
            }
            if (deltaTime <= 0f) return;

            Vector3 startPosition = transform.position;
            if (hasDestination) MoveHorizontally(deltaTime);
            MoveTowardCruiseAltitude(deltaTime);
            velocity = (transform.position - startPosition) / deltaTime;
        }

        /// <summary>
        /// 이동 의도를 저장한다. 목적지가 없으면 제자리에서 고도만 맞추고, 있으면 그 목적지로 수평 이동한다.
        /// MonsterMovement 가 의도를 확정할 때 호출한다.
        /// </summary>
        /// <param name="intent">정책이 낸 이동 의도. Destination 과 Speed 만 쓴다</param>
        /// <returns>목적지 없음 Stopped, 있음 PathSet</returns>
        public MoveApplyResult Apply(in MoveIntent intent)
        {
            holdsAltitude = true;
            if (intent.HasDestination == false)
            {
                hasDestination = false;
                return MoveApplyResult.Stopped;
            }

            destination = intent.Destination;
            speed = intent.Speed;
            hasDestination = true;
            return MoveApplyResult.PathSet;
        }

        /// <summary>
        /// 이동과 고도 맞추기를 모두 멈춘다. MonsterMovement 가 정지 요청(suspend) 때 호출한다.
        /// 급강하 중 고도 보정이 끼어들지 않게 고도 유지도 끈다. 다음 Apply 가 다시 켠다.
        /// </summary>
        public void Stop()
        {
            hasDestination = false;
            holdsAltitude = false;
        }

        /// <summary>몸을 그대로 옮긴다. 장애물은 처리하지 않는다. MonsterMovement.MoveDuringAttack 이 급강하 중에 호출한다</summary>
        /// <param name="displacement">이번 틱 이동량 (월드, m). 높이 포함</param>
        /// <returns>항상 false (막히지 않는다)</returns>
        public bool MoveBy(Vector3 displacement)
        {
            transform.position += displacement;
            return false;
        }

        /// <summary>목적지 쪽으로 이번 프레임만큼 수평 등속 이동한다. 높이는 바꾸지 않는다. Update 가 부른다</summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void MoveHorizontally(float deltaTime)
        {
            Vector3 position = transform.position;
            Vector3 flatDestination = new Vector3(destination.x, position.y, destination.z);
            transform.position = Vector3.MoveTowards(position, flatDestination, speed * deltaTime);
        }

        /// <summary>높이를 순항 고도 쪽으로 climbSpeed 만큼 옮긴다 (넘치지 않게). Update 가 부른다</summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void MoveTowardCruiseAltitude(float deltaTime)
        {
            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, CruiseAltitude, climbSpeed * deltaTime);
            transform.position = position;
        }

        /// <summary>
        /// 자기 XZ 중심과 반경 groundSampleRadius 원 위 4점(앞·뒤·좌·우)의 Terrain 높이 평균.
        /// 작은 요철에 고도가 흔들리지 않게 한다. Terrain 이 없는 씬이면 NullReferenceException 으로 드러난다.
        /// </summary>
        /// <returns>지면 평균 높이 (월드 y, m)</returns>
        private float GroundAverageY()
        {
            Terrain terrain = Terrain.activeTerrain;
            Vector3 center = transform.position;
            float heightSum = terrain.SampleHeight(center)
                            + terrain.SampleHeight(center + Vector3.forward * groundSampleRadius)
                            + terrain.SampleHeight(center + Vector3.back * groundSampleRadius)
                            + terrain.SampleHeight(center + Vector3.left * groundSampleRadius)
                            + terrain.SampleHeight(center + Vector3.right * groundSampleRadius);
            return heightSum / 5f + terrain.transform.position.y;
        }

        /// <summary>두 점의 수평(XZ) 거리. ExecutionState 의 도착 판정용</summary>
        /// <param name="from">첫 점 (월드)</param>
        /// <param name="to">둘째 점 (월드)</param>
        /// <returns>수평 거리 (m)</returns>
        private static float FlatDistance(Vector3 from, Vector3 to)
        {
            Vector2 flatFrom = new Vector2(from.x, from.z);
            Vector2 flatTo = new Vector2(to.x, to.z);
            return Vector2.Distance(flatFrom, flatTo);
        }
    }
}
