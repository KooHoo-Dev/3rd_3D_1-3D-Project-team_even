using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 몬스터 접촉 수평화 (Contact Modification).
    ///  - 몬스터 몸이 플레이어보다 높은 구·캡슐이면, 아래쪽 곡면이 밀어내는 방향에 아래 성분이 생긴다.
    ///    여러 마리에 둘러싸이면 수평 성분은 서로 상쇄되고 아래 성분만 쌓여 땅 밑으로 밀려 들어간다
    ///  - 플레이어 ↔ MonsterBlocking 콜라이더 접촉의 법선에서 높이 성분을 지워 옆으로만 밀리게 한다
    ///  - 거의 수직인 접촉(몬스터 위에 올라섬 등)은 그대로 둔다
    ///  - 접촉 수정 콜백은 물리 스레드에서 불려 유니티 API 를 쓸 수 없다 →
    ///    FixedUpdate 에서 근처 몬스터 콜라이더 ID 를 모아 두고, 콜백은 읽기만 한다 (물리 진행 중에는 메인 스레드가 멈춰 있다)
    /// </summary>
    public partial class PlayerController
    {
        private const string MonsterContactLayerName = "MonsterBlocking";
        // 몬스터는 Update 에서 transform 으로 움직여 물리 위치가 한 프레임 늦다 → 그만큼 넉넉히 찾는다 (돌진 12m/s ≈ 프레임당 0.2~0.4m)
        private const float MonsterScanMargin = 1f;
        // 법선의 수평 성분이 이보다 작으면(거의 수직) 건드리지 않는다
        private const float MinFlatNormalSqr = 0.01f;

        private readonly Collider[] monsterScanHits = new Collider[16];
        private readonly HashSet<int> nearbyMonsterColliders = new HashSet<int>();
        private int monsterContactMask;
        private int playerColliderId;

        /// <summary>Awake: 레이어를 찾고 플레이어 캡슐의 접촉을 수정 가능하게 한다</summary>
        private void InitMonsterContact()
        {
            int layer = LayerMask.NameToLayer(MonsterContactLayerName);
            if (layer < 0) throw new InvalidOperationException($"[Player] '{MonsterContactLayerName}' 레이어가 없습니다. TagManager 를 확인해주세요.");
            monsterContactMask = 1 << layer;
            playerColliderId = capsule.GetInstanceID();
            capsule.hasModifiableContacts = true;
        }

        private void SubscribeMonsterContact()
        {
            Physics.ContactModifyEvent += FlattenMonsterContacts;
            Physics.ContactModifyEventCCD += FlattenMonsterContacts; // 플레이어는 ContinuousDynamic
        }

        private void UnsubscribeMonsterContact()
        {
            Physics.ContactModifyEvent -= FlattenMonsterContacts;
            Physics.ContactModifyEventCCD -= FlattenMonsterContacts;
            nearbyMonsterColliders.Clear();
        }

        /// <summary>FixedUpdate, 물리 진행 전: 이번 스텝에 닿을 수 있는 몬스터 콜라이더를 모은다</summary>
        private void ScanNearbyMonsters()
        {
            nearbyMonsterColliders.Clear();
            Vector3 center = rb.position + rb.rotation * capsule.center;
            float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            int count = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * half, center - Vector3.up * half,
                capsule.radius + MonsterScanMargin, monsterScanHits, monsterContactMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) nearbyMonsterColliders.Add(monsterScanHits[i].GetInstanceID());
        }

        /// <summary>물리 스레드: 플레이어 ↔ 근처 몬스터 접촉의 법선을 수평으로 만든다 (방향 부호와 무관)</summary>
        private void FlattenMonsterContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            if (nearbyMonsterColliders.Count == 0) return;

            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                int other;
                if (pair.colliderInstanceID == playerColliderId) other = pair.otherColliderInstanceID;
                else if (pair.otherColliderInstanceID == playerColliderId) other = pair.colliderInstanceID;
                else continue;
                if (nearbyMonsterColliders.Contains(other) == false) continue;

                for (int c = 0; c < pair.contactCount; c++)
                {
                    Vector3 normal = pair.GetNormal(c);
                    Vector3 flat = new Vector3(normal.x, 0f, normal.z);
                    if (flat.sqrMagnitude < MinFlatNormalSqr) continue;
                    pair.SetNormal(c, flat.normalized);
                }
            }
        }
    }
}
