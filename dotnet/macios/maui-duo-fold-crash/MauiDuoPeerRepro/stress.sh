#!/bin/zsh
# Folds and unfolds an iPhone Duo simulator running the sample, relaunching after each crash.
# usage: ./stress.sh <simulator-udid> [cycles=15] [seconds per posture=2.5]
# Needs: the MAUI CLI (dotnet tool install --global Microsoft.Maui.Cli --prerelease) for `maui devflow device fold`.
# Extra switches go to the app as SIMCTL_CHILD_<NAME>=1 in the environment (see HomePage.cs).
U=$1; N=${2:-15}; S=${3:-2.5}; APP=com.example.duopeerrepro
[[ -z $U ]] && { echo "usage: $0 <udid> [cycles] [seconds]"; exit 1; }
fold(){ maui devflow device fold set $1 --udid $U --experimental >/dev/null 2>&1; }
pause(){ perl -e "select(undef,undef,undef,$1)"; }
mkdir -p logs; run=0; c=0; crashes=0
launch(){
  run=$((run+1)); LOG=logs/run-$run.log
  xcrun simctl terminate $U $APP >/dev/null 2>&1
  (perl -e 'alarm 1800; exec @ARGV' xcrun simctl launch --console-pty $U $APP > $LOG 2>&1 &)
  for i in {1..60}; do grep -q "\[REPRO\] attached" $LOG 2>/dev/null && break; pause 1; done; pause 3
}
fold folded; launch
while (( c < N )); do
  c=$((c+1)); fold unfolded; pause $S; fold folded; pause $S
  if grep -q "Terminating app\|Fatal error" $LOG; then
    crashes=$((crashes+1)); echo "cycle $c CRASH: $(grep -m1 -oE "reason: '.{0,150}" $LOG)"; pause 2; launch
  fi
done
pkill -f "simctl launch --console-pty $U" 2>/dev/null
echo "cycles=$c crashes=$crashes stale-peer warnings=$(cat logs/run-*.log | grep -c 'Tried to create a managed reference')"
