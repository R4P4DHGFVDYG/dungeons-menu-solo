# Prints the Dungeons Menu log lines (GVAS string array), in file order: python -I read-menu-log.py [count] [file].
# GVAS stores ASCII strings as UTF-8 and strings with accents as UTF-16, so both are collected with their offsets.
# Since 0.3.2 the log is DungeonsMenu_Log.sav: Better Blueprint Loader deletes NeoRune_<Mod>.sav on every level start.
import re, sys, os
saves = os.path.expandvars(r'%LOCALAPPDATA%\Dungeons2\Saved\SaveGames')
path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(saves, 'DungeonsMenu_Log.sav')
if not os.path.exists(path): path = os.path.join(saves, 'NeoRune_DungeonsMenu.sav')
if not os.path.exists(path): sys.exit('Sem log ainda: ' + path)
data = open(path, 'rb').read()
found = [(m.start(), m.group().decode('utf-8', 'replace')) for m in re.finditer(rb'\[(?:menu|diag|hotkey)\][^\x00]*', data)]
found += [(m.start(), m.group().decode('utf-16le')) for m in re.finditer(rb'\[\x00(?:m\x00e\x00n\x00u\x00|d\x00i\x00a\x00g\x00|h\x00o\x00t\x00k\x00e\x00y\x00)\]\x00(?:[^\x00]\x00|[\x00-\xff][^\x00])*?(?=\x00\x00)', data)]
found.sort()
count = int(sys.argv[1]) if len(sys.argv) > 1 else 0
for _, line in found[-count:] if count else found: print(line)
