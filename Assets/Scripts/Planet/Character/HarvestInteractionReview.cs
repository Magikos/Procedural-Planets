using UnityEngine;

/// <summary>Playable harvesting fixture using the shared interaction clock and harvest service.</summary>
public sealed class HarvestInteractionReview : MonoBehaviour
{
    public HumanInteractionReview Review;
    public Transform Contact;
    public Transform Tree;
    public ScatterInteraction Interaction = ScatterInteraction.Chop;
    [Min(1)] public int HitPoints = 3;
    public int Wood { get; private set; }
    public int Stone { get; private set; }
    public string CollectionItem = "Plant";
    public Transform CollectedPart;
    public int Collected { get; private set; }
    public bool Felled { get; private set; }
    public int Impacts => _interaction?.Impacts ?? 0;
    HarvestInteraction _interaction;
    Quaternion _standing;
    float _fall;
    Vector3 _standingScale;
    Vector3 _partPosition, _partScale;

    void OnEnable()
    {
        if (Review == null || Contact == null || Tree == null) return;
        _standing = Tree.localRotation;
        _standingScale = Tree.localScale;
        if (CollectedPart != null) { _partPosition = CollectedPart.localPosition; _partScale = CollectedPart.localScale; }
        ResetTree();
        Review.InteractionMarker += OnMarker;
    }

    void OnDisable()
    {
        if (Review != null) Review.InteractionMarker -= OnMarker;
    }

    public void ResetTree()
    {
        Review?.Cancel();
        Wood = 0; Stone = 0; Collected = 0; Felled = false; _fall = 0f;
        if (CollectedPart != null) { CollectedPart.localPosition = _partPosition; CollectedPart.localScale = _partScale; }
        if (Tree == null || Contact == null) return;
        Contact.gameObject.SetActive(true);
        Tree.localRotation = _standing;
        Tree.localScale = _standingScale;
        var pick = new ScatterPick(1, 0, Tree.position, Tree.rotation, 1f);
        Vector3 contactPosition = Contact.position;
        var service = new HarvestService(_ => { if (Felled) return false; Felled = true; return true; },
            (_, _) => { }, (item, count) => { if (Interaction == ScatterInteraction.Collect) Collected += count; else if (item == "Stone") Stone += count; else if (item == "Wood") Wood += count; },
            _ => new ProtoHarvestInfo(Interaction, Interaction == ScatterInteraction.Collect ? CollectionItem : Interaction == ScatterInteraction.Mine ? "Review stone" : "Review tree", HitPoints), null);
        var tool = Interaction == ScatterInteraction.Collect ? new ToolTier("Hands", 1) : Interaction == ScatterInteraction.Mine ? ToolTier.BasicPickaxe : ToolTier.BasicAxe;
        _interaction = new HarvestInteraction(service, pick, tool, () =>
            !Felled && Tree != null && Tree.gameObject.activeInHierarchy && Contact != null &&
            Contact.gameObject.activeInHierarchy && Review != null && Review.Actor != null &&
            Vector3.Distance(Contact.position, contactPosition) <= .05f &&
            Vector3.Distance(Tree.position, pick.Position) <= .05f &&
            Review.Actor.Actor != null && Vector3.Distance(Review.Actor.Actor.position, Contact.position) <= 2f);
    }

    void OnMarker(HumanInteractionReview.Target target, string marker)
    {
        if (target.Contact != Contact || marker != "HarvestImpact" || _interaction == null) return;
        var result = _interaction.Strike();
        if (result.Outcome == HarvestOutcome.NotHarvestable) Review.Cancel();
        else if (result.Outcome != HarvestOutcome.Hit) Review.Session.Continue();
    }

    void Update() => Tick(Time.deltaTime);

    public void Tick(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new System.ArgumentOutOfRangeException(nameof(dt));
        if (!Felled || Tree == null) return;
        if (Review != null && !Review.Session.Active && Contact != null) Contact.gameObject.SetActive(false);
        _fall = Mathf.MoveTowards(_fall, 1f, dt / 1.2f);
        if (Interaction == ScatterInteraction.Collect)
        {
            if (CollectedPart != null)
                CollectedPart.localScale = Vector3.Lerp(CollectedPart.localScale, Vector3.zero, 1f - Mathf.Exp(-dt * 12f));
        }
        else if (Interaction == ScatterInteraction.Mine)
        {
            // Keep the final strike surface until the tool has recovered, then settle the depleted fixture.
            if (Review != null && !Review.Session.Active)
                Tree.localScale = Vector3.Lerp(Tree.localScale, _standingScale * .3f, 1f - Mathf.Exp(-dt * 5f));
        }
        else Tree.localRotation = _standing * Quaternion.Euler(Mathf.SmoothStep(0f, 80f, _fall), 0f, 0f);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 290, 15, 275, 100), GUI.skin.box);
        GUILayout.Label(Interaction == ScatterInteraction.Collect ? $"Gathering: {Collected} {CollectionItem}" : Interaction == ScatterInteraction.Mine ? $"Stone mining: {Impacts} impacts, {Stone} stone" : $"Tree chopping: {Impacts} impacts, {Wood} wood");
        GUILayout.Label("E: start / finish. Move: cancel.");
        if (GUILayout.Button("Reset harvest target")) ResetTree();
        GUILayout.EndArea();
    }
}
