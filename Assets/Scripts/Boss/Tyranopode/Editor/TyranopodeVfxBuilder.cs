using System;
using System.IO;
using HybridShooter.Boss.Tyranopode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Boss.TyranopodeEditor
{
    /// <summary>
    /// Tyranopode 연출(파티클) 프리팹을 코드로 만든다 → Resources/Boss/Tyranopode/VFX/.
    /// 텍스처(부드러운 원·링)도 직접 그려서 외부 에셋 없이 동작한다. 색은 보스 발광(주황)·산성(연두) 두 계열.
    /// 크기 기준: 충격파 반지름 10m, 충돌 먼지 반지름 4m (TyranopodeController 가 실제 크기에 맞춰 키운다)
    /// 다시 실행하면 덮어쓴다. TyranopodePrefabBuilder 가 먼저 부른다
    /// </summary>
    public static class TyranopodeVfxBuilder
    {
        public const string Folder = "Assets/Resources/Boss/Tyranopode/VFX";

        private static readonly Color Orange = new Color(1f, 0.55f, 0.12f);
        private static readonly Color Ichor = new Color(1f, 0.75f, 0.2f);
        private static readonly Color Acid = new Color(0.65f, 1f, 0.2f);
        private static readonly Color Dust = new Color(0.55f, 0.5f, 0.45f);
        private static readonly Color Shell = new Color(0.9f, 0.82f, 0.65f);
        private static readonly Color Flesh = new Color(0.45f, 0.2f, 0.14f, 0.9f);

        /// <summary>만든 연출 묶음</summary>
        public sealed class Set
        {
            public GameObject Roar, Shockwave, Impact, PartBreak, Stump, Wave, BeaconPulse, BeaconBreak, BeaconFail,
                AcidSplash, SpitTrail, EggBreak, EggHatch, MinionSpawn, MinionDeath, Groggy,
                EnergyOrb, EnergyAbsorb, ShieldHit, ShieldCrack, ShieldShatter, FinaleGroundEnergy, OrbitalStrike, Ricochet, IntroVeins;
            public Material Shield, Beam, SpitSac;
        }

        private sealed class Materials
        {
            public Material Additive, Alpha, Ring;
        }

        [MenuItem("Tools/HybridShooter/Boss/Tyranopode 연출(VFX) 만들기")]
        public static void BuildMenu()
        {
            BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log($"[TyranopodeVfx] 완료: {Folder}. 보스 프리팹에 연결하려면 'Tyranopode 프리팹 만들기'를 실행하세요.");
        }

        public static Set BuildAll()
        {
            EnsureFolder(Folder);
            var soft = SaveTexture("T_Soft", (x, y) => Mathf.Pow(Mathf.Clamp01(1f - Radius(x, y)), 2f));
            // 링 두께를 바깥 끝에 붙여 크기 = 실제 반지름이 되게 (중심 0.92, 반폭 0.07)
            var ring = SaveTexture("T_Ring", (x, y) => Mathf.Clamp01(1f - Mathf.Abs(Radius(x, y) - 0.92f) / 0.07f));
            var m = new Materials
            {
                Additive = SaveMaterial("M_VfxAdditive", soft, additive: true),
                Alpha = SaveMaterial("M_VfxAlpha", soft, additive: false),
                Ring = SaveMaterial("M_VfxRing", ring, additive: true),
            };

            var shieldMaterial = SaveMembraneMaterial("M_Shield", null);
            // 침 주머니: 보호막과 같은 생체막, 더 불투명하고 산성 담즙빛 혈관이 빠르게 맥박친다
            var spitSac = SaveMembraneMaterial("M_SpitSac", material =>
            {
                material.SetColor("_BaseColor", new Color(0.32f, 0.4f, 0.08f, 0.8f));
                material.SetColor("_VeinColor", new Color(1.8f, 2.6f, 0.4f, 1f));
                material.SetColor("_DamagedVeinColor", new Color(1.8f, 2.6f, 0.4f, 1f));
                material.SetColor("_RimColor", new Color(1.2f, 2f, 0.3f, 1f));
                material.SetColor("_HitColor", new Color(1.8f, 2.6f, 0.4f, 1f));
                material.SetFloat("_CellScale", 4f);
                material.SetFloat("_VeinWidth", 0.18f);
                material.SetFloat("_PulseSpeed", 7f);
                material.SetFloat("_Wobble", 0.08f);
            });

            return new Set
            {
                Shield = shieldMaterial,
                SpitSac = spitSac,
                Beam = m.Additive,
                Ricochet = Save("VFX_Ricochet", root =>
                {
                    // 굳은 외피에 튕긴 총알: 차가운 불꽃이 짧게 흩어진다
                    Flash(root, m.Additive, new Color(0.6f, 0.9f, 1f), size: 1.2f);
                    Burst(root, m.Additive, new Color(0.7f, 0.95f, 1f), count: 10, speed: 9f, size: 0.15f, life: 0.25f, gravity: 1f, shapeRadius: 0.05f, upward: false);
                }),
                IntroVeins = Save("VFX_IntroVeins", root =>
                {
                    // 등장 흡수: 발밑에서 빛나는 혈관 줄기가 3초에 걸쳐 반지름 60m 까지 뻗고, 그 끝을 링이 따라간다
                    // (TyranopodeController 가 경기장 반지름·흡수 시간에 맞춰 크기와 재생 속도를 바꾼다)
                    Flash(root, m.Additive, Orange, size: 10f);
                    var veins = NewSystem(root, "Veins", m.Additive, loop: false, duration: 3f);
                    veins.transform.localPosition = Vector3.up * 0.15f;
                    var main = veins.main;
                    main.startLifetime = 3f;
                    main.startSpeed = 20f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                    main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
                    SetBurst(veins, 90);
                    var shape = veins.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.5f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    Fade(veins);
                    var stretch = veins.GetComponent<ParticleSystemRenderer>();
                    stretch.renderMode = ParticleSystemRenderMode.Stretch;
                    stretch.lengthScale = 8f;
                    stretch.velocityScale = 0.15f;

                    // 혈관 끝을 따라가는 링: 수명 내내 일정한 속도로 60m 까지
                    GroundRing(root, m.Ring, new Color(1f, 0.5f, 0.15f, 0.8f), diameter: 120f, life: 3f, growShare: 1f);
                }),
                FinaleGroundEnergy = Save("VFX_FinaleGroundEnergy", root =>
                {
                    // 땅에서 힘을 빨아들인다: 둘레에서 안쪽으로 모이며 솟는 빛 + 발밑 맥박 링
                    var inflow = Loop(root, m.Additive, Orange, rate: 160f, speed: 0f, size: 0.8f, life: 1.6f, gravity: -0.3f);
                    var shape = inflow.shape;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 22f;
                    shape.radiusThickness = 0.2f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    var velocity = inflow.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.radial = new ParticleSystem.MinMaxCurve(-14f);
                    velocity.y = new ParticleSystem.MinMaxCurve(2f);
                    var column = Loop(root, m.Additive, new Color(1f, 0.8f, 0.4f, 0.8f), rate: 60f, speed: 12f, size: 1.2f, life: 1.2f, gravity: 0f);
                    var columnShape = column.shape;
                    columnShape.shapeType = ParticleSystemShapeType.Cone;
                    columnShape.angle = 8f;
                    columnShape.radius = 2f;
                    columnShape.rotation = new Vector3(-90f, 0f, 0f);
                    Loop(root, m.Alpha, new Color(0.35f, 0.25f, 0.2f, 0.4f), rate: 20f, speed: 2f, size: 5f, life: 2.5f, gravity: -0.05f);
                }),
                OrbitalStrike = Save("VFX_OrbitalStrike", root =>
                {
                    Flash(root, m.Additive, new Color(1f, 0.95f, 0.85f), size: 70f);
                    GroundRing(root, m.Ring, new Color(1f, 0.85f, 0.6f, 1f), diameter: 110f, life: 1.4f);
                    GroundRing(root, m.Ring, new Color(1f, 0.5f, 0.2f, 0.8f), diameter: 70f, life: 1.8f);
                    Burst(root, m.Additive, new Color(1f, 0.75f, 0.4f), count: 200, speed: 45f, size: 1.2f, life: 2f, gravity: 1.5f, shapeRadius: 3f, upward: true);
                    Burst(root, m.Alpha, new Color(0.3f, 0.25f, 0.22f, 0.7f), count: 60, speed: 10f, size: 9f, life: 4f, gravity: -0.05f, shapeRadius: 6f, upward: true);
                    Burst(root, m.Alpha, Flesh, count: 80, speed: 25f, size: 1.2f, life: 2.5f, gravity: 2.5f, shapeRadius: 3f, upward: true);
                }),
                EnergyOrb = Save("VFX_EnergyOrb", root =>
                {
                    // 생체 에너지: 빛나는 핵 + 지나간 자리에 남는 꼬리
                    Loop(root, m.Additive, new Color(1f, 0.7f, 0.25f), rate: 30f, speed: 0f, size: 1.2f, life: 0.15f, gravity: 0f);
                    Loop(root, m.Additive, new Color(1f, 0.5f, 0.15f, 0.8f), rate: 40f, speed: 0.2f, size: 0.6f, life: 0.6f, gravity: 0f);
                }),
                EnergyAbsorb = Save("VFX_EnergyAbsorb", root =>
                {
                    Flash(root, m.Additive, new Color(1f, 0.75f, 0.3f), size: 5f);
                    Burst(root, m.Additive, Orange, count: 12, speed: 4f, size: 0.4f, life: 0.5f, gravity: 0f, shapeRadius: 0.3f, upward: true);
                }),
                ShieldHit = Save("VFX_ShieldHit", root =>
                {
                    // 막이 맞은 자리: 체액이 튀고 작은 빛
                    Flash(root, m.Additive, new Color(1f, 0.6f, 0.2f), size: 1.6f);
                    Burst(root, m.Additive, Ichor, count: 10, speed: 4f, size: 0.25f, life: 0.6f, gravity: 2f, shapeRadius: 0.1f, upward: true);
                }),
                ShieldCrack = Save("VFX_ShieldCrack", root =>
                {
                    // 막이 찢어질 때: 체액 분출 + 어두운 살점 조각
                    Flash(root, m.Additive, new Color(1f, 0.45f, 0.15f), size: 5f);
                    Burst(root, m.Additive, Ichor, count: 35, speed: 8f, size: 0.4f, life: 1f, gravity: 2.5f, shapeRadius: 0.5f, upward: true);
                    Burst(root, m.Alpha, Flesh, count: 14, speed: 6f, size: 0.6f, life: 1.4f, gravity: 2.5f, shapeRadius: 0.5f, upward: true);
                }),
                ShieldShatter = Save("VFX_ShieldShatter", root =>
                {
                    Flash(root, m.Additive, new Color(1f, 0.6f, 0.25f), size: 18f);
                    Burst(root, m.Additive, Ichor, count: 140, speed: 16f, size: 0.6f, life: 1.6f, gravity: 2.5f, shapeRadius: 4f, upward: false);
                    Burst(root, m.Alpha, Flesh, count: 60, speed: 12f, size: 1f, life: 2f, gravity: 2.5f, shapeRadius: 4f, upward: true);
                    Burst(root, m.Alpha, new Color(0.35f, 0.18f, 0.12f, 0.5f), count: 25, speed: 3f, size: 4f, life: 2.2f, gravity: -0.05f, shapeRadius: 3f, upward: true);
                }),
                Roar = Save("VFX_RoarGas", root =>
                {
                    Plume(root, m.Alpha, new Color(1f, 0.6f, 0.3f, 0.55f), rate: 45f, duration: 1.4f, speed: 6f, size: 2.5f);
                    Plume(root, m.Additive, new Color(1f, 0.45f, 0.1f, 0.5f), rate: 25f, duration: 1.2f, speed: 8f, size: 1.5f);
                }),
                Shockwave = Save("VFX_Shockwave", root =>
                {
                    GroundRing(root, m.Ring, new Color(1f, 0.7f, 0.4f, 0.9f), diameter: 20f, life: 0.7f);
                    Burst(root, m.Alpha, WithAlpha(Dust, 0.6f), count: 30, speed: 9f, size: 2.5f, life: 1.2f, gravity: -0.05f, shapeRadius: 2f, upward: false);
                }),
                Impact = Save("VFX_Impact", root =>
                {
                    Burst(root, m.Alpha, WithAlpha(Dust, 0.75f), count: 35, speed: 5f, size: 2.2f, life: 1.6f, gravity: -0.05f, shapeRadius: 2f, upward: true);
                    Burst(root, m.Alpha, new Color(0.35f, 0.3f, 0.25f), count: 25, speed: 9f, size: 0.35f, life: 1.1f, gravity: 2f, shapeRadius: 1f, upward: true);
                }),
                PartBreak = Save("VFX_PartBreak", root =>
                {
                    Burst(root, m.Additive, Ichor, count: 70, speed: 11f, size: 0.45f, life: 1.2f, gravity: 2f, shapeRadius: 0.5f, upward: true);
                    Flash(root, m.Additive, Orange, size: 6f);
                }),
                Stump = Save("VFX_Stump", root =>
                {
                    Loop(root, m.Additive, Ichor, rate: 10f, speed: 0.5f, size: 0.3f, life: 1f, gravity: 1.5f);
                    Loop(root, m.Alpha, new Color(0.25f, 0.2f, 0.18f, 0.45f), rate: 5f, speed: 1.2f, size: 1.2f, life: 2f, gravity: -0.1f);
                }),
                Wave = Save("VFX_Wave", root =>
                {
                    GroundRing(root, m.Ring, new Color(1f, 0.8f, 0.4f, 1f), diameter: 120f, life: 1.3f);
                    GroundRing(root, m.Ring, new Color(1f, 0.5f, 0.15f, 0.7f), diameter: 80f, life: 1.6f);
                    Flash(root, m.Additive, new Color(1f, 0.85f, 0.5f), size: 30f);
                    Burst(root, m.Additive, Orange, count: 120, speed: 30f, size: 0.6f, life: 1.4f, gravity: 0.5f, shapeRadius: 2f, upward: false);
                }),
                BeaconPulse = Save("VFX_BeaconPulse", root =>
                    Billboard(root, m.Ring, new Color(1f, 0.65f, 0.2f, 0.9f), diameter: 26f, life: 0.9f)),
                BeaconFail = Save("VFX_BeaconFail", root =>
                {
                    Flash(root, m.Additive, new Color(1f, 0.3f, 0.1f), size: 25f);
                    Billboard(root, m.Ring, new Color(1f, 0.25f, 0.1f, 1f), diameter: 40f, life: 0.8f);
                    Burst(root, m.Additive, new Color(1f, 0.35f, 0.1f), count: 120, speed: 25f, size: 0.8f, life: 1.6f, gravity: 1f, shapeRadius: 1f, upward: false);
                }),
                BeaconBreak = Save("VFX_BeaconBreak", root =>
                {
                    Flash(root, m.Additive, new Color(1f, 0.9f, 0.6f), size: 18f);
                    Burst(root, m.Additive, Orange, count: 90, speed: 18f, size: 0.7f, life: 1.5f, gravity: 1f, shapeRadius: 1f, upward: false);
                }),
                AcidSplash = Save("VFX_AcidSplash", root =>
                {
                    // 반경 2.5m 기준 (침이 실제 반경에 맞춰 키운다): 담즙이 튀고 찢어진 막 조각, 바닥 링, 산성 김
                    Flash(root, m.Additive, Acid, size: 5f);
                    GroundRing(root, m.Ring, new Color(0.75f, 1f, 0.3f, 0.9f), diameter: 5f, life: 0.6f);
                    Burst(root, m.Additive, Acid, count: 40, speed: 7f, size: 0.35f, life: 0.9f, gravity: 2.5f, shapeRadius: 0.4f, upward: true);
                    Burst(root, m.Alpha, new Color(0.3f, 0.35f, 0.1f, 0.9f), count: 12, speed: 5f, size: 0.5f, life: 1.1f, gravity: 2.5f, shapeRadius: 0.3f, upward: true);
                    Plume(root, m.Alpha, new Color(0.55f, 0.7f, 0.35f, 0.35f), rate: 30f, duration: 0.8f, speed: 2.5f, size: 1.8f);
                }),
                SpitTrail = Save("VFX_SpitTrail", root =>
                {
                    // 빛나는 꼬리 + 떨어지는 담즙 방울
                    Loop(root, m.Additive, Acid, rate: 50f, speed: 0.2f, size: 0.7f, life: 0.3f, gravity: 0f);
                    Loop(root, m.Additive, new Color(0.8f, 1f, 0.35f, 0.9f), rate: 25f, speed: 0.6f, size: 0.18f, life: 0.6f, gravity: 2.5f);
                }),
                EggBreak = Save("VFX_EggBreak", root =>
                {
                    Burst(root, m.Alpha, Shell, count: 20, speed: 6f, size: 0.4f, life: 1f, gravity: 2.5f, shapeRadius: 0.4f, upward: true);
                    Burst(root, m.Additive, Ichor, count: 25, speed: 4f, size: 0.35f, life: 0.8f, gravity: 2f, shapeRadius: 0.3f, upward: true);
                }),
                EggHatch = Save("VFX_EggHatch", root =>
                {
                    Burst(root, m.Alpha, new Color(0.5f, 0.45f, 0.3f, 0.6f), count: 25, speed: 3f, size: 1.8f, life: 1.4f, gravity: -0.05f, shapeRadius: 0.6f, upward: true);
                    Burst(root, m.Additive, Ichor, count: 30, speed: 5f, size: 0.35f, life: 0.9f, gravity: 2f, shapeRadius: 0.4f, upward: true);
                }),
                MinionSpawn = Save("VFX_MinionSpawn", root =>
                    Burst(root, m.Alpha, WithAlpha(Dust, 0.7f), count: 25, speed: 3f, size: 2.5f, life: 1.5f, gravity: -0.05f, shapeRadius: 1f, upward: true)),
                MinionDeath = Save("VFX_MinionDeath", root =>
                {
                    Burst(root, m.Additive, Orange, count: 30, speed: 6f, size: 0.35f, life: 0.8f, gravity: 1f, shapeRadius: 0.5f, upward: true);
                    Flash(root, m.Additive, Orange, size: 3f);
                }),
                Groggy = Save("VFX_Groggy", root =>
                {
                    var stars = Loop(root, m.Additive, new Color(1f, 0.9f, 0.5f), rate: 12f, speed: 0f, size: 0.5f, life: 1.2f, gravity: 0f);
                    var shape = stars.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 2f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    var velocity = stars.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.space = ParticleSystemSimulationSpace.Local;
                    velocity.orbitalY = new ParticleSystem.MinMaxCurve(3f);
                    Loop(root, m.Alpha, new Color(0.4f, 0.38f, 0.35f, 0.35f), rate: 4f, speed: 1f, size: 2f, life: 2f, gravity: -0.1f);
                }),
            };
        }

        // ───────────────────────── 파티클 조각 ─────────────────────────

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, bool loop, float duration)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = loop;
            main.duration = duration;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 500;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // 기본값(화면의 50%)이면 가까이서 큰 링·섬광이 잘려 실제 범위보다 작아 보인다
            renderer.maxParticleSize = 100f;
            return ps;
        }

        private static void Fade(ParticleSystem ps, float startAlpha = 1f)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha, 0.3f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        private static void SetBurst(ParticleSystem ps, int count)
        {
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        /// <summary>
        /// 바닥에 눕힌 링이 diameter 까지 퍼지며 사라진다. 지형을 따라 휘는 띠 메시라 경사에서도 묻히지 않는다 (TyranopodeGroundRing).
        /// growShare: 수명의 이 비율 안에 실제 반지름까지 퍼지고 그 크기에서 사라진다 → 사라지는 링 = 피해 범위 끝 (1 = 수명 내내 일정하게)
        /// </summary>
        private static void GroundRing(Transform root, Material material, Color color, float diameter, float life, float growShare = 0.35f)
        {
            var go = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.AddComponent<TyranopodeGroundRing>().Configure(diameter * 0.5f, life, growShare, color);
        }

        /// <summary>카메라를 보는 링 (공중 포자의 파동)</summary>
        private static ParticleSystem Billboard(Transform root, Material material, Color color, float diameter, float life)
        {
            var ps = NewSystem(root, "Ring", material, loop: false, duration: life);
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = diameter;
            main.startColor = color;
            SetBurst(ps, 1);
            Grow(ps, 0.1f, 1f);
            Fade(ps);
            return ps;
        }

        /// <summary>순간 섬광</summary>
        private static void Flash(Transform root, Material material, Color color, float size)
        {
            var ps = NewSystem(root, "Flash", material, loop: false, duration: 0.3f);
            var main = ps.main;
            main.startLifetime = 0.25f;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = color;
            SetBurst(ps, 1);
            Grow(ps, 0.6f, 1f);
            Fade(ps);
        }

        /// <summary>한 번에 터지는 입자 (먼지·체액·파편). upward = 위쪽 반구로, 아니면 수평으로 퍼진다</summary>
        private static void Burst(Transform root, Material material, Color color, int count, float speed, float size, float life,
            float gravity, float shapeRadius, bool upward)
        {
            var ps = NewSystem(root, "Burst", material, loop: false, duration: life);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SetBurst(ps, count);

            var shape = ps.shape;
            shape.enabled = true;
            if (upward)
            {
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = shapeRadius;
                shape.rotation = new Vector3(-90f, 0f, 0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = shapeRadius;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            Fade(ps);
        }

        /// <summary>계속 나오는 입자 (상처·꼬리·그로기). 켜고 끄는 쪽이 오브젝트를 다룬다</summary>
        private static ParticleSystem Loop(Transform root, Material material, Color color, float rate, float speed, float size, float life, float gravity)
        {
            var ps = NewSystem(root, "Loop", material, loop: true, duration: 1f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            Fade(ps);
            return ps;
        }

        /// <summary>위로 솟는 가스 기둥 (포효)</summary>
        private static void Plume(Transform root, Material material, Color color, float rate, float duration, float speed, float size)
        {
            var ps = NewSystem(root, "Plume", material, loop: false, duration: duration);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startColor = color;
            main.gravityModifier = -0.05f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Grow(ps, 0.5f, 2.5f);
            Fade(ps);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ───────────────────────── 저장 ─────────────────────────

        private static GameObject Save(string name, Action<Transform> build)
        {
            var root = new GameObject(name);
            try
            {
                build(root.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/{name}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static float Radius(float x, float y) => new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f;

        /// <summary>흰색 + 알파 텍스처를 그려 PNG 로 저장한다 (alpha = 0~1 함수)</summary>
        private static Texture2D SaveTexture(string name, Func<float, float, float> alpha)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha((x + 0.5f) / size, (y + 0.5f) / size)));
            texture.Apply();

            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material SaveMaterial(string name, Texture2D texture, bool additive)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);
            existing.shaderKeywords = material.shaderKeywords;
            existing.renderQueue = material.renderQueue;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(material);
            return existing;
        }

        /// <summary>보호막 막: 전용 셰이더(혈관·가장자리 빛·찢김). 색·세기 기본값은 셰이더 속성 기본값</summary>
        /// <summary>생체막 셰이더 머티리얼. setup 이 없으면 셰이더 기본값(보호막 주황)</summary>
        private static Material SaveMembraneMaterial(string name, Action<Material> setup)
        {
            var shader = Shader.Find("HybridShooter/TyranopodeMembrane");
            if (shader == null) throw new System.InvalidOperationException("[TyranopodeVfx] 셰이더 HybridShooter/TyranopodeMembrane 이 없습니다 (Assets/Shaders/Boss).");
            var material = new Material(shader);
            setup?.Invoke(material);

            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);
            existing.shaderKeywords = material.shaderKeywords;
            existing.renderQueue = material.renderQueue;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(material);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) == false) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
