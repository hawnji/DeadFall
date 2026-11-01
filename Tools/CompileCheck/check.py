#!/usr/bin/env python3
"""
Compile-checks every Deadfall assembly definition against only its declared references (so assembly
boundaries are enforced), then builds and runs the EditMode Rules tests with NUnitLite.
Uses Unity 2021.3 reference assemblies plus small stubs for packages, so it catches most errors but is
NOT a substitute for compiling in the Unity 6 editor. Run setup.sh first.
"""
import glob, json, os, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
P = os.path.join(ROOT, "Assets", "_Project")
C = os.path.join(HERE, ".cache")
B = os.path.join(C, "build"); os.makedirs(B, exist_ok=True)
FW = "/usr/lib/mono/4.7.2-api"
CSC = ["mono", f"{C}/microsoft.net.compilers.toolset/tasks/net472/csc.exe", "-nologo", "-langversion:9", "-nullable:disable",
       "-warn:4", "-nowarn:1591,0649", "-nostdlib", f"-r:{FW}/mscorlib.dll", f"-r:{FW}/Facades/netstandard.dll",
       f"-r:{FW}/System.dll", f"-r:{FW}/System.Core.dll"]
UNITY = [f"-r:{f}" for f in sorted(glob.glob(f"{C}/unityengine.modules/lib/net35/*.dll"))]
NUNIT = [f"-r:{C}/nunit/lib/net45/nunit.framework.dll"]
STUBS = {"Unity.InputSystem": glob.glob(f"{HERE}/stubs/InputSystemStubs.cs")}

def run(args):
    r = subprocess.run(args, capture_output=True, text=True)
    return r.returncode, (r.stdout + r.stderr).strip()

stub_dlls = {}
for name, srcs in STUBS.items():
    out = f"{B}/{name}.dll"
    code, log = run(CSC + ["-t:library", f"-out:{out}"] + UNITY + srcs)
    if code: print(log); sys.exit(1)
    stub_dlls[name] = out

asms = {}
for a in glob.glob(f"{P}/**/*.asmdef", recursive=True):
    j = json.load(open(a)); d = os.path.dirname(a)
    nested = [os.path.dirname(x) for x in glob.glob(f"{d}/**/*.asmdef", recursive=True) if os.path.dirname(x) != d]
    srcs = [s for s in glob.glob(f"{d}/**/*.cs", recursive=True) if not any(s.startswith(n + os.sep) for n in nested)]
    asms[j["name"]] = (j, srcs)

built, failed = {}, False
def build(name):
    global failed
    if name in built: return built[name]
    j, srcs = asms[name]
    refs = []
    for r in j["references"]:
        if r in asms:
            o = build(r)
            if o: refs.append(f"-r:{o}")
        elif r in stub_dlls: refs.append(f"-r:{stub_dlls[r]}")
    if not srcs: built[name] = None; return None
    out = f"{B}/{name}.dll"
    extra = [] if j.get("noEngineReferences") else UNITY
    if name.startswith("Deadfall.Tests"): extra = extra + NUNIT
    code, log = run(CSC + ["-t:library", f"-out:{out}"] + extra + refs + srcs)
    print(f"{'ok    ' if code == 0 else 'FAILED'} {name} ({len(srcs)} files)")
    if log: print("   " + log.replace("\n", "\n   "))
    if code: failed = True; built[name] = None; return None
    built[name] = out; return out

for n in sorted(asms):
    if not n.startswith("Deadfall.Tests"): build(n)
# Test assemblies reference UnityEngine.TestRunner which isn't available here; compile them with NUnit only.
for n in sorted(asms):
    if n.startswith("Deadfall.Tests"): build(n)
if failed: sys.exit(1)

# Run engine-free EditMode tests (Rules) with NUnitLite.
rules = [built[n] for n in asms if n.startswith("Deadfall.Rules") and built.get(n)]
main = os.path.join(B, "Main.cs")
open(main, "w").write("public static class TestMain { public static int Main(string[] a) => "
                      "new NUnitLite.AutoRun(System.Reflection.Assembly.LoadFrom(a[0])).Execute(new[]{\"--noresult\",\"--labels=Off\"}); }")
runner = os.path.join(B, "RunTests.exe")
code, log = run(CSC + ["-t:exe", f"-out:{runner}", f"-r:{C}/nunitlite/lib/net45/nunitlite.dll"] + NUNIT + [main])
if code: print(log); sys.exit(1)
import shutil
for dll in [f"{C}/nunit/lib/net45/nunit.framework.dll", f"{C}/nunitlite/lib/net45/nunitlite.dll"]:
    if not os.path.exists(os.path.join(B, os.path.basename(dll))): shutil.copy(dll, B)
edit = built.get("Deadfall.Tests.EditMode")
# EditMode tests also reference Deadfall.Core (UnityEngine), so only Rules-only test classes can run here.
code, log = run(["mono", runner, edit])
print("\n".join(l for l in log.splitlines() if "Test Count" in l or "Failed" in l or l.strip().startswith(tuple("0123456789"))))
sys.exit(code)
