"""Review, compile, and apply the weather candidate without importing it into Unity."""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def candidates():
    return sorted(p for p in (HERE / "candidate").rglob("*") if p.is_file())


def check():
    conflicts = []
    for candidate in candidates():
        relative = candidate.relative_to(HERE / "candidate")
        baseline = HERE / "baseline" / relative
        live = ROOT / relative
        if (baseline.exists() and (not live.exists() or live.read_bytes() != baseline.read_bytes())) \
                or (not baseline.exists() and live.exists()):
            conflicts.append(str(relative))
    if conflicts:
        raise RuntimeError("Live sources changed; merge before applying:\n" + "\n".join(conflicts))
    print(f"Baseline and new-file checks passed for {len(candidates())} candidate files.")


def review():
    patches = []
    for candidate in candidates():
        relative = candidate.relative_to(HERE / "candidate")
        if candidate.suffix == ".wav":
            continue
        baseline = HERE / "baseline" / relative
        before = baseline.read_text(encoding="utf-8-sig") if baseline.exists() else ""
        after = candidate.read_text(encoding="utf-8-sig")
        patches.extend(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
            fromfile="a/" + relative.as_posix() if baseline.exists() else "/dev/null",
            tofile="b/" + relative.as_posix()))
    output = HERE / "weather.patch"
    output.write_text("".join(patches), encoding="utf-8", newline="\n")
    print(output)


def build():
    """Make disposable build projects; never edit Unity's generated projects."""
    build_dir = HERE / "build"
    build_dir.mkdir(exist_ok=True)
    visited = set()

    def project(name):
        if name in visited:
            return
        visited.add(name)
        tree = ET.parse(ROOT / name)
        xml = tree.getroot()
        assembly = Path(name).stem
        definition = next((p for p in (ROOT / "Assets/Scripts").rglob("*.asmdef")
            if json.loads(p.read_text(encoding="utf-8-sig")).get("name") == assembly), None)
        if definition:
            # Unity has not refreshed the generated projects while the other task is running.
            # Reconstruct only these disposable projects from actual asmdef ownership.
            for group in xml.findall("ItemGroup"):
                for item in list(group.findall("Compile")):
                    group.remove(item)
            sources = ET.SubElement(xml, "ItemGroup")
            for path in definition.parent.rglob("*.cs"):
                owner = path.parent
                while not list(owner.glob("*.asmdef")) and owner != ROOT:
                    owner = owner.parent
                if owner == definition.parent:
                    ET.SubElement(sources, "Compile", Include=str(path.relative_to(ROOT)))
            for path in (HERE / "candidate" / definition.parent.relative_to(ROOT)).rglob("*.cs"):
                relative = path.relative_to(HERE / "candidate")
                if not (ROOT / relative).exists():
                    ET.SubElement(sources, "Compile", Include=str(relative))
        if name == "ProceduralPlanets.Tests.EditMode.csproj":
            ET.SubElement(ET.SubElement(xml, "ItemGroup"), "Compile",
                Include=str(HERE / "RainParticleRegressionTests.cs"))
        for node in xml.iter():
            if node.tag == "ProjectReference":
                dependency = Path(node.attrib["Include"].replace("\\", "/")).name
                project(dependency)
                node.set("Include", str(build_dir / dependency))
            elif node.tag in ("Compile", "Analyzer", "AdditionalFiles", "None") and "Include" in node.attrib:
                path = Path(node.attrib["Include"].replace("\\", "/"))
                if not path.is_absolute():
                    staged = HERE / "candidate" / path
                    node.set("Include", str(staged if staged.exists() else ROOT / path))
            elif node.tag == "HintPath" and node.text:
                path = Path(node.text.replace("\\", "/"))
                if not path.is_absolute():
                    node.text = str(ROOT / path)
            elif node.tag in ("OutputPath", "BaseIntermediateOutputPath", "IntermediateOutputPath"):
                leaf = "bin" if node.tag == "OutputPath" else "obj"
                node.text = str(build_dir / leaf / Path(name).stem) + "/"
        tree.write(build_dir / name, encoding="utf-8", xml_declaration=True)

    project("ProceduralPlanets.Tests.EditMode.csproj")
    for name in ("ProceduralPlanets.Core.csproj", "ProceduralPlanets.Planet.csproj",
                 "ProceduralPlanets.Tests.EditMode.csproj"):
        result = subprocess.run(["dotnet", "build", str(build_dir / name), "--nologo", "-v:minimal", "-m:1"],
            cwd=build_dir, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log = build_dir / (Path(name).stem + ".log")
        log.write_text(result.stdout, encoding="utf-8")
        print(result.stdout[-6000:])
        print(f"Full log: {log}")
        if result.returncode:
            raise SystemExit(result.returncode)


def apply():
    check()
    # Validate the complete batch before the first write. The baseline is also the backup.
    for candidate in candidates():
        relative = candidate.relative_to(HERE / "candidate")
        live = ROOT / relative
        baseline = HERE / "baseline" / relative
        before_hash = hashlib.sha256(live.read_bytes()).hexdigest() if live.exists() else None
        expected = hashlib.sha256(baseline.read_bytes()).hexdigest() if baseline.exists() else None
        if before_hash != expected:
            raise RuntimeError(f"Concurrent edit detected: {relative}")
        live.parent.mkdir(parents=True, exist_ok=True)
        live.write_bytes(candidate.read_bytes())
    print("Applied candidate. Unity import and runtime validation remain required.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("check", "review", "build", "apply"))
    args = parser.parse_args()
    {"check": check, "review": review, "build": build, "apply": apply}[args.action]()
