using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 공중 몬스터용 실행기. MonsterMovement 가 넘긴 MoveIntent 의 목적지로 수평 등속 이동하고, 높이는 순항 고도(지면 평균 + 순항 높이)로 맞춘다.
    /// 경로 탐색·장애물 회피는 하지 않는다. 급강하(DiveAttack)는 MoveBy 로 몸을 직접 옮기고, 그동안 Stop 으로 고도 보정을 끈다.
    /// 사망하면 MonsterController 가 StartFalling 을 불러, 죽는 순간의 속도에 중력을 더해 지면까지 떨어진다.
    /// </summary>
    public sealed class FlyingMoveExecutor : MonoBehaviour, IMoveExecutor
    {
        // 비행 상태. Apply = Cruising, Stop = Suspended, StartFalling = Falling
        private enum FlightMode
        {
            Suspended,  // 정지 요청. 고도 보정 없음 (급강하가 MoveBy 로 몸을 직접 옮긴다)
            Cruising,   // 목적지로 수평 이동 + 순항 고도 맞추기
            Falling     // 사망 낙하. 중력으로 지면까지 떨어진다
        }

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
        [Header("사망 낙하")]
        [Tooltip("낙하 중 아래로 더하는 중력 가속도 (m/s²)")]
        [SerializeField, Min(0f)] private float fallGravity = 9.81f;
        [Tooltip("착지 때 피벗을 지면 위 이 높이에 둔다 (m). 피벗이 몸 중심인 모델은 몸 반높이")]
        [SerializeField] private float landingHeightOffset = 0f;

        // ════════ 이동 상태 ════════
        private Vector3 destination;      // Apply 가 저장한 목적지 (월드)
        private float speed;              // m/s, 수평 이동 속도
        private bool hasDestination;      // 목적지로 이동 중인가
        private FlightMode mode = FlightMode.Suspended;
        private Vector3 velocity;         // m/s, 지난 Update 이후 실제 이동량으로 계산 (MoveBy 이동 포함)
        private Vector3 lastPosition;     // 직전 Update 끝의 위치 (월드). velocity 계산용
        private Vector3 fallVelocity;     // m/s, 낙하 중 속도. StartFalling 이 velocity 로 시작한다

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

        /// <summary>켜질 때 속도 기준 위치를 지금 위치로 맞춘다. 풀 재사용 뒤 첫 프레임 속도가 튀지 않게 한다</summary>
        private void OnEnable()
        {
            lastPosition = transform.position;
        }

        /// <summary>
        /// 매 프레임 비행 상태대로 몸을 옮기고, 실제 이동량으로 속도를 기록한다.
        /// 순항 = 수평 이동 + 고도 맞추기, 낙하 = 중력 낙하, 정지 = 이동 없음 (급강하가 MoveBy 로 몸을 직접 옮긴다).
        /// </summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            if (mode == FlightMode.Cruising) Cruise(deltaTime);
            if (mode == FlightMode.Falling) Fall(deltaTime);
            RecordVelocity(deltaTime);
        }

        /// <summary>
        /// 이동 의도를 저장한다. 목적지가 없으면 제자리에서 고도만 맞추고, 있으면 그 목적지로 수평 이동한다.
        /// MonsterMovement 가 의도를 확정할 때 호출한다.
        /// </summary>
        /// <param name="intent">정책이 낸 이동 의도. Destination 과 Speed 만 쓴다</param>
        /// <returns>목적지 없음 Stopped, 있음 PathSet</returns>
        public MoveApplyResult Apply(in MoveIntent intent)
        {
            mode = FlightMode.Cruising;
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
            mode = FlightMode.Suspended;
        }

        /// <summary>
        /// 사망 낙하를 시작한다. 지금 속도(지난 프레임 실제 이동, 급강하 포함)를 이어받아 중력으로 지면까지 떨어진다.
        /// MonsterController 가 상태 머신이 Dead 에 들어갈 때 호출한다.
        /// </summary>
        /// <remarks>DeadState 의 Stop 다음에 불린다. 다음 Apply(풀 재사용 후 이동 재개)가 순항으로 되돌린다.</remarks>
        public void StartFalling()
        {
            hasDestination = false;
            fallVelocity = velocity;
            mode = FlightMode.Falling;
        }

        /// <summary>몸을 그대로 옮긴다. 장애물은 처리하지 않는다. MonsterMovement.MoveDuringAttack 이 급강하 중에 호출한다</summary>
        /// <param name="displacement">이번 틱 이동량 (월드, m). 높이 포함</param>
        /// <returns>항상 false (막히지 않는다)</returns>
        public bool MoveBy(Vector3 displacement)
        {
            transform.position += displacement;
            return false;
        }

        /// <summary>순항: 목적지가 있으면 수평 이동하고, 높이를 순항 고도로 맞춘다. Update 가 부른다</summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void Cruise(float deltaTime)
        {
            if (hasDestination) MoveHorizontally(deltaTime);
            MoveTowardCruiseAltitude(deltaTime);
        }

        /// <summary>
        /// 낙하: 낙하 속도에 중력을 더해 몸을 옮긴다. 지면 + landingHeightOffset 아래로 내려가면 그 높이에 붙이고 멈춘다. Update 가 부른다
        /// </summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void Fall(float deltaTime)
        {
            fallVelocity += Vector3.down * fallGravity * deltaTime;
            Vector3 position = transform.position + fallVelocity * deltaTime;
            float landingY = GroundY(position) + landingHeightOffset;
            if (position.y <= landingY)
            {
                position.y = landingY;
                fallVelocity = Vector3.zero;
            }
            transform.position = position;
        }

        /// <summary>지난 Update 이후 실제 이동량으로 velocity 를 갱신한다. MoveBy(급강하) 이동도 여기서 잡힌다</summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void RecordVelocity(float deltaTime)
        {
            velocity = (transform.position - lastPosition) / deltaTime;
            lastPosition = transform.position;
        }

        /// <summary>목적지 쪽으로 이번 프레임만큼 수평 등속 이동한다. 높이는 바꾸지 않는다. Cruise 가 부른다</summary>
        /// <param name="deltaTime">이번 프레임 경과 시간 (s)</param>
        private void MoveHorizontally(float deltaTime)
        {
            Vector3 position = transform.position;
            Vector3 flatDestination = new Vector3(destination.x, position.y, destination.z);
            transform.position = Vector3.MoveTowards(position, flatDestination, speed * deltaTime);
        }

        /// <summary>높이를 순항 고도 쪽으로 climbSpeed 만큼 옮긴다 (넘치지 않게). Cruise 가 부른다</summary>
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

        /// <summary>한 점 바로 아래 Terrain 높이. 낙하 착지 판정용. Terrain 이 없는 씬이면 NullReferenceException 으로 드러난다</summary>
        /// <param name="point">높이를 잴 위치 (월드). XZ 만 쓴다</param>
        /// <returns>지면 높이 (월드 y, m)</returns>
        private static float GroundY(Vector3 point)
        {
            Terrain terrain = Terrain.activeTerrain;
            return terrain.SampleHeight(point) + terrain.transform.position.y;
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
