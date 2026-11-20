using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using B612;
using UnityEngine;

public static class ChapterJourneyValidation
{
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static object Plans()
    {
        var settings=UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset");
        var rows=new List<object>();int water=0,glides=0;
        for(int chapter=1;chapter<=3;chapter++)foreach(int runSeed in new[]{612101,1,77})foreach(float size in new[]{.7f,1f,1.5f,2f})
        {
            int systemSeed = chapter == 1 ? runSeed : StarSystemFactory.DeriveSeed(runSeed, 50, chapter);
            var data=StarSystemFactory.Create(systemSeed,settings,chapter);Check(data.systemIndex==chapter&&data.planets.Count==6&&data.links.Count==7,"Chapter graph changed");
            Check(data.planets[0].spec.requiredCapabilities==ChapterTerrainDefinition.Entry(chapter)&&data.planets[0].spec.unlock==ChapterTerrainDefinition.Reward(chapter),"Unlock chain mismatch");
            int seed = data.planets[size == 1f ? 0 : 1].spec.seed;
            int required = size == 1f ? chapter - 1 : -1;
            int f=PlanetSurfaceGrid.CalculateFrequency(settings.subdivisions,size);var definition=settings.Chapter(chapter);
            var plan=ProceduralPlanetTerrain.CreatePlan(seed,settings.nominalRadius*size,settings.spawnDirection,
                definition.featureBudget,chapter,f,definition.regionWeights,required,appearance:settings.terrainAppearance);
            Check(plan.regions.Count==20*f*f&&plan.features.Count<=2&&plan.placementAttempts<=128,"Region/feature count mismatch");
            var topology=PlanetSurfaceGrid.CreateTopology(f);
            foreach(var cell in plan.regions)
            {
                int id=cell.id;var seen=new HashSet<int>();
                while(plan.regions[id].parent>=0)
                {Check(seen.Add(id),"Patch growth cycle");int parent=plan.regions[id].parent;Check(topology[id].neighbors.Contains(parent)&&plan.regions[parent].patch==cell.patch,"Disconnected region patch");id=parent;}
                Check(id==plan.patches[cell.patch].cellId,"Region did not grow from its seed");
                Check(float.IsFinite(cell.moisture)&&cell.moisture>=0&&cell.moisture<=1,"Invalid independent field");
            }
            var repeat=ProceduralPlanetTerrain.CreatePlan(seed,settings.nominalRadius*size,settings.spawnDirection,
                definition.featureBudget,chapter,f,definition.regionWeights,required,appearance:settings.terrainAppearance);
            Check(JsonUtility.ToJson(plan)==JsonUtility.ToJson(repeat),"Plan not reproducible");
            Check(plan.version == PlanetGenerationSettings.GeneratorVersion && !ReferenceEquals(plan.appearance.macroKeys, settings.terrainAppearance.macroKeys),
                "Missing version or independent height snapshot");
            Check(plan.landformAttempts <= TerrainLandforms.MaxAttempts, "Unbounded landform placement");
            float min = 0f, max = 0f;
            for (int i = 0; i < 512; i++)
            {
                float y = (i + .5f) / 256f - 1f, angle = i * 2.399963f;
                float radial = Mathf.Sqrt(1f - y * y);
                var n = new Vector3(radial * Mathf.Cos(angle), y, radial * Mathf.Sin(angle));
                float free = ProceduralPlanetTerrain.FreeHeight(plan, n);
                Check(float.IsFinite(ProceduralPlanetTerrain.Height(plan, n)) && Mathf.Abs(free) <= plan.amplitude + 1e-5f,
                    "Nonfinite height or free height outside budget");
                min = Mathf.Min(min, free);
                max = Mathf.Max(max, free);
            }
            foreach (var direction in ProceduralPlanetTerrain.MainRoute(plan))
                Check(Mathf.Abs(ProceduralPlanetTerrain.BaseHeight(plan, direction)) < .002f, "Main route heights changed");
            if (required >= 0)
                foreach (var connector in plan.connectors)
                    Check(Mathf.Abs(ProceduralPlanetTerrain.BaseHeight(plan, connector.anchor)) < .002f, "Connector anchor is not protected");
            if(plan.river!=null)
            {
                water++;var river=plan.river;for(int i=1;i<river.levels.Count;i++)Check(river.levels[i]<river.levels[i-1]&&river.beds[i]<river.levels[i],"River flow not downhill/contained");
                Check(Mathf.Abs(river.beds[river.fordIndex]+.06f)<.001f,"Protected ford depth changed");
                var mesh = TerrainWater.CreateMesh(plan);
                try
                {
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    Check(vertices.Length == 42 && triangles.Length == 120, "Water ribbon missing or double-sided");
                    for (int i = 0; i < vertices.Length; i++)
                        Check(Vector3.Dot(mesh.normals[i], vertices[i].normalized) > .99f, "Invalid water shading normal");
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var a = vertices[triangles[i]];
                        Check(Vector3.Dot(a, Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a)) > 0f,
                            "Water triangle faces below the surface");
                    }
                    Check(vertices[0].magnitude < plan.radius + river.beds[0] &&
                        vertices[vertices.Length - 1].magnitude < plan.radius + river.beds[river.beds.Count - 1], "Water ends are exposed above the bed");
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                Check(TerrainWater.MovementFactor(plan,river.directions[10],-.05f)==.65f&&TerrainWater.MovementFactor(plan,river.directions[10],2)==1,"Water slowdown ignored height");
            }
            glides+=plan.features.Count(feature=>feature.kind==2);
            rows.Add(new{chapter,runSeed,planetSeed=seed,size,plan.amplitude,min,max,plan.macroSeed,plan.hillSeed,
                cells=plan.regions.Count,patches=plan.patches.Count,river=plan.river!=null,features=plan.features.Select(x=>x.kind).ToArray(),
                plan.landformAttempts, landforms = plan.landforms.Select(x => new { x.height, x.radius }).ToArray()});
        }
        Check(water>0&&water<rows.Count&&glides>0,"Water, water omission or glide feature never selected");
        Directory.CreateDirectory("Artifacts/TerrainAppearanceV6");
        File.WriteAllText("Artifacts/TerrainAppearanceV6/Plans.json",Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
        return new{passed=true,cases=rows.Count,water,glides};
    }
    public static object Prepared(int chapter)
    {
        Check(Application.isPlaying,"Use Play mode");var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();var settings=flow.worldHost.settings;
        var root=new GameObject("Chapter preparation validation");root.transform.position=new Vector3(1000,0,0);var host=root.AddComponent<PlanetWorldHost>();host.settings=settings;var rows=new List<object>();
        try
        {
            foreach(float size in new[]{.7f,1f,2f})
            {
                var data=StarSystemFactory.Create(612101,settings,chapter).planets[size==1f?0:1];data.spec.terrain.sizeScale=size;data.spec.terrain.radius=settings.nominalRadius*size;data.spec.sizeClass=PlanetGenerationSettings.Classify(size);
                var watch=System.Diagnostics.Stopwatch.StartNew();var world=host.Prepare(data,flow.player.GetComponent<CapsuleCollider>());watch.Stop();
                try
                {
                    var terrain=world.Terrain;var plan=terrain.GenerationPlan;Check(plan.regions.Count==world.surface.Cells.Count||plan.safeFallback,"Region and final grid mismatch");Check(plan.buildAttempts<=5,"Repair budget exceeded");
                    Check(((MeshCollider)world.ground).sharedMesh==terrain.OwnedMesh,"Collision mismatch");
                    foreach(var cell in world.surface.Cells)foreach(var slot in cell.slots)Check(terrain.AllowsSpawn(world.surface.WorldPoint(slot),world.surface.WorldNormal(slot)),"Spawn is in water/protected site");
                    Check(world.surface.SlotCount>40,"Insufficient safe spawns");
                    if(plan.river!=null)Check(terrain.WaterMesh&&world.GetComponentsInChildren<MeshCollider>().Length==1,"Water became a second floor");
                    Check(plan.mainRouteSeconds>0&&plan.traversalCosts.All(c=>c.shortcutSeconds<c.basicSeconds),"Traversal cost benefit missing");
                    rows.Add(new{chapter,size,features=plan.features.Select(x=>x.kind).ToArray(),river=plan.river!=null,plan.buildAttempts,plan.repairs,world.surface.SlotCount,plan.mainRouteSeconds,plan.traversalCosts,milliseconds=watch.Elapsed.TotalMilliseconds});
                }
                finally{UnityEngine.Object.DestroyImmediate(world.gameObject);}
            }
            Directory.CreateDirectory("Temp/ChapterJourney");File.WriteAllText("Temp/ChapterJourney/Prepared"+chapter+".json",Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));return new{passed=true,rows};
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    public static object ReviewedStarts(int chapter)
    {
        Check(Application.isPlaying, "Use Play mode");
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var settings = flow.worldHost.settings;
        var actor = flow.player.GetComponent<CapsuleCollider>();
        var definition = settings.Chapter(chapter);
        var fixture = new GameObject("Reviewed start validation");
        fixture.transform.position = new Vector3(100, 0, 0);
        var host = fixture.AddComponent<PlanetWorldHost>();
        host.settings = settings;
        var rows = new List<object>();
        try
        {
            foreach (int runSeed in new[] { 612101, 1, 77 })
            {
                int systemSeed = chapter == 1 ? runSeed : StarSystemFactory.DeriveSeed(runSeed, 50, chapter);
                var data = StarSystemFactory.Create(systemSeed, settings, chapter).planets[0];
                var world = host.Prepare(data, actor);
                try
                {
                    var plan = world.Terrain.GenerationPlan;
                    int frequency = world.surface.PreparedFrequency;
                    Check(plan.requiredKind == chapter - 1 && plan.features[0].kind == plan.requiredKind &&
                        plan.connectors.Count == 2 && plan.buildAttempts <= ProceduralPlanetTerrain.MaxBuildAttempts,
                        "Required course or bounded attempts missing");
                    var expected = ProceduralPlanetTerrain.CreatePlan(plan.seed, plan.radius, plan.spawn, definition.featureBudget,
                        chapter, frequency, definition.regionWeights, plan.requiredKind, plan.reviewedLayout, settings.terrainAppearance);
                    Check(JsonUtility.ToJson(plan.features[0]) == JsonUtility.ToJson(expected.features[0]) &&
                        plan.connectors.Select(c => JsonUtility.ToJson(c)).SequenceEqual(expected.connectors.Select(c => JsonUtility.ToJson(c))),
                        "Reviewed course layout is not reproducible");
                    Check(((MeshCollider)world.ground).sharedMesh == world.Terrain.OwnedMesh &&
                        world.ground.GetComponent<MeshFilter>().sharedMesh == world.Terrain.OwnedMesh, "Collision mesh differs");
                    Check(world.groundRenderer.sharedMaterials.Length == 1 && world.Terrain.OwnedMesh.subMeshCount == 1 &&
                        world.Terrain.OwnedMesh.colors.Length == world.Terrain.OwnedMesh.vertexCount, "Missing continuous ground data");
                    if (runSeed == 612101) CheckClosedMesh(world.Terrain.OwnedMesh);
                    CheckShape(world, plan);
                    Check(world.surface.SlotCount > 40 && world.surface.Cells.All(cell => cell.slots.All(slot =>
                        world.Terrain.AllowsSpawn(world.surface.WorldPoint(slot), world.surface.WorldNormal(slot)))),
                        "Missing safe baobab slots or slots inside protected route");
                    rows.Add(new { chapter, runSeed, planetSeed = plan.seed, plan.requiredKind, plan.reviewedLayout,
                        plan.fixedPlacement, plan.buildAttempts, plan.repairs, plan.traversalCosts });
                    if (runSeed == 612101) VerifyFixedCourse(world, actor, plan, definition, frequency);
                }
                finally { UnityEngine.Object.DestroyImmediate(world.gameObject); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
        Directory.CreateDirectory("Artifacts/TerrainAppearanceV6");
        File.WriteAllText("Artifacts/TerrainAppearanceV6/Reviewed" + chapter + ".json",
            Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
        return new { passed = true, chapter, cases = rows.Count, fixedCourseVerified = true, rows };
    }

    static void VerifyFixedCourse(PlanetWorldInstance world, CapsuleCollider actor, ProceduralPlanetTerrain.Plan plan,
        ChapterTerrainDefinition definition, int frequency)
    {
        var fallback = ProceduralPlanetTerrain.CreatePlan(plan.seed, plan.radius, plan.spawn, definition.featureBudget,
            plan.chapter, frequency, definition.regionWeights, plan.requiredKind, ProceduralPlanetTerrain.MaxBuildAttempts - 1,
            plan.appearance);
        Check(fallback.fixedPlacement && fallback.features.Count == 1 && fallback.connectors.Count == 2 && fallback.river == null,
            "Fixed course lost its required terrain");
        var build = typeof(ProceduralPlanetTerrain).GetMethod("BuildMesh",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var mesh = (Mesh)build.Invoke(null, new object[] { fallback });
        var ground = (MeshCollider)world.ground;
        var filter = ground.GetComponent<MeshFilter>();
        var previous = ground.sharedMesh;
        try
        {
            ground.sharedMesh = mesh;
            filter.sharedMesh = mesh;
            Physics.SyncTransforms();
            CheckShape(world, fallback);
            var verdict = TerrainTraversalVerifier.Verify(ground, actor, fallback);
            Check(verdict.passed, "Fixed layout failed actual movement: " + verdict.reason);
        }
        finally
        {
            ground.sharedMesh = previous;
            filter.sharedMesh = previous;
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    static void CheckClosedMesh(Mesh mesh)
    {
        var edges = new Dictionary<ulong, int>();
        var triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        for (int side = 0; side < 3; side++)
        {
            int a = triangles[i + side], b = triangles[i + (side + 1) % 3];
            ulong key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }
        Check(edges.Values.All(count => count == 2) && mesh.vertexCount - edges.Count + triangles.Length / 3 == 2,
            "Reviewed mesh has a split seam or pole");
        Check(mesh.normals.All(n => float.IsFinite(n.x) && n.sqrMagnitude > .99f), "Invalid reviewed mesh normals");
    }

    static void CheckShape(PlanetWorldInstance world, ProceduralPlanetTerrain.Plan plan)
    {
        var feature = plan.features[0];
        Vector2[] samples = feature.kind switch
        {
            0 => new[] { new Vector2(-2.18f, 0), new Vector2(-2.02f, 0), Vector2.zero,
                new Vector2(2.02f, 0), new Vector2(2.18f, 0) },
            1 => new[] { new Vector2(-.1f, 0), new Vector2(.1f, 0), new Vector2(4f, 0), new Vector2(2.6f, 1f) },
            _ => new[] { new Vector2(-2f, 0), new Vector2(-1.85f, 0), new Vector2(5f, 0),
                new Vector2(5.15f, -1f), new Vector2(5.15f, 1f), new Vector2(8f, -1f), new Vector2(8f, 1f) }
        };
        foreach (Vector2 sample in samples)
        {
            Vector2 measured = sample;
            bool hit = world.surface.TryProject(feature.Direction(measured, plan.radius), out var point, out _);
            // An exact vertex ray can miss both adjacent PhysX triangles at floating-point precision.
            // Retry 0.2 mm inside the surface and evaluate the authored height at that same point.
            if (!hit)
            {
                measured += new Vector2(.0002f, .0002f);
                hit = world.surface.TryProject(feature.Direction(measured, plan.radius), out point, out _);
            }
            Check(hit, "Missing reviewed surface at " + sample);
            float expected = TraversalTestCourseGeometry.Height(feature.kind, measured.x, measured.y);
            float actual = Vector3.Distance(point, world.transform.position) - plan.radius;
            Check(Mathf.Abs(actual - expected) < .025f, $"Reviewed dimensions changed at {sample}: {actual:F3} versus {expected:F3}");
        }
    }
}
