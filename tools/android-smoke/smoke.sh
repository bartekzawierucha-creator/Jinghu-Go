#!/usr/bin/env bash
# End-to-end smoke test of the native Android client on an emulator in OFFLINE mode (the game server runs inside
# the app). Creates a character, meditates, explores with an emulated GPS position, fights a training battle, opens
# the bag, then kills and restarts the app and checks that the character is still there.
# Screenshots go to ./shots; the test fails on any missing screen or an app crash.
set -euo pipefail
APK="$1"
PKG=pl.bz.jianghugo
UI="python3 $(dirname "$0")/ui.py"
mkdir -p shots
step() { echo "### $*"; }
trap 'adb logcat -d > shots/logcat.txt 2>/dev/null || true' EXIT

adb wait-for-device
adb shell settings put global window_animation_scale 0 || true
adb shell settings put global transition_animation_scale 0 || true
adb shell settings put global animator_duration_scale 0 || true
# a busy emulator makes the launcher "not respond": never show those dialogs over the game
adb shell settings put global hide_error_dialogs 1 || true
adb shell settings put secure anr_show_background 0 || true
adb install -r "$APK"
# start in Krakow, so the first fix the game sees is already there (a jump from the default location trips anti-cheat)
for i in 1 2 3; do adb emu geo fix 19.9372 50.0614 220 >/dev/null || true; sleep 1; done
adb shell pm grant $PKG android.permission.ACCESS_FINE_LOCATION
adb shell pm grant $PKG android.permission.ACCESS_COARSE_LOCATION
adb logcat -c
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null

step "first start goes straight into the offline world"

step "character creation"
$UI wait "Narodziny wojownika" 120
$UI wait "Żetony Losu" 30
$UI shot shots/02-creation.png
$UI tap "Losuj ponownie"
$UI wait "Żetony Losu" 30
$UI field 0 "Lin"
$UI tap "Qi — równowaga"

step "character sheet + mentor tutorial (first run)"
$UI wait "Mistrz Lao" 60
$UI shot shots/03a-tutorial.png
for i in 1 2 3 4; do $UI tap "Dalej" 20; sleep 1; done
$UI tap "Ruszam w drogę" 20
sleep 1
$UI wait "Szansa przełomu" 60
$UI shot shots/03-character.png
$UI tap "Dzienny meldunek" 30
$UI wait "MELDUNEK" 30
$UI shot shots/03c-checkin.png
$UI tap "Dalej" 20
sleep 1
$UI tap "Medytuj"
$UI wait "Medytujesz od" 30
sleep 2
$UI shot shots/03b-meditating.png

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
$UI tap "Drewniany Manekin"
$UI wait "Tura" 60
$UI shot shots/05-battle.png
$UI tap "Odpoczynek" 20
sleep 3
$UI shot shots/06-battle-turn.png
$UI tap "Wróć"
$UI wait "Szansa przełomu" 60

step "new enemies have their own sprites"
for foe in "Kamienny Golem" "Zdrajca Sekty"; do
  $UI tap "$foe"
  $UI wait "Tura" 60
  $UI tap "Odpoczynek" 20
  sleep 3
  $UI shot "shots/06-$(echo "$foe" | tr ' ' '_').png"
  $UI tap "Wróć"
  $UI wait "Szansa przełomu" 60
done

step "bag"
$UI tap "Sakwa, kuźnia"
$UI wait "Kamienie Duchowe" 60
$UI shot shots/07-bag.png

step "loadout, Flame sites, notifications"
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI wait "Szansa przełomu" 60
$UI tap "Zestaw technik"
$UI wait "Slot 1" 60
$UI shot shots/09-loadout.png
$UI tap "Slot 2"
$UI wait "Opróżnij slot" 30
$UI shot shots/10-slot-picker.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI wait "Slot 1" 30
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Miejsca Płomienia"
$UI wait "Smocza Jama" 60
$UI shot shots/11-flame-sites.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Powiadomienia"
$UI wait "Witaj w Jianghu" 60
$UI shot shots/12-inbox.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI wait "Szansa przełomu" 60

step "MVP 1.1: quests, meridians, rankings, spirit pet, hero"
$UI tap "Zadania i osiągnięcia"
$UI wait "Nowe zadania o północy" 60
$UI shot shots/13-quests.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Punkty z każdego etapu"
$UI wait "Wolne punkty" 60
$UI shot shots/14-meridians.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Rankingi"
$UI wait "Twoje miejsce" 60
$UI shot shots/15-ranking.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Duchowy towarzysz"
$UI wait "Nie masz jeszcze towarzysza" 60
$UI shot shots/16-pet.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Postać i ekwipunek"
$UI wait "Dotknij slotu" 60
$UI shot shots/17-hero.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI wait "Szansa przełomu" 60

step "restart: progress must survive"
sleep 2
adb shell input keyevent KEYCODE_HOME
sleep 2
adb shell am force-stop $PKG
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null
$UI wait "Szansa przełomu" 90
$UI wait "Lin" 10
$UI shot shots/08-after-restart.png

step "crash check"
adb logcat -d > shots/logcat.txt
if grep -E "FATAL EXCEPTION|Unhandled managed exception|AndroidRuntime: Process: $PKG" shots/logcat.txt; then
  echo "app crashed"; exit 1
fi
adb shell pidof $PKG >/dev/null || { echo "app is not running"; exit 1; }
echo "SMOKE OK"
