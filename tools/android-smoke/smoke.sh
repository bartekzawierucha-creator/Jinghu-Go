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
adb logcat -G 16M || true
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null

step "first start goes straight into the offline world"

step "character creation: fate, path, name"
$UI wait "Narodziny wojownika" 120
$UI wait "Żetony Losu" 30
$UI noscroll
$UI shot shots/02-creation.png
$UI tap "Losuj ponownie"
$UI wait "Żetony Losu" 30
$UI tap "Dalej ›"
$UI wait "ŚCIEŻKA" 30
$UI shot shots/02b-path.png
$UI tap "+25% Qi · regeneracja"
$UI wait "Rozpocznij drogę" 30
$UI field 0 "Lin"
$UI tap "Rozpocznij drogę"

step "cultivation hub + mentor tutorial (coach marks)"
$UI wait "Mistrz Lao" 90
$UI shot shots/03a-tutorial.png
for i in 1 2 3 4 5 6 7; do $UI tap "Dalej" 20; sleep 1; done
$UI tap "Ruszam w drogę" 20
sleep 1
$UI wait "· przełom" 60
$UI noscroll
$UI shot shots/03-home.png
$UI tap "Medytuj"
$UI wait "Medytacja ×5" 30
sleep 2
$UI shot shots/03b-meditating.png
$UI tap "Rozwój" 20
$UI wait "METODY KULTYWACJI" 30
$UI shot shots/03c-cultivation-sheet.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI tap "Posiadłość"
$UI wait "Poletka" 60
$UI noscroll
$UI shot shots/03d-estate.png
$UI tap "Uczniowie"
$UI wait "Uczniowie:" 30
$UI shot shots/03e-disciples.png
adb shell input keyevent KEYCODE_BACK; sleep 2

step "quests: check-in"
$UI tap "Zadania"
$UI wait "Nowe zadania o północy" 60
$UI tap "Zamelduj" 20
$UI wait "MELDUNEK" 30
$UI shot shots/04-checkin.png
$UI tap "Dalej" 20
$UI noscroll
$UI shot shots/04b-quests.png
$UI tap "Osiągnięcia"
$UI wait "Tajne osiągnięcia" 30
$UI paged "Pierwsza krew" 8   # list order follows progress, so it may sit on a later page
$UI shot shots/04c-achievements.png
$UI tap "Tytuły"
$UI wait "Nad postacią" 30
$UI noscroll
$UI shot shots/04d-titles.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "Opowieść" 30
$UI tap "Opowieść"
$UI wait "ROZDZIAŁ 1" 30
$UI noscroll
$UI shot shots/04e-story.png
adb shell input keyevent KEYCODE_BACK; sleep 2

step "world: full-screen map with emulated GPS (Krakow)"
$UI tap "Świat"
$UI wait "Pozycja" 60
for i in $(seq 1 12); do
  lon=$(python3 -c "print(19.9372 + $i * 0.00004)")
  adb emu geo fix "$lon" 50.0614 220 >/dev/null || true
  sleep 3
done
$UI wait "Pozycja potwierdzona" 60 || echo "position not confirmed by the server within the time limit"
$UI noscroll
$UI shot shots/05-world.png
$UI tap "Okolica"
$UI wait "Miejsca w okolicy" 20
$UI shot shots/05b-places.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI tap "Płomień"
$UI wait "Smocza Jama" 30
$UI shot shots/05c-flame.png
adb shell input keyevent KEYCODE_BACK; sleep 1

step "combat: training cards, duel, forfeit"
$UI tap "Walka"
$UI wait "Trening" 60
$UI noscroll
$UI shot shots/06-combat.png
$UI tap "Drewniany Manekin"
$UI wait "Prędkość" 60
$UI noscroll
$UI shot shots/06b-battle.png
# auto battles are fast: no taps during the fight (buttons move when it ends)
$UI wait "WYNIK" 180
$UI shot shots/06c-battle-turn.png
sleep 4
$UI shot shots/06e-auto-result.png
$UI tap "Zabierz" 15 || $UI tap "Dalej" 15
$UI wait "Walcz ponownie" 20
$UI tap "Wyjdź"
$UI wait "Trening" 30
for foe in "Kamienny Golem" "Zdrajca Sekty"; do
  $UI paged "$foe" 6 0.45
  $UI tap "$foe"
  $UI wait "Poddaj" 60
  sleep 2
  $UI shot "shots/06-$(echo "$foe" | tr ' ' '_').png"
  # the automatic fight may end on its own before the forfeit lands
  $UI tap "Poddaj" 5 || true
  $UI tap "Na pewno?" 5 || true
  $UI wait "WYNIK" 120
  $UI tap "Dalej" 20 || $UI tap "Zabierz" 15
  $UI tap "Wyjdź"
  $UI wait "Trening" 30
done
$UI tap "Wieża"
$UI wait "WIEŻA PRÓB" 30
$UI noscroll
$UI shot shots/06h-tower.png
$UI tap "Wejdź na piętro 1"
$UI wait "Poddaj" 60
sleep 2
$UI shot shots/06i-tower-battle.png
$UI wait "WYNIK" 180
$UI tap "Zabierz" 15 || $UI tap "Dalej" 15
$UI wait "Następne piętro" 20 || $UI wait "Spróbuj ponownie" 5
$UI shot shots/06i2-tower-next.png
$UI tap "Wyjdź"
$UI wait "WIEŻA PRÓB" 30
$UI wait "Piętro 1 z 330" 30
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "Trening" 30
$UI tap "Bossy"
$UI wait "legowisk" 30
$UI noscroll
$UI shot shots/06d-bosses.png
$UI tap "Atakuj bossa"
$UI wait "Poddaj" 60
sleep 3
$UI shot shots/06j-world-boss.png
$UI tap "Poddaj" 5 || true
$UI tap "Na pewno?" 5 || true
$UI wait "WYNIK" 120
$UI tap "Dalej" 20 || $UI tap "Zabierz" 15
$UI tap "Wyjdź"
$UI wait "legowisk" 30
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "Trening" 30
$UI tap "Łowy"
$UI wait "Wybierz przeciwnika" 30
$UI noscroll
$UI shot shots/06f-hunt-pick.png
$UI tap "Drewniany Manekin"
$UI wait "Zakończ i odbierz" 60
$UI noscroll
$UI shot shots/06g-hunt.png
$UI tap "Zakończ i odbierz"
$UI wait "POLOWANIE" 30
$UI tap "Dalej" 20
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "Trening" 30
$UI tap "Arena"
$UI wait "ARENA" 30
$UI noscroll
$UI shot shots/06k-arena.png

step "hero: silhouette, meridians, techniques, pet, Dao"
$UI tap "Bohater"
$UI wait "Cechy, ścieżka i tytuły" 60
$UI noscroll
$UI shot shots/07-hero.png
$UI tap "Cechy, ścieżka i tytuły"
$UI wait "ŚCIEŻKA" 30
$UI shot shots/07f-origins.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI tap "Meridiany"
$UI wait "Wolne punkty" 60
$UI shot shots/07b-meridians.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "MOC BOJOWA" 30
$UI tap "Techniki"
$UI wait "Ułóż najlepsze" 60
$UI shot shots/07c-techniques.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "MOC BOJOWA" 30
$UI tap "Towarzysz"
$UI wait "Nie masz jeszcze towarzysza" 60
$UI shot shots/07d-pet.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "MOC BOJOWA" 30
$UI tap "Dao"
$UI wait "Ułóż własną" 60
$UI shot shots/07e-dao.png
adb shell input keyevent KEYCODE_BACK; sleep 1
$UI wait "MOC BOJOWA" 30
$UI tap "Sztuki"
$UI wait "Płomienie:" 60
$UI noscroll
$UI shot shots/07g-arts-flames.png
for t in "Trucizny:Ciało Toksyn" "Gu:Gniazdo Gu" "Ciało:KĄPIELE" "Miecz:Intencja Miecza" "Gałąź:Gałąź ścieżki"; do
  $UI tap "${t%%:*}" 20
  $UI wait "${t#*:}" 30
  $UI noscroll
  $UI shot "shots/07h-arts-${t%%:*}.png"
done

step "bag, forge, market"
$UI tap "Sakwa"
$UI wait "Kamieni Duchowych" 60
$UI noscroll
$UI shot shots/08-bag.png
$UI tap "Wędrowny kupiec"
$UI wait "podstawowe towary" 30
$UI noscroll
$UI shot shots/08a-merchant.png
$UI tap "Zamknij"
$UI wait "Kamieni Duchowych" 60
$UI tap "Kuźnia"
$UI wait "Kowalstwo" 60
$UI shot shots/08b-forge.png
$UI tap "Targ"
$UI wait "Moje oferty" 60
$UI shot shots/08c-market.png
adb shell input keyevent KEYCODE_BACK; sleep 2

step "sect and rankings"
$UI tap "Sekta"
$UI wait "Nie należysz" 60
$UI noscroll
$UI shot shots/09-sect.png
$UI tap "Ranking"
$UI wait "Twoje miejsce" 60
$UI shot shots/09b-ranking.png

step "news"
$UI tap "Wieści"
$UI wait "Z CAŁEGO JIANGHU" 60
$UI shot shots/10-news.png
adb shell input keyevent KEYCODE_BACK; sleep 2
$UI tap "Kultywacja"
$UI wait "· przełom" 60

step "restart: progress must survive"
sleep 2
adb shell input keyevent KEYCODE_HOME
sleep 2
adb shell am force-stop $PKG
adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 >/dev/null
$UI wait "· przełom" 90
$UI wait "Lin" 10
$UI shot shots/08-after-restart.png

step "layout audit: every screen fits without scrolling"
adb logcat -d > shots/logcat.txt
grep "JianghuLayout" shots/logcat.txt | grep "AUDIT" || true
if grep -E "JianghuLayout.*(OVERFLOW|CLIPPED)" shots/logcat.txt; then
  echo "::error::layout does not fit the screen ($(grep -cE 'JianghuLayout.*(OVERFLOW|CLIPPED)' shots/logcat.txt) problems)"; exit 1
fi

step "crash check"
adb logcat -d > shots/logcat.txt
if grep -E "FATAL EXCEPTION|Unhandled managed exception|AndroidRuntime: Process: $PKG" shots/logcat.txt; then
  echo "app crashed"; exit 1
fi
adb shell pidof $PKG >/dev/null || { echo "app is not running"; exit 1; }
echo "SMOKE OK"
