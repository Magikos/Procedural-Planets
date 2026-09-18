"""Package explicitly selected animation captures without changing source frames."""

import argparse
import html
import json
from pathlib import Path
import shutil
import struct
import subprocess
import textwrap
from urllib.parse import quote


def labels_for(metadata):
    if metadata.get("columns"):
        return metadata["columns"]
    if metadata.get("stageOrder"):
        source_label = metadata["stageOrder"].split(";")[0].removeprefix("Columns ")
        source_label = source_label.split(" (clip provenance:")[0].replace("A ", "A: ", 1)
        return [source_label, "B: production graph / no corrections", "C: production presenter"]
    sources = metadata.get("records", [{}])[0].get("sources", [])
    if sources:
        return ["Source candidate: " + Path(s["ClipAssetPath"]).stem for s in sources]
    return ["Unclassified capture: see metadata"]


def capture_data(folder):
    metadata_path = next((folder / name for name in ("metadata.json", "metrics.json")
                          if (folder / name).is_file()), None)
    if metadata_path is None:
        raise ValueError(f"Missing metadata: {folder}")
    metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    frames = sorted(folder.glob("frame-*.png"))
    if not frames or any(p.name != f"frame-{i:04d}.png" for i, p in enumerate(frames)):
        raise ValueError(f"Missing or nonconsecutive frames: {folder}")
    fps = float(metadata.get("captureHz", 30))
    if fps != 30:
        raise ValueError(f"Expected 30 Hz capture, found {fps}: {folder}")
    with frames[0].open("rb") as stream:
        header = stream.read(24)
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Invalid PNG: {frames[0]}")
    width, height = struct.unpack(">II", header[16:24])
    return metadata_path, metadata, frames, width, height


def encode(folder, ffmpeg, ffprobe):
    metadata_path, metadata, frames, width, _ = capture_data(folder)
    labels = labels_for(metadata)
    font = Path("C:/Windows/Fonts/arial.ttf")
    font_option = ""
    if font.is_file():
        shutil.copyfile(font, folder / "review-font.ttf")
        font_option = "fontfile=review-font.ttf:"
    filters = ["pad=iw:ih+96:0:96:color=0x17202a"]
    for i, label in enumerate(labels):
        text_path = folder / f"review-label-{i}.txt"
        text_path.write_text(textwrap.fill(label, max(22, int(width / len(labels) / 9))),
                             encoding="utf-8")
        filters.append(f"drawtext={font_option}textfile={text_path.name}:expansion=none:"
                       f"fontsize=17:fontcolor=white:x={i * width // len(labels) + 8}:y=8")
    filters.append(f"drawtext={font_option}" + "text='Elapsed %{pts\\:hms}':fontsize=16:"
                   "fontcolor=white:x=8:y=74")
    # The partial output protects a previous completed bundle if encoding fails.
    command = [ffmpeg, "-hide_banner", "-y", "-framerate", "30", "-i", "frame-%04d.png",
               "-frames:v", str(len(frames)), "-vf", ",".join(filters), "-an",
               "-c:v", "libx264", "-preset", "fast", "-crf", "19", "-pix_fmt", "yuv420p",
               "-movflags", "+faststart", "review.partial.mp4"]
    with (folder / "package-ffmpeg.log").open("w", encoding="utf-8") as log:
        log.write(json.dumps(command) + "\n")
        log.flush()
        subprocess.run(command, cwd=folder, stdout=log, stderr=subprocess.STDOUT, check=True)
    result = subprocess.run([ffprobe, "-v", "error", "-count_frames", "-select_streams", "v:0",
                             "-show_entries", "stream=nb_read_frames,width,height,r_frame_rate,duration",
                             "-of", "json", "review.partial.mp4"], cwd=folder,
                            capture_output=True, text=True, check=True)
    probe = json.loads(result.stdout)
    if int(probe["streams"][0]["nb_read_frames"]) != len(frames):
        raise ValueError(f"Encoded frame count mismatch: {folder}")
    (folder / "review.partial.mp4").replace(folder / "review.mp4")
    if font_option:
        (folder / "review-font.ttf").unlink()
    return {"name": folder.name, "metadata": metadata_path.name, "labels": labels,
            "frames": len(frames), "seconds": len(frames) / 30, "probe": probe,
            "limits": [metadata[key] for key in ("convention", "scope", "camera",
                       "rootMotionConvention", "RootMotionConvention", "sourceSwitches", "sourceComparisonLimits",
                       "sourceLimits", "sourceConvention", "chestSourceConvention", "contactConvention", "diagnosticOverrides")
                       if metadata.get(key)]}


def write_index(root, entries, skipped):
    cards = []
    names = {entry["name"] for entry in entries}
    pairs = [
        ("Running jump", "run-jump-before", "run-jump-final" if "run-jump-final" in names else "run-jump-after"),
        ("Deer", "deer-reviewed", "deer-final"),
        ("Chest", "chest-complete", "chest-final"),
        ("Ladder", "ladder-roundtrip", "ladder-complete-short"),
        ("Step up", "step-up-reviewed", "step-up-contact-fit"),
        ("Goat drink", "goat-continuity", "goat-drink-final"),
        ("Running jump handoff", "run-jump-final", "run-jump-phase-matched"),
        ("Running jump with sustained run", "run-jump-through-before", "run-jump-through-after"),
        ("Chest search", "chest-final", "chest-search-candidate"),
        ("Ladder ground mount", "ladder-complete-short", "ladder-bottom-authored"),
        ("Focused goat drink", "goat-drink-focused-before", "goat-drink-focused-after"),
    ]
    before_names = {before for _, before, after in pairs if before in names and after in names}
    navigation = '<nav aria-label="Capture navigation">'
    for title, selected in [("After and additional captures", names - before_names), ("Before captures", names & before_names)]:
        if selected:
            navigation += f'<p><strong>{title}:</strong> ' + ' · '.join(
                f'<a href="#{quote(name)}">{html.escape(name)}</a>' for name in sorted(selected)) + '</p>'
    navigation += '</nav>'
    comparisons = ''.join(f'''<div class="sync" data-before="{html.escape(before)}" data-after="{html.escape(after)}">
<h2>{html.escape(title)}: synchronized before / after</h2>
<p>Sync aligns elapsed capture time, not action events. Source A and recipes can differ; check metadata before comparing.</p>
<div class="pair"></div></div>''' for title, before, after in pairs if before in names and after in names)
    for entry in entries:
        name = entry["name"]
        url = quote(name)
        limits = " ".join(entry["limits"])
        cards.append(f'''<section id="{html.escape(name)}"><h2>{html.escape(name)}</h2>
<p>{html.escape(' | '.join(entry['labels']))}</p>
<video controls preload="metadata" src="{url}/review.mp4"></video>
<div class="controls"><button data-step="-1">Previous frame</button><button data-step="1">Next frame</button>
<label>Speed <select><option value="0.25">0.25x</option><option value="0.5">0.5x</option>
<option value="1" selected>1x</option><option value="2">2x</option></select></label><output>0.000 s</output></div>
<p>{entry['frames']} frames · {entry['seconds']:.3f} seconds · 30 fps ·
<a href="{url}/{quote(entry['metadata'])}">Capture metadata</a> · <a href="{url}/package-ffmpeg.log">Encoding log</a></p>
<p>{html.escape(limits)}</p></section>''')
    page = '''<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>Animation review bundle</title>
<style>body{font:16px system-ui;max-width:1450px;margin:24px auto;padding:0 18px;background:#101820;color:#eef2f5}
a{color:#9cdaff}section{border-top:1px solid #53616e;margin-top:30px;padding-top:12px}video{width:100%;background:black}
button,select{font:inherit;padding:7px;margin:4px}output{margin:10px}.sync{background:#263748;padding:16px}
p{line-height:1.5}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>Animation review bundle</h1>
<p>These captures document a limited sample. They do not certify animation polish or close the continuity audit.
Functional tests do not establish visual quality. Visual approval remains pending.</p>
<p>A is retargeted where labeled. Root-aligned views do not prove authored world travel or support-relative contact.
Diagnostic source switches do not validate production transitions. The original-source candidate comparison is NOT A/B/C.
Read each capture's metadata for timing, rig, and coverage differences. Folder names are capture identifiers, not verdicts.</p>
<p>Frame controls seek at 1/30 second. Browser seeks and synchronized playback are approximate; original PNG frames remain authoritative.
Use metadata timestamps for precise events. The elapsed overlay measures encoded capture time, not a source clip's local time.</p>
''' + ('<p><a href="review-revision.md">Revision findings and remaining limits</a> · <a href="index-first-review.html">Earlier broad review bundle</a></p>'
       if (root / "review-revision.md").is_file() and (root / "index-first-review.html").is_file() else '') + navigation + comparisons + "\n".join(cards) + '''
<script>
for(const section of document.querySelectorAll('section')){
 const v=section.querySelector('video');
 for(const b of section.querySelectorAll('[data-step]'))b.onclick=()=>{v.pause();v.currentTime=Math.max(0,Math.min(v.duration||0,v.currentTime+Number(b.dataset.step)/30));};
 section.querySelector('select').onchange=e=>v.playbackRate=Number(e.target.value);
 v.ontimeupdate=()=>section.querySelector('output').value=v.currentTime.toFixed(3)+' s';
}
for(const group of document.querySelectorAll('.sync')){
const pair=[];
for(const name of [group.dataset.before,group.dataset.after]){
 const source=document.getElementById(name)?.querySelector('video');
 if(source){const box=document.createElement('div');const title=document.createElement('p');title.textContent=name;box.append(title);
 const v=document.createElement('video');v.src=source.getAttribute('src');v.preload='metadata';v.controls=false;v.setAttribute('aria-label',name);box.append(v);group.querySelector('.pair').append(box);pair.push(v);}
}
function pausePair(){pair.forEach(v=>v.pause());}
function seekPair(t){pausePair();pair.forEach(v=>v.currentTime=Math.max(0,Math.min(v.duration||0,t)));}
const actions=[['Play both',()=>{if(pair.length===2){pair[1].currentTime=pair[0].currentTime;pair.forEach(v=>v.play().catch(()=>pausePair()));}}],
 ['Pause both',pausePair],['Previous frame',()=>seekPair((pair[0]?.currentTime||0)-1/30)],['Next frame',()=>seekPair((pair[0]?.currentTime||0)+1/30)]];
for(const [text,action] of actions){const b=document.createElement('button');b.textContent=text;b.onclick=action;group.append(b);}
const label=document.createElement('label');label.textContent='Both speeds ';const speed=document.createElement('select');
for(const rate of [0.25,0.5,1]){const option=document.createElement('option');option.value=rate;option.textContent=rate+'x';option.selected=rate===1;speed.append(option);}
speed.onchange=e=>pair.forEach(v=>v.playbackRate=Number(e.target.value));label.append(speed);group.append(label);
if(pair.length===2){pair[0].addEventListener('timeupdate',()=>{if(!pair[0].paused&&Math.abs(pair[0].currentTime-pair[1].currentTime)>0.07)pair[1].currentTime=pair[0].currentTime;});pair.forEach(v=>v.addEventListener('ended',pausePair));}
}
</script></html>'''
    destination = "diagnostics.html" if (root / "review-queue.json").is_file() else "index.html"
    (root / destination).write_text(page, encoding="utf-8")
    (root / "package-summary.json").write_text(json.dumps({"captures": entries, "skipped": skipped}, indent=2), encoding="utf-8")


def write_focused_review(root, ffmpeg, ffprobe):
    queue = json.loads((root / "review-queue.json").read_text(encoding="utf-8-sig"))
    if not isinstance(queue, list) or not 1 <= len(queue) <= 3:
        raise ValueError("A review round requires one to three actions.")
    ids = set()
    encoded = {}
    for item in queue:
        if any(not isinstance(item.get(key), str) or not item[key].strip() for key in ("id", "title", "status", "question")):
            raise ValueError("Every review action requires an ID, title, status, and review question.")
        scenarios = item.get("scenarios", [{"id": "complete", "title": "Complete action", "versions": item.get("versions")}])
        if not isinstance(scenarios, list) or not scenarios:
            raise ValueError("Each action requires at least one situation.")
        scenario_ids = set()
        versions = []
        for scenario in scenarios:
            if any(not isinstance(scenario.get(key), str) or not scenario[key].strip() for key in ("id", "title")):
                raise ValueError("Each situation requires an ID and title.")
            if scenario["id"] in scenario_ids:
                raise ValueError("Situation IDs must be unique within an action.")
            scenario_ids.add(scenario["id"])
            if not isinstance(scenario.get("versions"), list) or not 1 <= len(scenario["versions"]) <= 2:
                raise ValueError("Each situation requires a current version and at most one previous version.")
            versions.extend(scenario["versions"])
        if item["id"] in ids:
            raise ValueError("Review action IDs must be unique.")
        ids.add(item["id"])
        for version in versions:
            if any(not isinstance(version.get(key), str) or not version[key].strip() for key in ("capture", "label", "note")):
                raise ValueError("Every version requires a capture, label, and explanatory note.")
            folder = root / version["capture"]
            if folder.resolve().parent != root:
                raise ValueError("Review captures must be direct child folders.")
            if version["capture"] not in encoded:
                _, metadata, frames, width, height = capture_data(folder)
                columns = metadata.get("columns", [])
                if len(columns) != 3 or not columns[2].startswith("C:") or width % 3 or height % 2:
                    raise ValueError(f"Expected an explicitly labeled three-stage, two-angle capture: {folder}")
                for angle, row in (("side", 1), ("rear", 0)):
                    font = Path("C:/Windows/Fonts/arial.ttf")
                    if not font.is_file():
                        raise ValueError("Arial is required for visible review frame numbers.")
                    shutil.copyfile(font, folder / "review-font.ttf")
                    filters = (f"crop={width // 3}:{height // 2}:{2 * width // 3}:{row * height // 2},"
                               "pad=iw:ih+44:0:44:color=0x17202a,"
                               "drawtext=fontfile=review-font.ttf:fontsize=18:fontcolor=white:x=10:y=12:"
                               "text='Frame %{n} | %{pts\\:hms}'")
                    partial = f"runtime-{angle}.partial.mp4"
                    command = [ffmpeg, "-hide_banner", "-y", "-framerate", "30", "-i", "frame-%04d.png",
                               "-frames:v", str(len(frames)), "-vf", filters, "-an", "-c:v", "libx264",
                               "-preset", "fast", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", partial]
                    with (folder / f"runtime-{angle}-encode.log").open("w", encoding="utf-8") as log:
                        subprocess.run(command, cwd=folder, stdout=log, stderr=subprocess.STDOUT, check=True)
                    probe = subprocess.run([ffprobe, "-v", "error", "-count_frames", "-select_streams", "v:0",
                                            "-show_entries", "stream=nb_read_frames", "-of", "json", partial],
                                           cwd=folder, capture_output=True, text=True, check=True)
                    if int(json.loads(probe.stdout)["streams"][0]["nb_read_frames"]) != len(frames):
                        raise ValueError(f"Runtime frame count mismatch: {folder}")
                    (folder / partial).replace(folder / f"runtime-{angle}.mp4")
                    (folder / "review-font.ttf").unlink()
                encoded[version["capture"]] = len(frames)
                print(f"Runtime views: {folder.name}, {len(frames)} frames each", flush=True)
            version["frames"] = encoded[version["capture"]]
    template = Path(__file__).with_name("focused_review.html").read_text(encoding="utf-8")
    data = json.dumps(queue, ensure_ascii=True).replace("<", "\\u003c")
    (root / "index.html").write_text(template.replace("__REVIEW_QUEUE__", data), encoding="utf-8")
    (root / "focused-package-summary.json").write_text(json.dumps(encoded, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("folders", nargs="*", help="Direct child capture folders; omit to rebuild review-queue.json only")
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    ffmpeg, ffprobe = shutil.which("ffmpeg"), shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        parser.error("ffmpeg and ffprobe must be on PATH")
    focused = (root / "review-queue.json").is_file()
    if not args.folders and not focused:
        parser.error("Select capture folders or provide review-queue.json.")
    entries, skipped = [], []
    for name in dict.fromkeys(args.folders):
        folder = root / name
        if folder.resolve().parent != root or name in (".", ".."):
            parser.error(f"Not a direct child folder: {name}")
        if not folder.is_dir():
            skipped.append(name)
            continue
        entries.append(encode(folder, ffmpeg, ffprobe))
        print(f"Packaged {name}: {entries[-1]['frames']} frames", flush=True)
    if entries:
        write_index(root, entries, skipped)
    if focused:
        write_focused_review(root, ffmpeg, ffprobe)
    print(root / "index.html")


if __name__ == "__main__":
    main()
