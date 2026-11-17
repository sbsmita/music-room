#!/bin/zsh
# Headless Unity pipeline for VR Music Room (macOS). Usage: Tools/unity.sh <command> [args]
#   compile | test-edit | test-play [--gpu] | test-perf | configure | render-setup | tmp-import | scene | bake | report
#   | meta-report | build [apk] | install [apk] | log | all
# Every call uses -batchmode -nographics -buildTarget Android -projectPath $PROJ -logFile $LOGS/<cmd>.log.
# Environment overrides: UNITY, PROJ, LOGS, ADB. Only ONE editor may open a project folder at a time (Temp/UnityLockfile).
set -u
UNITY=${UNITY:-/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity}
PROJ=${PROJ:-${0:A:h:h}}
LOGS=${LOGS:-$PROJ/Logs/ci}
ADB=${ADB:-/Applications/Unity/Hub/Editor/6000.3.27f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}
PKG=com.vrmusicroom.app
mkdir -p "$LOGS"

# run <name> <extra unity args...>; NOGFX=0 to keep the GPU (graphics tests).
run() {
  local name=$1; shift
  local log="$LOGS/$name.log"
  local gfx=(-nographics); [[ ${NOGFX:-1} == 0 ]] && gfx=()
  local t0=$SECONDS
  "$UNITY" -batchmode $gfx -buildTarget Android -projectPath "$PROJ" -logFile "$log" "$@"
  local rc=$?
  # Compiler diagnostics in our own code (each line is printed 3x by Unity, hence sort -u).
  grep -E '^[^ ].*\.cs\([0-9]+,[0-9]+\): error CS[0-9]+:' "$log" | sort -u
  grep -E '^Assets/MusicRoom/.*\.cs\([0-9]+,[0-9]+\): warning CS[0-9]+:' "$log" | sort -u | sed 's/^/[warn] /'
  # Known (6000.3.27f1, macOS): intermittent SIGABRT (134, "libc++abi: terminate_handler unexpectedly
  # returned") during shutdown AFTER the work finished. Trust the success marker instead.
  local mark=${OK_MARK:-Exiting batchmode successfully now!}
  if [[ $rc -eq 134 ]] && grep -qE "$mark" "$log"; then
    echo "[$name] note: exit 134 during shutdown after success marker, treating as 0"; rc=0
  fi
  echo "[$name] exit=$rc in $((SECONDS - t0))s log=$log"
  return $rc
}

# check_xml <file>: fail when no tests ran or any failed (exit code alone treats "0 tests" as success).
check_xml() {
  python3 -I - "$1" <<'EOF'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
t, f, i = int(r.get('total')), int(r.get('failed')), int(r.get('inconclusive'))
print(f"[tests] total={t} passed={r.get('passed')} failed={f} inconclusive={i} skipped={r.get('skipped')}")
for tc in r.iter('test-case'):
    if tc.get('result') in ('Failed', 'Inconclusive'):
        m = tc.find('./failure/message')
        m = m if m is not None else tc.find('./reason/message')
        print('  FAIL', tc.get('fullname'), ((m.text or '') if m is not None else '').strip().splitlines()[:3])
sys.exit(0 if t > 0 and f == 0 and i == 0 else 1)
EOF
}

cmd=${1:-help}; shift 2>/dev/null
# Optional: DEV=1 makes build a development player (the [Audio] dsp load line once a second in the log; no HUD).
# Optional: TEST_FILTER=<regex> limits test-edit / test-play / test-perf to matching tests (Unity -testFilter). Unset: all.
flt=(); [[ -n ${TEST_FILTER:-} ]] && flt=(-testFilter "$TEST_FILTER")
case $cmd in
  compile)      run compile -quit ;;
  # Results files are deleted first: a run that aborts (compile error) must not be judged on the previous run's XML/JSON.
  test-edit)    rm -f "$LOGS/editmode.xml"
                OK_MARK='Exiting with code 0' run test-edit -runTests -testPlatform EditMode $flt -testResults "$LOGS/editmode.xml"; rc=$?
                [[ -f "$LOGS/editmode.xml" ]] && check_xml "$LOGS/editmode.xml" || rc=1; exit $rc ;;
  # Category "Graphics" needs a GPU (--gpu); category "Performance" (audio load benchmarks, several seconds) runs only in test-perf.
  test-play)    export OK_MARK='Exiting with code 0'; rm -f "$LOGS/playmode.xml"
                if [[ ${1:-} == --gpu ]]; then NOGFX=0 run test-play -runTests -testPlatform PlayMode -testCategory '!Performance' $flt -testResults "$LOGS/playmode.xml"; rc=$?
                else run test-play -runTests -testPlatform PlayMode -testCategory '!Graphics;!Performance' $flt -testResults "$LOGS/playmode.xml"; rc=$?; fi
                [[ -f "$LOGS/playmode.xml" ]] && check_xml "$LOGS/playmode.xml" || rc=1; exit $rc ;;
  test-perf)    export OK_MARK='Exiting with code 0'; rm -f "$LOGS/perf.xml"
                run test-perf -runTests -testPlatform PlayMode -testCategory 'Performance' $flt -testResults "$LOGS/perf.xml"; rc=$?
                grep -E '\[PERF\]' "$LOGS/test-perf.log" | sort -u
                [[ -f "$LOGS/perf.xml" ]] && check_xml "$LOGS/perf.xml" || rc=1; exit $rc ;;
  configure)    run configure -stackTraceLogType None -executeMethod MusicRoom.EditorTools.QuestSetup.ConfigureAll -quit
                grep -E '\[SETUP\]|\[AUDIO-SETUP\]|\[BURST-SETUP\]|\[OPENXR-VALIDATION\]' "$LOGS/configure.log" ;;
  render-setup) run render-setup -stackTraceLogType None -executeMethod MusicRoom.EditorTools.RenderSetup.SetupAll -quit
                grep -E '\[RENDER\]|\[MATERIALS\]' "$LOGS/render-setup.log" ;;
  tmp-import)   OK_MARK='importPackageCompleted|TMP already imported' run tmp-import -executeMethod MusicRoom.EditorTools.TextSetup.ImportTmpEssentialsAndExit ;;  # no -quit
  scene)        run scene -stackTraceLogType None -executeMethod MusicRoom.EditorTools.SceneBuilder.BuildMain -quit
                grep -E '\[SCENE\]' "$LOGS/scene.log" ;;
  bake)         run bake -stackTraceLogType None -executeMethod MusicRoom.EditorTools.TextureBaker.BakeAll -quit
                grep -E '\[BAKE\]' "$LOGS/bake.log" ;;
  report)       run report -stackTraceLogType None -executeMethod MusicRoom.EditorTools.QuestSetup.Report -quit; grep -E '\[(REPORT|OPENXR-VALIDATION)\]' "$LOGS/report.log" ;;
  # meta-report runs WITHOUT -quit: QuestSetup.MetaReport waits for Meta's Package Manager list (else the whole Packages
  # group of rules, several Required, drops out of the report), then exits itself. The check fails without that group.
  meta-report)  rm -f "$LOGS/meta-setup.json"
                OK_MARK='\[META-REPORT\] done exit=0' run meta-report -executeMethod MusicRoom.EditorTools.QuestSetup.MetaReport -reportFile "$LOGS/meta-setup.json"; rc=$?
                grep -E '\[META-REPORT\]' "$LOGS/meta-report.log" | sort -u
                [[ -f "$LOGS/meta-setup.json" ]] || { echo "[meta] no report"; exit 1; }
                python3 -I -c "import json,sys,collections; d=json.load(open('$LOGS/meta-setup.json')); ts=d['tasksStatus']; o=[t for t in ts if not t['isDone']]; g=collections.Counter(t['group'] for t in ts); print('[meta] tasks', len(ts), 'outstanding', len(o), 'groups', dict(g)); [print(' ', t['level'],'|',t['group'],'|',t['message'][:120]) for t in o]; ok=g.get('Packages',0)>0 and not any(t['level']=='Required' for t in o); print('[meta] Packages group missing: report made before the package list was ready') if g.get('Packages',0)==0 else None; sys.exit(0 if ok else 1)" || rc=1
                exit $rc ;;
  build)        apk=${1:-Builds/Android/VRMusicRoom.apk}
                OK_MARK='\[BUILD\] result=Succeeded' run build -stackTraceLogType None -executeMethod MusicRoom.EditorTools.BuildPlayer.BuildApk -outputPath "$apk" ${DEV:+-development}; rc=$?
                grep -E '\[BUILD(-ERROR)?\]' "$LOGS/build.log"; [[ -f "$PROJ/$apk" || -f "$apk" ]] || rc=1; exit $rc ;;
  install)      apk=${1:-$PROJ/Builds/Android/VRMusicRoom.apk}
                "$ADB" install -r -g "$apk" && "$ADB" shell am start -n $PKG/com.unity3d.player.UnityPlayerGameActivity ;;
  log)          "$ADB" logcat -c; "$ADB" logcat -v time -s Unity:V AndroidRuntime:E CRASH:E DEBUG:E libc:F ;;
  all)          for c in compile configure tmp-import render-setup scene test-edit test-play meta-report; do "$0" $c || exit $?; done ;;
  *)            sed -n 2,4p "$0"; exit 2 ;;
esac
