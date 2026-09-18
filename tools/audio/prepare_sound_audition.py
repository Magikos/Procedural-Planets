"""Prepare the approved listening shortlist without operating Unity.

Run from any directory. --check validates the prepared assets without writes.
Existing clips are never overwritten with different bytes.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import uuid

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "Assets/AssetPacks/EnvironmentAudition"
SCENE = ROOT / "Assets/Scenes/Tests/SoundAudition.unity"
SCRATCH = Path("D:/Unity/Explore Assets/Assets")
AMBIENT = "Procedural Worlds/Ambient Sounds/Audio/Sounds/"
FARM = "Farm Animal Sounds/"
UNISTORM = "UniStorm Weather System/Sounds/"

# environment bits: coast=1, forest day=2, forest night=4, wetland=8
SHORTLIST = [
    ("ocean-a", "Ocean surf A", "Ocean", AMBIENT + "Ambient/sfx-ambient-water_ocean_0-0_lp.wav", True, 1),
    ("ocean-b", "Ocean surf B", "Ocean", "SurfaceData/Demo/Sounds/waves_ambient.wav", True, 1),
    ("stream-calm", "Calm stream", "Streams", FARM + "Bonus/Stream Calm Loop 1.wav", True, 8),
    ("stream-moderate", "Moderate stream", "Streams", FARM + "Bonus/Stream Moderate Loop 1.wav", True, 8),
    ("water-generic", "Water - lake candidate", "Lakes", AMBIENT + "Ambient/sfx-ambient-water_0-0_lp.wav", True, 8),
    ("wind-steady", "Steady wind", "Wind", AMBIENT + "Ambient/sfx-ambient-wind_0-0_lp.wav", True, 15),
    ("wind-winter", "Winter wind", "Wind", UNISTORM + "Wind/Winter Wind.wav", True, 0),
    ("wind-gust", "Wind gust", "Wind", UNISTORM + "Wind/Gust 1.wav", False, 0),
    ("cricket-a", "Crickets A", "Insects", UNISTORM + "Crickets/Cricket 1.wav", True, 12),
    ("cricket-b", "Crickets B", "Insects", AMBIENT + "Insects/sfx-insects-cricket_3-0-lp.wav", True, 12),
    ("cicada-pixabay", "Cicadas - BlenderTimer", "Insects", "C:/Users/Bryan/Downloads/blendertimer-cicada-419563.mp3", True, 2),
    ("bird-sparrow", "Sparrow call", "Birds", AMBIENT + "Birds/sfx-birds-sparrow_0-0.wav", False, 2),
    ("bird-mixed", "Mixed bird calls", "Birds", AMBIENT + "Birds/sfx-birds-mixed_0-0.wav", False, 10),
    ("frog", "Frog call", "Frogs", AMBIENT + "Amphibians/sfx-amphibian-frog_0-0.wav", False, 8),
    ("rain-light", "Light rain", "Rain", UNISTORM + "Weather Effects/Light Rain.wav", True, 0),
    ("rain-heavy", "Heavy rain", "Rain", UNISTORM + "Weather Effects/Heavy Rain 1.wav", True, 0),
    ("thunder", "Thunder", "Thunder", UNISTORM + "Thunder/Thunder 1.wav", False, 0),
    ("forest", "Forest background", "Forest", FARM + "Bonus/Forest Loop.wav", True, 2),
    ("underwater", "Underwater background", "Underwater", "Fantacode Studios/Swimming System/Audio/Underwater Sound Effect.wav", True, 0),
    ("campfire", "Campfire", "Fire", "SurfaceData/Demo/Sounds/campfire.wav", True, 0),
]

GAPS = [
    ("leaves", "Broadleaf rustle", "Foliage", "Find wind through broad leaves without loud birds or traffic."),
    ("pine", "Pine wind", "Foliage", "Find wind through pine needles, distinct from broadleaf rustle."),
    ("grass", "Grass movement", "Foliage", "Find nearby grass moving in light wind."),
    ("reeds", "Reed movement", "Foliage", "Find dry and wet reeds moving beside water."),
    ("lake-lapping", "Gentle lake lapping", "Lakes", "Find small shore waves without ocean roar or flowing stream noise."),
    ("rain-leaves", "Rain on leaves", "Rain", "Find close leaf impacts to layer over the general rain sound."),
    ("waterfall", "Distant waterfall", "Streams", "Find a steady distant waterfall without voices."),
]


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, "ProceduralPlanets/SoundAudition/" + path.relative_to(ROOT).as_posix()).hex


def meta(path, audio=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return re.search(r"^guid: (\w+)$", target.read_text(), re.M)[1]
    body = f"fileFormatVersion: 2\nguid: {guid(path)}\n"
    if path.is_dir():
        body += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    if audio:
        body += """AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 2
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 0
  loadInBackground: 1
  ambisonic: 0
  3D: 0
  userData: Audition source; streaming PCM; preserve stereo and original level
  assetBundleName:
  assetBundleVariant:
"""
    target.write_text(body, encoding="utf-8")
    return guid(path)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def prepare():
    sources = [Path(row[3]) if Path(row[3]).is_absolute() else SCRATCH / row[3] for row in SHORTLIST]
    for source in sources:
        if not source.is_file():
            raise FileNotFoundError(source)
    DEST.mkdir(parents=True, exist_ok=True)
    meta(DEST)
    rows = []
    for (identity, title, category, _, loop, environments), source in zip(SHORTLIST, sources):
        target = DEST / (identity + source.suffix)
        if target.exists() and sha(source) != sha(target):
            raise ValueError(f"Refusing to overwrite changed recording: {target}")
        if not target.exists():
            shutil.copyfile(source, target)
        source_guid = meta(target, audio=True)
        pack = source.relative_to(SCRATCH).parts[0] if source.is_relative_to(SCRATCH) else "Pixabay / BlenderTimer"
        url = "https://pixabay.com/sound-effects/" if pack.startswith("Pixabay") else ""
        license_note = "Owned Asset Store pack. Verify included audio terms before production use."
        if pack.startswith("Pixabay"):
            license_note = "User-supplied Pixabay download. Retain the recording page/license before production use."
        listen = "Check unwanted voices, traffic, distortion, and repetition. Compare at a similar perceived volume."
        if loop:
            listen += " Looping is a review setting, not proof of a seamless edit."
        if identity == "water-generic":
            listen = "Decide whether this sounds like lake lapping or flowing water. Filename is ambiguous."
        rows.append(dict(Id=identity, Title=title, Category=category, Loop=loop, Environments=environments,
                         Source=str(source), SourceUrl=url, License=license_note, ListenFor=listen,
                         AssetPath=target.relative_to(ROOT).as_posix(), Guid=source_guid,
                         OriginalPath=str(source), Sha256=sha(target)))
    for identity, title, category, listen in GAPS:
        rows.append(dict(Id="missing-" + identity, Title=title, Category=category, Loop=True, Environments=0,
                         Source="Not selected yet", SourceUrl="", License="No recording yet", ListenFor=listen,
                         AssetPath="", Guid="", OriginalPath="", Sha256=""))
    (DEST / "catalog.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
    meta(DEST / "catalog.json")
    for path in [ROOT / "Assets/Scripts/Planet/AudioReview", ROOT / "Assets/Scripts/Planet/AudioReview/SoundAudition.cs",
                 ROOT / "Assets/Scripts/Planet/AudioReview/SoundReviewStore.cs", ROOT / "Assets/Editor/SoundAuditionMenu.cs",
                 ROOT / "Assets/Tests/EditMode/SoundAuditionTests.cs"]:
        meta(path)

    # Reuse the project's serialized camera and scene defaults, without its bird/light components.
    template = (ROOT / "Assets/Scenes/Tests/BirdAnimationReview.unity").read_text(encoding="utf-8-sig")
    scene = template.split("--- !u!1 &1073807848")[0]
    review = template.split("--- !u!1 &1073807848")[1].split("--- !u!1 &1101467288")[0]
    review = "--- !u!1 &1073807848" + review
    review = review.replace("Bird Animation Review", "Environment Sound Audition")
    review = review.replace("969fae4674ad498882eac1ae074e91f0", meta(ROOT / "Assets/Scripts/Planet/AudioReview/SoundAudition.cs"))
    review = review.replace("ProceduralPlanets.Planet::BirdAnimationReview", "ProceduralPlanets.Planet::SoundAudition")
    fields = "  Candidates:\n"
    for row in rows:
        fields += "  - Id: " + json.dumps(row["Id"]) + "\n"
        for key in ("Title", "Category"):
            fields += "    " + key + ": " + json.dumps(row[key]) + "\n"
        fields += "    Clip: " + ("{fileID: 8300000, guid: " + row["Guid"] + ", type: 3}" if row["Guid"] else "{fileID: 0}") + "\n"
        fields += f"    Loop: {int(row['Loop'])}\n    Environments: {row['Environments']}\n"
        for key in ("Source", "SourceUrl", "License", "ListenFor"):
            fields += "    " + key + ": " + json.dumps(row[key]) + "\n"
    review = re.sub(r"  Action:.*?(?=--- !u!4)", lambda _: fields, review, flags=re.S)
    scene += review + """--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {fileID: 1073807850}
  - {fileID: 237493587}
"""
    SCENE.write_text(scene, encoding="utf-8")
    meta(SCENE)
    print(f"Prepared {len(SHORTLIST)} recordings and {len(GAPS)} missing entries.")


def check():
    rows = json.loads((DEST / "catalog.json").read_text())
    assert len({row["Id"] for row in rows}) == len(rows), "Duplicate candidate IDs"
    scene = SCENE.read_text()
    total = 0
    for row in rows:
        assert "  - Id: " + json.dumps(row["Id"]) in scene, row["Id"]
        if not row["AssetPath"]:
            continue
        path = ROOT / row["AssetPath"]
        assert path.is_file() and sha(path) == row["Sha256"], path
        assert row["Guid"] in Path(str(path) + ".meta").read_text(), path
        assert row["Guid"] in scene, path
        total += path.stat().st_size
    assert scene.count("AudioListener:") == 1
    assert "BirdAnimationReview" not in scene
    print(f"PASS: {len(rows)} unique entries; 20 recording hashes and scene references; one listener; {total / 1048576:.1f} MiB.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.check:
        prepare()
    check()
