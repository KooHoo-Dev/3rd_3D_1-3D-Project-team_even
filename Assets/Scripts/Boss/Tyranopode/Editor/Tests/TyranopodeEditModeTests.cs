using System.Collections.Generic;
using HybridShooter.Boss.Tyranopode;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using HybridShooter.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Boss.TyranopodeEditor.Tests
{
    /// <summary>
    /// Tyranopode 데이터·순수 계산 검사 (EditMode). 전투 흐름(페이즈 전환 등)은 Test_Tyranopode 씬에서 직접 확인한다
    /// </summary>
    public sealed class TyranopodeEditModeTests
    {
        private static TyranopodeDto ReadRow(DataLoadReport report)
        {
            var rows = TsvTableReader.Read<TyranopodeDto>(DataManager.DefaultTableFolder, TyranopodeDef.Table, report);
            Assert.AreEqual(1, rows.Count, "TyranopodeTable 은 정확히 1행");
            return rows[0];
        }

        private static TyranopodeDef ValidDef()
        {
            var report = new DataLoadReport();
            var def = new TyranopodeDef(ReadRow(report), report);
            Assert.IsFalse(report.HasError, report.BuildSummary());
            return def;
        }

        [Test]
        public void 실제_테이블이_오류없이_읽힌다()
        {
            var def = ValidDef();
            Assert.Greater(def.SummonMinionIds.Count, 0);
        }

        // 부위 배율·체력을 TyranopodeTable → MonsterPartTable(BossId 2)로 옮기기 전 값. 피해량이 그대로인지 본다
        [TestCase(TyranopodePartType.Body, 0.8f, 0)]
        [TestCase(TyranopodePartType.FrontTentacle, 1f, 600)]
        [TestCase(TyranopodePartType.LegTentacle, 0.9f, 720)]
        [TestCase(TyranopodePartType.Sac, 1.5f, 420)]
        [TestCase(TyranopodePartType.Head, 1.3f, 480)]
        public void 부위_배율과_체력은_이전_전과_같다(TyranopodePartType type, float oldMultiplier, int oldHp)
        {
            Assert.IsTrue(DataManager.Bosses.TryGet(DataManager.Tyranopode.BossId, out var bossDef));
            Assert.IsFalse(DataManager.LastReport.HasError, DataManager.LastReport.BuildSummary());

            var part = bossDef.FindPart(type.ToString());
            Assert.IsNotNull(part, $"MonsterPartTable 에 BossId {bossDef.Id} / PartKey {type} 행이 없다");
            Assert.AreEqual(oldMultiplier, part.DamageMultiplier);
            Assert.AreEqual(oldHp, part.PartHp);

            // ApplyPartDamage 와 같은 계산 (그로기 배율 포함) → 결과 피해량이 같다
            foreach (int amount in new[] { 1, 7, 25, 100 })
            {
                foreach (float groggy in new[] { 1f, DataManager.Tyranopode.GroggyDamageMultiplier })
                {
                    int before = Mathf.Max(1, Mathf.RoundToInt(amount * oldMultiplier * groggy));
                    int after = Mathf.Max(1, Mathf.RoundToInt(amount * part.DamageMultiplier * groggy));
                    Assert.AreEqual(before, after, $"{type} 피해 {amount} × 그로기 {groggy}");
                }
            }
        }

        [Test]
        public void 소환Id는_세로선으로_나눈다()
        {
            var report = new DataLoadReport();
            var dto = ReadRow(report);
            dto.SummonMinionIds = "1002|2001";
            var def = new TyranopodeDef(dto, report);
            CollectionAssert.AreEqual(new[] { 1002, 2001 }, def.SummonMinionIds);
        }

        [Test]
        public void 소환Id에_숫자가_아니면_오류()
        {
            var report = new DataLoadReport();
            var dto = ReadRow(report);
            dto.SummonMinionIds = "1002|abc";
            new TyranopodeDef(dto, report);
            Assert.IsTrue(report.HasError);
        }

        [Test]
        public void 소환Id가_비면_오류()
        {
            var report = new DataLoadReport();
            var dto = ReadRow(report);
            dto.SummonMinionIds = "";
            new TyranopodeDef(dto, report);
            Assert.IsTrue(report.HasError);
        }

        [Test]
        public void 근거리가_원거리보다_크면_오류()
        {
            var report = new DataLoadReport();
            var dto = ReadRow(report);
            dto.MeleeRange = 30f;
            dto.FarRange = 20f;
            new TyranopodeDef(dto, report);
            Assert.IsTrue(report.HasError);
        }

        [Test]
        public void 난이도보정_체력0이면_체력몫까지_오른다()
        {
            var def = ValidDef();
            var assist = new TyranopodeAssist(def);
            assist.Begin();
            try
            {
                EventBus<PlayerHpChangedEvent>.Publish(new PlayerHpChangedEvent(0, 100));
                assist.Tick(1000f);
                // 목숨 정보가 없으면 체력 몫(0.6)만
                Assert.AreEqual(0.6f, assist.Struggle, 0.001f);
                Assert.AreEqual(Mathf.Lerp(1f, def.AssistMaxDelayMultiplier, 0.6f), assist.DelayMultiplier, 0.001f);
                Assert.AreEqual(1f - def.AssistComboReduce * 0.6f, assist.ComboMultiplier, 0.001f);
            }
            finally
            {
                assist.End();
            }
        }

        [Test]
        public void 난이도보정은_천천히_따라간다()
        {
            var def = ValidDef();
            var assist = new TyranopodeAssist(def);
            assist.Begin();
            try
            {
                EventBus<PlayerHpChangedEvent>.Publish(new PlayerHpChangedEvent(0, 100));
                assist.Tick(1f);
                float expected = Mathf.Min(0.6f, 1f / def.AssistSmoothTime);
                Assert.AreEqual(expected, assist.Struggle, 0.001f);
            }
            finally
            {
                assist.End();
            }
        }

        [Test]
        public void 체력이_가득하면_보정없음()
        {
            var assist = new TyranopodeAssist(ValidDef());
            assist.Begin();
            try
            {
                EventBus<PlayerHpChangedEvent>.Publish(new PlayerHpChangedEvent(100, 100));
                assist.Tick(1000f);
                Assert.AreEqual(0f, assist.Struggle, 0.001f);
                Assert.AreEqual(1f, assist.DelayMultiplier, 0.001f);
            }
            finally
            {
                assist.End();
            }
        }

        [Test]
        public void 경기장_경계_밖은_벽에서_여유만큼_안으로_당긴다()
        {
            var go = new GameObject("LocomotionTest");
            try
            {
                var locomotion = go.AddComponent<TyranopodeLocomotion>();
                locomotion.SetBounds(Vector3.zero, 20f);
                float inner = locomotion.InnerRadius;

                Vector3 clamped = locomotion.ClampToBounds(new Vector3(30f, 1f, 0f));
                Assert.AreEqual(inner, clamped.x, 0.001f);
                Assert.AreEqual(1f, clamped.y, 0.001f, "높이는 그대로");

                var inside = new Vector3(1f, 0f, 1f);
                Assert.AreEqual(inside, locomotion.ClampToBounds(inside));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 등장_흡수_보호막은_마리수만큼_상한까지()
        {
            Assert.AreEqual(6f, TyranopodeController.DrainShield(3, 2f, 100f, 0f));
            // 이미 준 양과 합쳐 상한을 넘지 않는다
            Assert.AreEqual(4f, TyranopodeController.DrainShield(10, 2f, 100f, 96f));
            Assert.AreEqual(0f, TyranopodeController.DrainShield(5, 2f, 100f, 100f));
        }

        [Test]
        public void 보호막_기본량은_흡수량에_상한_밖으로_더한다()
        {
            // 흡수량 + 최대 체력 10%
            Assert.AreEqual(150f, TyranopodeController.WithBaseShield(50f, 1000f, 0.1f), 0.001f);
            // 흡수 상한(DrainShield)이 꽉 차도 기본량은 그대로 붙는다
            Assert.AreEqual(400f, TyranopodeController.WithBaseShield(TyranopodeController.DrainShield(100, 10f, 300f, 0f), 1000f, 0.1f), 0.001f);
            // 흡수량이 없으면 보호막이 생기지 않으니 기본량도 없다
            Assert.AreEqual(0f, TyranopodeController.WithBaseShield(0f, 1000f, 0.1f));
        }

        [Test]
        public void 등장_시간표는_단계가_이어진다()
        {
            foreach (var s in new[] { TyranopodeIntroSettings.Full, TyranopodeIntroSettings.Brief })
            {
                Assert.Less(s.LandTime, s.DrainStart);
                Assert.Less(s.DrainStart, s.RoarStart);
                Assert.Less(s.RoarStart, s.BlendStart);
                Assert.Less(s.BlendStart, s.TotalTime);
            }
            Assert.Less(TyranopodeIntroSettings.Brief.TotalTime, TyranopodeIntroSettings.Full.TotalTime);
        }

        [Test]
        public void 범위_표시는_경사진_바닥을_따라_휜다()
        {
            // 터레인과 겹치지 않게 먼 곳에 20도 기운 바닥
            var center = new Vector3(50000f, 0f, 50000f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetPositionAndRotation(center, Quaternion.Euler(20f, 0f, 0f));
            ground.transform.localScale = new Vector3(80f, 1f, 80f);
            Physics.SyncTransforms();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var telegraph = GroundTelegraph.Create(material);
            try
            {
                telegraph.ShowCircle(center, 12f, 1f, Color.red);
                telegraph.ShowRect(center, Vector3.forward, 6f, 20f, 1f, Color.red);
                foreach (var vertex in telegraph.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Vector3 world = telegraph.transform.TransformPoint(vertex);
                    Assert.IsTrue(Physics.Raycast(new Vector3(world.x, 200f, world.z), Vector3.down, out var hit, 400f));
                    // 모든 꼭짓점이 바닥 위 Lift(0.1m) 근처 — 평평한 판이면 오르막 쪽이 수 m 묻힌다
                    Assert.AreEqual(hit.point.y + 0.1f, world.y, 0.05f);
                }
            }
            finally
            {
                Object.DestroyImmediate(telegraph.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void 벽_감지는_낮은_돌멩이를_무시하고_높은_벽만_잡는다()
        {
            // 터레인과 겹치지 않게 먼 곳. 보스는 +z 를 본다 (기본값: 감지 높이 2.5m, 반지름 2m, 앞쪽 3m)
            var origin = new Vector3(60000f, 0f, 60000f);
            var go = new GameObject("WallProbeTest");
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.position = origin;
                var locomotion = go.AddComponent<TyranopodeLocomotion>();
                rock.transform.SetPositionAndRotation(origin + new Vector3(0f, 0.5f, 5f), Quaternion.identity); // 높이 1m
                wall.transform.SetPositionAndRotation(origin + new Vector3(0f, 4f, 12f), Quaternion.identity);
                wall.transform.localScale = new Vector3(10f, 8f, 1f);
                Physics.SyncTransforms();

                Assert.AreEqual(TyranopodeLocomotion.WallContact.None, locomotion.ProbeWall(Vector3.forward, 3f, out _), "돌멩이(1m)는 벽이 아니다");
                Assert.AreEqual(TyranopodeLocomotion.WallContact.HeadOn, locomotion.ProbeWall(Vector3.forward, 8f, out _), "몸 높이에서 정면으로 막는 벽");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(rock);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void 벽_감지는_오를_수_있는_경사를_벽으로_보지_않는다()
        {
            // 구덩이 안에서 바깥으로 올라가는 35도 경사면 (기본 wallMinSteepness 50도보다 완만)
            var origin = new Vector3(70000f, 0f, 70000f);
            var go = new GameObject("SlopeProbeTest");
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.position = origin;
                var locomotion = go.AddComponent<TyranopodeLocomotion>();
                slope.transform.SetPositionAndRotation(origin + new Vector3(0f, 0f, 14f), Quaternion.Euler(-35f, 0f, 0f));
                slope.transform.localScale = new Vector3(20f, 1f, 30f);
                Physics.SyncTransforms();

                Assert.AreEqual(TyranopodeLocomotion.WallContact.None, locomotion.ProbeWall(Vector3.forward, 8f, out _), "35도 경사는 오를 수 있다");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(slope);
            }
        }

        [Test]
        public void 벽을_비스듬히_스치면_정면_충돌이_아니다()
        {
            // 진행 방향(+z)과 나란한 옆 벽이 몸통 감지 구에 걸린다 → 스침 (미끄러짐), 그로기 아님
            var origin = new Vector3(80000f, 0f, 80000f);
            var go = new GameObject("GrazeProbeTest");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.transform.position = origin;
                var locomotion = go.AddComponent<TyranopodeLocomotion>();
                // 옆 벽을 진행 방향으로 20도 기울여 앞쪽에서 구에 닿게 한다
                wall.transform.SetPositionAndRotation(origin + new Vector3(3.2f, 4f, 8f), Quaternion.Euler(0f, -20f, 0f));
                wall.transform.localScale = new Vector3(1f, 8f, 14f);
                Physics.SyncTransforms();

                var contact = locomotion.ProbeWall(Vector3.forward, 8f, out Vector3 normal);
                Assert.AreEqual(TyranopodeLocomotion.WallContact.Graze, contact);
                Assert.Less(Vector3.Dot(-normal, Vector3.forward), 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void 경기장_반지름은_막힌_방향의_중앙값_절반_미만이면_열린_맵()
        {
            // 24방향 중 12방향이 막힘 → 중앙값. 바위 구멍처럼 짧은 값 몇 개는 흔들지 않는다
            var blocked = new List<float> { 3f, 4f, 30f, 31f, 32f, 32f, 33f, 33f, 34f, 34f, 35f, 36f };
            Assert.IsTrue(TyranopodeMinions.TryArenaRadius(blocked, 24, out float radius));
            Assert.AreEqual(33f, radius);

            // 막힌 방향이 절반 미만 → 열린 맵 (경계 없음)
            Assert.IsFalse(TyranopodeMinions.TryArenaRadius(new List<float> { 10f, 12f, 14f }, 24, out _));
        }

        [Test]
        public void 경계가_없으면_그대로()
        {
            var go = new GameObject("LocomotionTest");
            try
            {
                var locomotion = go.AddComponent<TyranopodeLocomotion>();
                var far = new Vector3(500f, 0f, 0f);
                Assert.AreEqual(far, locomotion.ClampToBounds(far));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
