using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creepy_Cat Spaceship_01 파츠 프리펩으로 추락한 비행선(잔해 포함)을 조립한다.
/// 메뉴: Tools/Map/Build Crashed Ship
/// - 비행선 정면은 +Z, 폭은 X, 위는 Y 라고 가정한다. (파츠 크기는 Renderer Bounds로 맞추므로 피벗 위치는 무관)
/// - 조립 결과는 프리펩(CrashedShip.prefab)으로 저장하고, 씬 뷰 중앙(지면에 레이캐스트)에 인스턴스를 배치한다.
/// </summary>
public static class CrashedShipBuilder
{
    private const string PartsDir = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Spaceship_01/";
    private const string SavePath = "Assets/Scenes/GU/Prefabs/CrashedShip.prefab";
    private const string SmokePath = "Assets/Scenes/GU/Prefabs/EngineSmoke.prefab";

    // 추락 자세
    private const float Pitch = 16f;      // 기수 숙임
    private const float Roll = 14f;       // 옆으로 기울임
    private const float Yaw = 25f;        // 진행 방향 회전
    private const float BuriedRatio = 0.3f; // 선체 높이 중 땅에 파묻힌 비율

    [MenuItem("Tools/Map/Build Crashed Ship")]
    private static void Build()
    {
        GameObject root = new GameObject("CrashedShip");
        Transform wreck = new GameObject("Wreck").transform;
        wreck.SetParent(root.transform, false);

        // 1) 정상 자세(회전 없음)로 파츠 조립
        GameObject body = Spawn("Body_01", wreck);
        Bounds hull = GetBounds(body);

        // 조종석: 선체 앞쪽 끝에 맞물림
        GameObject cockpit = Spawn("Cockpit_01", wreck);
        AlignToFace(cockpit, hull, Axis.Z, +1, overlap: 0.15f);
        CenterOn(cockpit, hull, y: true, x: true);

        // 엔진: 선체 뒤쪽 끝, 좌우 한 쌍
        float engineX = hull.extents.x * 0.5f;
        GameObject engineL = Spawn("Engine_01", wreck);
        AlignToFace(engineL, hull, Axis.Z, -1, overlap: 0.1f);
        Shift(engineL, new Vector3(-engineX - GetBounds(engineL).center.x + hull.center.x, hull.center.y - GetBounds(engineL).center.y, 0f));
        GameObject engineR = Spawn("Engine_01", wreck);
        AlignToFace(engineR, hull, Axis.Z, -1, overlap: 0.1f);
        Shift(engineR, new Vector3(engineX - GetBounds(engineR).center.x + hull.center.x, hull.center.y - GetBounds(engineR).center.y, 0f));

        // 날개: 왼쪽은 붙어 있고(약간 꺾임), 오른쪽은 떨어져 나감
        GameObject wingL = Spawn("Wing_01", wreck);
        AlignToFace(wingL, hull, Axis.X, -1, overlap: 0.2f);
        Shift(wingL, new Vector3(0f, hull.center.y - GetBounds(wingL).center.y, hull.center.z - GetBounds(wingL).center.z));
        Bounds wb = GetBounds(wingL);
        wingL.transform.RotateAround(new Vector3(wb.max.x, wb.center.y, wb.center.z), Vector3.forward, -18f);

        // 2) 추락 자세 적용 + 지면에 파묻기
        wreck.rotation = Quaternion.Euler(Pitch, Yaw, Roll);
        Bounds posed = GetBounds(wreck.gameObject);
        wreck.position += Vector3.up * (-posed.min.y - posed.size.y * BuriedRatio);

        // 엔진에서 피어오르는 진한 회색 연기
        GameObject smokePrefab = GetOrCreateSmokePrefab(GetOrCreateSmokeMaterial());
        AddSmoke(engineL.transform, GetBounds(engineL), smokePrefab, 1f);
        AddSmoke(engineR.transform, GetBounds(engineR), smokePrefab, 0.8f);

        // 3) 떨어져 나간 날개 + 잔해 (진행 방향 뒤쪽으로 흩뿌림)
        Transform debris = new GameObject("Debris").transform;
        debris.SetParent(root.transform, false);

        Vector3 trailDir = Quaternion.Euler(0f, Yaw, 0f) * Vector3.back;
        Vector3 sideDir = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
        Random.State prev = Random.state;
        Random.InitState(3303);

        GameObject wingR = Spawn("Wing_01", debris);
        wingR.transform.localScale = new Vector3(-1f, 1f, 1f);
        wingR.transform.rotation = Quaternion.Euler(8f, Yaw + 40f, 95f);
        PlaceOnGround(wingR, trailDir * (hull.size.z * 0.9f) + sideDir * (hull.size.x * 1.4f), 0.15f);

        string[] junk = { "Arm_01", "Reactor_01", "Engine_02", "Body_02", "Arm_01", "Engine_01" };
        for (int i = 0; i < junk.Length; i++)
        {
            GameObject piece = Spawn(junk[i], debris);
            piece.transform.rotation = Random.rotationUniform;
            float dist = hull.size.z * Random.Range(0.5f, 1.6f);
            Vector3 offset = trailDir * dist + sideDir * Random.Range(-hull.size.x, hull.size.x);
            PlaceOnGround(piece, offset, 0.2f);
        }
        Random.state = prev;

        // 4) 프리펩 저장 후 씬에 배치
        PrefabUtility.SaveAsPrefabAsset(root, SavePath);
        Object.DestroyImmediate(root);

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SavePath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, SceneManager.GetActiveScene());
        instance.transform.position = FindGroundPoint();
        Undo.RegisterCreatedObjectUndo(instance, "Build Crashed Ship");
        Selection.activeGameObject = instance;
        Debug.Log($"[CrashedShipBuilder] 생성 완료: {SavePath}");
    }

    private enum Axis { X, Y, Z }

    private static GameObject Spawn(string partName, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PartsDir + partName + ".prefab");
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    private static Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static void Shift(GameObject go, Vector3 delta) => go.transform.position += delta;

    /// <summary>part의 안쪽 면이 target의 sign 방향 바깥 면에 닿도록(overlap 비율만큼 겹침) 이동</summary>
    private static void AlignToFace(GameObject part, Bounds target, Axis axis, int sign, float overlap)
    {
        Bounds pb = GetBounds(part);
        int a = (int)axis;
        float targetFace = target.center[a] + sign * target.extents[a];
        float partInner = pb.center[a] - sign * pb.extents[a];
        float delta = targetFace - partInner - sign * pb.size[a] * overlap;
        Vector3 d = Vector3.zero;
        d[a] = delta;
        part.transform.position += d;
    }

    private static void CenterOn(GameObject part, Bounds target, bool x, bool y)
    {
        Bounds pb = GetBounds(part);
        part.transform.position += new Vector3(x ? target.center.x - pb.center.x : 0f, y ? target.center.y - pb.center.y : 0f, 0f);
    }

    /// <summary>root 기준 offset 위치에서 바닥이 지면에 닿도록(sinkRatio만큼 파묻어) 배치</summary>
    private static void PlaceOnGround(GameObject go, Vector3 offset, float sinkRatio)
    {
        Bounds b = GetBounds(go);
        go.transform.position += new Vector3(offset.x - b.center.x, -b.min.y - b.size.y * sinkRatio, offset.z - b.center.z);
    }

    private static Material GetOrCreateSmokeMaterial()
    {
        const string matPath = "Assets/Scenes/GU/Map/CrashedShipSmoke.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null) return mat;

        // 킷의 Smoke.mat은 빌트인 셰이더라 URP에서 핑크로 보이므로 URP 파티클 셰이더로 새로 만든다.
        mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Textures/Smoke.png"));
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 0f);     // Alpha
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    /// <summary>엔진 윗면 근처에 연기 프리펩 인스턴스를 배치한다. (이미 있는 프리펩은 덮어쓰지 않고 그대로 재사용)</summary>
    private static void AddSmoke(Transform engine, Bounds engineBounds, GameObject smokePrefab, float scale)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab, engine);
        go.name = "Smoke";
        go.transform.position = new Vector3(engineBounds.center.x, engineBounds.max.y - engineBounds.size.y * 0.2f, engineBounds.center.z);
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // 콘이 항상 하늘(+Y)을 향함
        go.transform.localScale = Vector3.one * scale;
    }

    /// <summary>위로 피어오르는 진회색 연기 파티클(월드 공간)을 독립 프리펩(EngineSmoke.prefab)으로 만든다.</summary>
    private static GameObject GetOrCreateSmokePrefab(Material mat)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SmokePath);
        if (existing != null) return existing;

        GameObject go = new GameObject("EngineSmoke");
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        const float scale = 1f;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f * scale, 2.2f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.12f, 0.12f, 0.12f, 0.85f), new Color(0.25f, 0.25f, 0.25f, 0.85f));
        main.gravityModifier = -0.03f;  // 살짝 부력
        main.maxParticles = 150;

        var emission = ps.emission;
        emission.rateOverTime = 14f * scale;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.4f * scale;

        // 위로 갈수록 커지고 옅어짐
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(1f, 2.2f)));

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.3f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.sortingFudge = 1f;

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(go, SmokePath);
        Object.DestroyImmediate(go);
        return asset;
    }

    private static Vector3 FindGroundPoint()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        if (Physics.Raycast(new Vector3(pivot.x, 2000f, pivot.z), Vector3.down, out RaycastHit hit, 4000f))
            return hit.point;
        return new Vector3(pivot.x, 0f, pivot.z);
    }
}
