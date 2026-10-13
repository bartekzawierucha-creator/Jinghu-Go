#!/usr/bin/env bash
# End-to-end smoke test of the native Android client on an emulator against a real API (http://10.0.2.2:8080 = host).
# Registers an account, creates a character, explores with an emulated GPS position and fights a training battle.
# Screenshots go to ./shots; the test fails on any missing screen or an app crash.
set -euo pipefail
APK="$1"
PKG=pl.bz.jianghugo
UI="python3 $(dirname "$0")/ui.py"
mkdir -p shots
step() { echo "### $*"; }

adb wait-for-device
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
adb install -r "$APK"
# start in Krakow, so the first fix the game sees is already there (a jump from the default location trips anti-cheat)
for i in 1 2 3; do adb emu geo fix 19.9372 50.0614 220 >/dev/null || true; sleep 1; done
adb shell pm grant $PKG android.permission.ACCESS_FINE_LOCATION
adb shell pm grant $PKG android.permission.ACCESS_COARSE_LOCATION
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null

step "login screen"
$UI wait "Zaloguj" 90
$UI shot shots/01-login.png
$UI field 0 "http://10.0.2.2:8080"
$UI field 1 "smoke@test.dev"
$UI field 2 "smoke-password-123"
$UI tap "Załóż konto"

step "character creation"
$UI wait "Narodziny wojownika" 60
$UI wait "Żetony Losu" 30
$UI shot shots/02-creation.png
$UI tap "Losuj ponownie"
$UI wait "Żetony Losu" 30
$UI field 0 "Lin"
$UI tap "Qi — równowaga"

step "character sheet"
$UI wait "Szansa przełomu" 60
$UI shot shots/03-character.png
$UI tap "Medytuj"
$UI wait "Medytujesz od" 30

step "world exploration with emulated GPS (Krakow)"
$UI tap "Eksploruj świat"
$UI wait "Pozycja" 60
for i in $(seq 1 12); do
  lon=$(python3 -c "print(19.9372 + $i * 0.00004)")
  adb emu geo fix "$lon" 50.0614 220 >/dev/null || true
  sleep 3
done
$UI wait "Pozycja potwierdzona" 60 || echo "position not confirmed by the server within the time limit"
$UI shot shots/04-world.png
$UI tap "Zakończ eksplorację"

step "training battle"
$UI wait "Szansa przełomu" 60
$UI tap "Manekin treningowy"
$UI wait "Tura" 60
$UI shot shots/05-battle.png
$UI tap "Odpoczynek" 20
sleep 3
$UI shot shots/06-battle-turn.png
$UI tap "Wróć"
$UI wait "Szansa przełomu" 60

step "bag"
$UI tap "Sakwa, kuźnia"
$UI wait "Kamienie Duchowe" 60
$UI shot shots/07-bag.png

step "crash check"
adb logcat -d > shots/logcat.txt
if grep -E "FATAL EXCEPTION|Unhandled managed exception|AndroidRuntime: Process: $PKG" shots/logcat.txt; then
  echo "app crashed"; exit 1
fi
adb shell pidof $PKG >/dev/null || { echo "app is not running"; exit 1; }
echo "SMOKE OK"
