using System.Collections.Generic;
using UnityEngine;

/// <summary>Playable production-tree harvest review, independent of the approved humanoid animation fixture.</summary>
public sealed class TreeHarvestReview : MonoBehaviour, IPlanetSurfaceSampler
{
    public string Species = "Conifer";
    public float Age = .8f;
    public bool AutoChop;
    public string Status { get; private set; }
    public int Wood { get; private set; }
    public int Strikes { get; private set; }
    public ScatterHarvestStore Store { get; private set; }
    public ScatterPrototypeDto Prototype { get; private set; }
    public GameObject Standing { get; private set; }
    public Camera Camera;
    ScatterLibraryDto _library;
    HarvestService _harvest;
    TreeHarvestService _processing;
    TreeFallSystem _fall;
    ChopFxSystem _fx;
    LogRenderer _logs;
    StumpRenderer _stumps;
    Transform _planet;
    float _clock;
    readonly List<ScatterHarvestStore.LogRecord> _scratch = new();
    readonly List<Material> _materials = new();
    ScatterLibraryDto _generated;
    readonly List<GeneratedTree> _owned = new();

    void Start()
    {
        var center = new GameObject("Review planet center"); center.transform.SetParent(transform,false);
        center.transform.position = new Vector3(0,-1000000,0); _planet=center.transform;
        var source=Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        _generated=TreeInjection.Apply(ScatterLibraryDto.From(source));
        ResetTree();
    }

    public void ResetTree()
    {
        if (_generated == null) return;
        _fall?.Dispose(); _fx?.Dispose(); _logs?.Dispose(); _stumps?.Dispose();
        if (Standing != null) Destroy(Standing);
        foreach(var material in _materials) if(material!=null) Destroy(material);
        _materials.Clear();
        foreach(var tree in _owned) tree.Dispose(); _owned.Clear();
        var species = TreeDefLibrary.TryParseSpecies(Species,out var parsed) ? parsed : TreeDefLibrary.TreeSpecies.Conifer;
        ScatterPrototypeDto template=null;
        foreach(var prototype in _generated.Prototypes)
            if(prototype.Tree?.SpeciesName==Species && prototype.Parts.Length>1) { template=prototype; break; }
        if(template==null) { Status="No generated species material"; return; }
        var definition=TreeDefLibrary.Species(species,Age);
        var generated=TreeGenerator.Generate(definition,719); _owned.Add(generated);
        var parts=new ScatterPartDto[2];
        for(int i=0;i<2;i++)
        {
            var material=new Material(template.Parts[i].Material); _materials.Add(material);
            if(material.HasProperty("_FadeStart")) material.SetFloat("_FadeStart",8000);
            if(material.HasProperty("_FadeEnd")) material.SetFloat("_FadeEnd",10000);
            parts[i]=template.Parts[i] with { Material=material, LodMeshes=new[]{i==0?generated.Bark:generated.Foliage} };
        }
        Prototype=template with { Tree=generated, Parts=parts, StumpMesh=generated.Stump, StumpMaterial=parts[0].Material };
        _library=new ScatterLibraryDto(new[]{Prototype}); Store=new ScatterHarvestStore();
        Wood=0; Strikes=0; _clock=0; Status=generated.IsSapling?"Sapling":"Standing";
        Standing=new GameObject("Standing review tree");
        foreach(var part in parts) TreeFallSystem.AddPart(Standing.transform,part.LodMeshes[0],part.Material);
        _harvest=new HarvestService(p=>Store.RecordStump(p),(_,__)=>Standing.SetActive(false),(_,count)=>Wood+=count,
            _=>new ProtoHarvestInfo(ScatterInteraction.Chop,Species,generated.ChopHp,new HarvestYield("Wood",generated.IsSapling?generated.WoodYield:0)),
            Store.RecordDug,readHealth:Store.RemainingHealth,writeHealth:Store.RecordDamage);
        _processing=new TreeHarvestService(Store,()=>_library,(_,count)=>Wood+=count);
        _fall=new TreeFallSystem(_planet,()=>_library,Store,this);
        _fx=new ChopFxSystem(_planet,()=>_library,Store);
        _logs=new LogRenderer(Store,_planet,()=>_library); _stumps=new StumpRenderer(Store,_planet,()=>_library);
    }

    public void Strike()
    {
        if(Prototype==null) return;
        if(!Store.Contains(1))
        {
            var result=_harvest.TryHarvest(new ScatterPick(1,0,Vector3.zero,Quaternion.identity,1),ToolTier.BasicAxe);
            Strikes++; Status=result.Outcome==HarvestOutcome.Hit?"Chopping trunk":Prototype.Tree.IsSapling?"Sapling harvested":"Falling";
            if (Status == "Sapling harvested") AutoChop = false;
            return;
        }
        Store.CollectLogs(_scratch);
        if(_scratch.Count==0) return;
        var log=_scratch[0]; if(Store.IsFalling(log.Id)) return;
        int section=0;
        while(section<Prototype.Tree.LogSections.Length && (log.RemovedSections&(1u<<section))!=0) section++;
        if(section==Prototype.Tree.LogSections.Length) {Status="Harvest complete";AutoChop=false;return;}
        Vector3 point=log.Position+log.Rotation*Prototype.Tree.SectionAnchors[section];
        _processing.Strike(log.Id,point,ToolTier.BasicAxe); Strikes++;
        Store.TryGetLog(log.Id,out log);
        Status=log.RemovedBranches==7?"Processing trunk sections":"Removing branches";
    }

    void Update()
    {
        if(AutoChop) { _clock+=Time.deltaTime; if(_clock>=.8f) {_clock=0;Strike();} }
        Render(Camera);
    }

    public void Render(Camera camera) { _logs?.Render(camera); _stumps?.Render(camera); }
    public bool TryGetSurfaceRadius(Vector3 direction,out float radius)
    { radius=direction.y>.1f?1000000f/direction.y:0;return radius>0; }

    void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space) { Strike(); Event.current.Use(); }
        GUILayout.BeginArea(new Rect(15,15,330,190),GUI.skin.box);
        GUILayout.Label($"{Species} — {Status}");
        GUILayout.Label($"{Strikes} strikes / {Wood} wood");
        GUILayout.Label("Space: strike. Capsule: 2 metre player reference.");
        AutoChop=GUILayout.Toggle(AutoChop,"Repeat chopping");
        if(GUILayout.Button("Reset mature tree")) {Age=.8f;ResetTree();}
        if(GUILayout.Button("Reset sapling")) {Age=.25f;ResetTree();}
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        _fall?.Dispose();_fx?.Dispose();_logs?.Dispose();_stumps?.Dispose();
        foreach(var tree in _owned) tree.Dispose();
        if(_generated!=null) foreach(var prototype in _generated.Prototypes) prototype.Tree?.Dispose();
        foreach(var material in _materials) if(material!=null) Destroy(material);
        if(Standing!=null) Destroy(Standing);
    }
}
