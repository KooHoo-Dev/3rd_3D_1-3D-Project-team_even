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

                Assert.IsFalse(locomotion.ProbeWall(Vector3.forward, 3f), "돌멩이(1m)는 벽이 아니다");
                Assert.IsTrue(locomotion.ProbeWall(Vector3.forward, 8f), "몸 높이에 걸리는 벽");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(rock);
                Object.DestroyImmediate(wall);
            }
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
