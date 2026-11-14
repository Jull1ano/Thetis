# set-aether-colours.py - Thetis Setup colours in AetherSDR's palette (Jull1ano fork, goes with the
# "AetherSDR Style" skin). Writes the colour settings of a Thetis data folder's database.xml (Thetis must be closed).
# The spectrum trace and fill (clrbtnDataLine / clrbtnDataFill, and the fork's gradients) are left as they are.
#
# Usage: python -I set-aether-colours.py <Thetis data folder> [--dry]
import glob, os, re, sys

COLOURS = {
    # VFO: AetherSDR's light digits on its dark panel; the inactive VFO dimmer; small digits a touch dimmer
    'clrbtnVFOBackground': '10.16.24.255', 'clrbtnVFOLight': '230.240.250.255', 'clrbtnVFODark': '142.168.192.255',
    'clrbtnVFOSmallColor': '200.216.232.255', 'clrbtnInfoButtonsColor': '0.180.216.255',
    # band text under the VFO
    'clrbtnBandBackground': '10.16.24.255', 'clrbtnBandLight': '0.180.216.255', 'clrbtnBandDark': '80.112.140.255',
    'clrbtnOutOfBand': '58.42.14.255',
    # status bar
    'clrbtnStatusBarBackground': '15.15.26.255', 'clrbtnStatusBarText': '142.168.192.255',
    # cursor / peak readout
    'clrbtnPeakText': '0.180.216.255', 'clrbtnPeakBackground': '10.16.24.255',
    # spectrum: dark navy, a subtle grid, grey-blue labels, the VFO line and passband in AetherSDR's cyan
    'clrbtnBackground': '11.14.24.255', 'clrbtnGrid': '26.35.48.255', 'clrbtnGridFine': '20.28.40.255',
    'clrbtnHGridColor': '26.35.48.255', 'clrbtnText': '142.168.192.255', 'clrbtnZeroLine': '0.180.216.255',
    'clrbtnFilter': '0.180.216.50', 'clrbtnNoiseFloor': '80.96.112.255', 'clrbtnNoiseFloorText': '142.168.192.255',
    'clrbtnWaterfall_time_label_colour': '200.216.232.255', 'clrbtnBandEdge': '255.122.26.255',
    # multimeter
    'clrbtnMeterBackground': '10.16.24.255', 'clrbtnMeterDigBackground': '10.16.24.255', 'clrbtnMeterDigText': '230.240.250.255',
    'clrbtnMeterLeft': '47.158.106.255', 'clrbtnMeterRight': '108.197.106.255', 'clrbtnMeterEdgeBackground': '10.16.24.255',
    'clrbtnMeterEdgeLow': '200.216.232.255', 'clrbtnMeterEdgeHigh': '255.77.77.255',
    # selected buttons without a skin picture
    'clrbtnBtnSel': '0.112.192.255',
}

data = sys.argv[1]
dry = '--dry' in sys.argv
dbs = glob.glob(os.path.join(data, 'DB', '*', 'database.xml'))
if len(dbs) != 1:
    sys.exit('expected one database.xml under ' + data + ', found ' + str(dbs))
p = dbs[0]
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
t = raw.decode('utf-8-sig')
changed = 0
for k, v in COLOURS.items():
    m = re.search(r'(<Key>' + k + r'</Key>\s*<Value>)([^<]*)(</Value>)', t)
    if not m:
        print('missing', k)
        continue
    if m.group(2) != v:
        print('%-36s %s -> %s' % (k, m.group(2), v))
        t = t[:m.start(2)] + v + t[m.end(2):]
        changed += 1
if not dry and changed:
    open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + t.encode('utf-8'))
print(('would change' if dry else 'changed'), changed, 'in', p)
