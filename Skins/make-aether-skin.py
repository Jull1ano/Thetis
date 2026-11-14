# make-aether-skin.py - a Thetis skin in AetherSDR's look (Jull1ano fork)
#
# Draws every skinned console control of Thetis in AetherSDR's colours (its BandBar.cpp / applet styles and
# default-dark theme): mode buttons teal, ham bands and filters grey-blue, SWL bands amber, DSP toggles green when
# on, the rest dark navy with a blue "on" state; hover and disabled states too; sliders as AetherSDR's (a thin
# groove, a round light handle); a dark navy background; and the skin's xml with the text colours.
#
# Skin only: no Thetis code. Thetis reads Skins\<name>\Console\<control>-<state>.png (0 normal, 1 checked /
# pressed, 2 disabled, 3 disabled + checked, 6 mouse over, 7 mouse over + checked), <control>-back.png /
# -head.png for sliders, Console\console.png (stretched over the window) and Skins\<name>\<name>.xml.
#
# Usage: python -I make-aether-skin.py <out folder (Skins\<name>)> <Thetis Console source folder> <skins folder>
#   the source folder gives the control names, types and sizes (console.Designer.cs, console.resx); the skins
#   folder (the installed ones) adds control names the designer does not show (QuickRecall pad, info bar).

import os, re, sys
from PIL import Image, ImageDraw, ImageFont

OUT, SRC, SKINS = sys.argv[1], sys.argv[2], sys.argv[3]
NAME = os.path.basename(os.path.normpath(OUT))
S = 2  # pictures drawn at 200 % (a 4K screen); at 100 % Thetis scales them down

# ---------------------------------------------------------------- AetherSDR's colours
BG = (11, 17, 27)            # the window: the band bar's rgba(10, 14, 22) on the app's #0f0f1a, a touch bluer
BAR_FACE = (30, 40, 55, 220)  # BandBar buttons: rgba(30, 40, 55, 220)
STYLES = {
    # name: normal face, normal border, hover face, hover border, checked face, checked border, text, disabled border
    'ham':   dict(face=BAR_FACE, border='#304050', hface=(0, 112, 192, 180), hborder='#0090e0',
                  cface=(0, 112, 192, 210), cborder='#30b0ff', text='#c8d8e8', dborder='#263442', radius=2),
    'swl':   dict(face=BAR_FACE, border='#504030', hface=(160, 110, 0, 180), hborder='#e0a000',
                  cface=(160, 110, 0, 210), cborder='#ffc030', text='#ffcc66', dborder='#3a3226', radius=2),
    'mode':  dict(face=BAR_FACE, border='#2c5048', hface=(0, 140, 110, 180), hborder='#20c090',
                  cface=(0, 140, 110, 210), cborder='#40e0b0', text='#a8e8d0', dborder='#243a36', radius=2),
    # every other button the ham buttons' way (the user: "one class, the same everywhere"): the bar face with its
    # subtle edge, the blue hover; on: green for the noise reduction / DSP ones (AetherSDR's NR2), else blue
    'dsp':   dict(face=BAR_FACE, border='#304050', hface=(0, 112, 192, 180), hborder='#0090e0',
                  cface=(0, 128, 72, 215), cborder='#00b070', text='#c8d8e8', dborder='#263442', radius=2),
    'toggle': dict(face=BAR_FACE, border='#304050', hface=(0, 112, 192, 180), hborder='#0090e0',
                   cface=(0, 112, 192, 210), cborder='#30b0ff', text='#c8d8e8', dborder='#263442', radius=2),
    # MOX / TUNE: AetherSDR's TX colours (amber edge, #f0c890 text), on = red
    'tx':    dict(face=BAR_FACE, border='#d08020', hface=(160, 90, 0, 180), hborder='#e09030',
                  cface=(160, 28, 28, 220), cborder='#ff4d4d', text='#f0c890', dborder='#3a3020', radius=2),
    # plain buttons: pressed = the ham buttons' "on"
    'button': dict(face=BAR_FACE, border='#304050', hface=(0, 112, 192, 180), hborder='#0090e0',
                   cface=(0, 112, 192, 230), cborder='#30b0ff', text='#c8d8e8', dborder='#263442', radius=2),
}


def rgb(c):
    if isinstance(c, tuple):
        if len(c) == 4:  # rgba over the background
            a = c[3] / 255.0
            return tuple(int(round(c[i] * a + BG[i] * (1 - a))) for i in range(3))
        return c
    c = c.lstrip('#')
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))


def mix(c, k, other=BG):
    a, b = rgb(c), rgb(other)
    return tuple(int(round(a[i] * k + b[i] * (1 - k))) for i in range(3))


def hexcol(c):
    return 'FF%02X%02X%02X' % rgb(c)


# ---------------------------------------------------------------- the controls
designer = open(os.path.join(SRC, 'console.Designer.cs'), encoding='utf-8-sig', errors='replace').read()
resx = open(os.path.join(SRC, 'console.resx'), encoding='utf-8-sig', errors='replace').read()
types = {m.group(1): m.group(2).split('.')[-1] for m in re.finditer(r'this\.(\w+) = new ([\w\.]+)\(\);', designer)}
sizes = {m.group(1): (int(m.group(2)), int(m.group(3))) for m in re.finditer(
    r'<data name="(\w+)\.Size" type="System\.Drawing\.Size, System\.Drawing">\s*<value>(\d+), (\d+)</value>', resx)}
appear = set(m.group(1) for m in re.finditer(
    r'<data name="(\w+)\.Appearance" type="System\.Windows\.Forms\.Appearance, System\.Windows\.Forms">\s*<value>Button</value>', resx))
appear |= set(m.group(1) for m in re.finditer(r'this\.(\w+)\.Appearance = System\.Windows\.Forms\.Appearance\.Button;', designer))
fore = {}
for m in re.finditer(r'this\.(\w+)\.ForeColor = System\.Drawing\.(?:Color|SystemColors)\.(\w+);', designer):
    fore[m.group(1)] = m.group(2)

skin_pngs = {}  # name -> size of an installed skin's picture (for controls the designer does not size)
for s in sorted(os.listdir(SKINS)):
    for sub in ('Console', 'console'):
        d = os.path.join(SKINS, s, sub)
        if os.path.isdir(d):
            for f in os.listdir(d):
                m = re.match(r'(.+)-\d\.png$', f)
                if m and m.group(1) not in skin_pngs:
                    try:
                        skin_pngs[m.group(1)] = Image.open(os.path.join(d, f)).size
                    except Exception:
                        pass

buttons = set()
for n, t in types.items():
    if t in ('ButtonTS', 'Button') or (t in ('CheckBoxTS', 'CheckBox', 'RadioButtonTS', 'RadioButton') and n in appear):
        buttons.add(n)
# names only the installed skins have (an older Thetis' controls); one per name ignoring case, as Windows files are
# (a skin's "chkrx2" would overwrite "chkRX2"), the designer's spelling first
known = set(n.lower() for n in buttons)
for n in sorted(skin_pngs):
    if n.startswith(('ucQuickRecallPad', 'infoBar', 'cQuickRecall', 'pic')) or n == 'btn' or n.lower() in known:
        continue
    known.add(n.lower())
    buttons.add(n)
buttons -= {'btnHidden'}

DSP = re.compile(r'(NR|ANF|NB\d?|BIN|MNF|TNF|SNB|Squelch|SQL|APF|NoiseGate|CPDR|DX|BCI|MUT|Mute)', re.I)
TX = re.compile(r'^chk(MOX|TUN|2TONE|RX2MOX)$|^chkRX2TUN$', re.I)


def category(n):
    if n.startswith(('radMode', 'radRX2Mode')):
        return 'mode'
    if re.match(r'rad(RX2)?BandGEN\d', n):
        return 'swl'
    if n.startswith(('radBand', 'radRX2Band', 'radFilter', 'radRX2Filter', 'btnBand')):
        return 'ham'
    if TX.match(n):
        return 'tx'
    if n.startswith(('btn', 'ucQuickRecall')):
        return 'button'
    if n.startswith(('chk', 'ck', 'rad')) and DSP.search(n[3:]):
        return 'dsp'
    return 'toggle'


def size_of(n):
    if n in sizes:
        return sizes[n]
    return skin_pngs.get(n, (50, 23))


# ---------------------------------------------------------------- drawing
SS = 4  # supersampling for clean anti-aliased edges


def rounded(draw, box, r, fill):
    draw.rounded_rectangle(box, radius=r, fill=fill)


def button_image(w, h, st, state, glyph=None):
    """state: 0 normal, 1 checked/pressed, 2 disabled, 3 disabled checked, 6 hover, 7 hover checked"""
    W, H = w * S, h * S
    big = Image.new('RGB', (W * SS, H * SS), BG)
    d = ImageDraw.Draw(big)
    on = state in (1, 3, 7)
    if state in (6,):
        face, border = st['hface'], st['hborder']
    elif state in (1, 7):
        face, border = st['cface'], st['cborder']
    elif state == 3:
        face, border = mix(st['cface'], 0.45), st['dborder']
    else:
        face, border = st['face'], st['border'] if state != 2 else st['dborder']
    if state == 7:
        border = mix('#ffffff', 0.35, border)  # under the mouse: the edge a little brighter
    r = st['radius'] * S * SS
    lw = S * SS  # 1 px at 100 %
    inset = 1 * SS  # the edge of the picture: a hair of background so neighbours do not touch
    rounded(d, (inset, inset, W * SS - 1 - inset, H * SS - 1 - inset), r, rgb(border))
    rounded(d, (inset + lw, inset + lw, W * SS - 1 - inset - lw, H * SS - 1 - inset - lw), max(0, r - lw), rgb(face))
    if glyph:
        glyph(d, W * SS, H * SS, on, state)
    return big.resize((W, H), Image.LANCZOS)


def glyph_label(text):
    def g(d, W, H, on, state):
        col = rgb('#ffffff' if on else ('#c8d8e8' if state != 2 else '#56687a'))
        try:
            f = ImageFont.truetype(os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'micross.ttf'), int(8.25 * 96 / 72 * S * SS * 1.05))
        except Exception:
            f = ImageFont.load_default()
        d.text((W / 2, H / 2), text, fill=col, font=f, anchor='mm', stroke_width=int(S * SS * 0.35), stroke_fill=col)
    return g


def glyph_power(d, W, H, on, state):
    col = rgb('#00ff88') if on else rgb('#c8d8e8' if state != 2 else '#56687a')
    cx, cy, rr = W / 2, H / 2, min(W, H) * 0.26
    lw = max(1, int(S * SS * 1.6))
    d.arc((cx - rr, cy - rr, cx + rr, cy + rr), start=-60, end=240, fill=col, width=lw)
    d.line((cx, cy - rr * 1.25, cx, cy - rr * 0.1), fill=col, width=lw)


def glyph_rec(d, W, H, on, state):
    col = rgb('#ff4d4d') if on else rgb('#d04040' if state != 2 else '#56687a')
    rr = min(W, H) * 0.2
    d.ellipse((W / 2 - rr, H / 2 - rr, W / 2 + rr, H / 2 + rr), fill=col)


def glyph_play(d, W, H, on, state):
    col = rgb('#4dd87a') if on else rgb('#c8d8e8' if state != 2 else '#56687a')
    rr = min(W, H) * 0.22
    d.polygon([(W / 2 - rr * 0.8, H / 2 - rr), (W / 2 - rr * 0.8, H / 2 + rr), (W / 2 + rr, H / 2)], fill=col)


def arrow_image(w, h, which, state):
    """QuickRecall pad: a face with a triangle (the fork measures both from the picture); which 0 left 1 down 2 right"""
    st = STYLES['button']
    W, H = w * S, h * S
    big = Image.new('RGB', (W * SS, H * SS), BG)
    d = ImageDraw.Draw(big)
    face = st['hface'] if state == 6 else (st['cface'] if state == 1 else st['face'])
    border = st['hborder'] if state == 6 else (st['cborder'] if state == 1 else st['border'])
    r = 3 * S * SS
    lw = S * SS
    i0 = 1 * SS
    rounded(d, (i0, i0, W * SS - 1 - i0, H * SS - 1 - i0), r, rgb(border))
    rounded(d, (i0 + lw, i0 + lw, W * SS - 1 - i0 - lw, H * SS - 1 - i0 - lw), r - lw, rgb(face))
    cx, cy, a = W * SS / 2, H * SS / 2, min(W, H) * SS * 0.2
    col = rgb('#e6f0fa')
    if which == 0:
        pts = [(cx + a * 0.6, cy - a), (cx + a * 0.6, cy + a), (cx - a * 0.8, cy)]
    elif which == 2:
        pts = [(cx - a * 0.6, cy - a), (cx - a * 0.6, cy + a), (cx + a * 0.8, cy)]
    else:
        pts = [(cx - a, cy - a * 0.6), (cx + a, cy - a * 0.6), (cx, cy + a * 0.8)]
    d.polygon(pts, fill=col)
    return big.resize((W, H), Image.LANCZOS)


def slider_back(w, h, vertical=False):
    W, H = w * S, h * S
    big = Image.new('RGB', (W * SS, H * SS), BG)
    d = ImageDraw.Draw(big)
    gh = 4 * S * SS  # AetherSDR: a 4 px groove, radius 2
    m = 5 * S * SS
    if vertical:
        x0 = (W * SS - gh) // 2
        rounded(d, (x0, m, x0 + gh, H * SS - 1 - m), 2 * S * SS, rgb('#24384c'))
    else:
        y0 = (H * SS - gh) // 2
        rounded(d, (m, y0, W * SS - 1 - m, y0 + gh), 2 * S * SS, rgb('#24384c'))
    return big.resize((W, H), Image.LANCZOS)


KNOB = 14  # the handle: AetherSDR's 12 px, a little larger (the user)


def slider_head():
    # the picture gives the handle's size (Thetis scales it by the DPI); the fork draws the knob itself as a vector
    # circle (<Slider> below); this picture is what other Thetis builds show
    big = Image.new('RGBA', (KNOB * 8, KNOB * 8), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.ellipse((2, 2, KNOB * 8 - 3, KNOB * 8 - 3), fill=rgb('#c8d8e8') + (255,))
    small = big.resize((KNOB, KNOB), Image.LANCZOS)
    out = Image.new('RGB', (KNOB, KNOB), BG)  # opaque: the handle's corners show the background
    out.paste(small, (0, 0), small)
    return out


# ---------------------------------------------------------------- write the skin
cons = os.path.join(OUT, 'Console')
os.makedirs(cons, exist_ok=True)
for f in os.listdir(cons):
    if f.lower().endswith('.png'):
        os.remove(os.path.join(cons, f))

count = {}
for n in sorted(buttons):
    w, h = size_of(n)
    cat = category(n)
    st = STYLES[cat]
    glyph = {'chkPower': glyph_power, 'ckQuickRec': glyph_rec, 'ckQuickPlay': glyph_play, 'chkRX2': glyph_label('RX2')}.get(n)
    states = (0, 1, 2, 3, 6, 7)
    for state in states:
        button_image(w, h, st if n != 'chkPower' else STYLES['dsp'], state, glyph).save(os.path.join(cons, '%s-%d.png' % (n, state)))
    count[cat] = count.get(cat, 0) + 1

# info bar buttons (ucInfoBar: "_button1" / "_button2")
for n in ('infoBar_button1', 'infoBar_button2'):
    w, h = skin_pngs.get(n, (50, 23))
    for state in (0, 1, 2, 3, 6, 7):
        button_image(w, h, STYLES['toggle'], state).save(os.path.join(cons, '%s-%d.png' % (n, state)))

# QuickRecall pad: 20 x 20 at 100 % (40 x 40 at 200 %, used as they are)
for which, n in enumerate(('ucQuickRecallPad_previous', 'ucQuickRecallPad_list', 'ucQuickRecallPad_next')):
    for state in (0, 1, 6):
        arrow_image(20, 20, which, state).save(os.path.join(cons, '%s-%d.png' % (n, state)))

# sliders
ptbs = sorted(n for n, t in types.items() if t == 'PrettyTrackBar')
vertical = set(m.group(1) for m in re.finditer(r'this\.(\w+)\.Orientation = System\.Windows\.Forms\.Orientation\.Vertical;', designer))
head = slider_head()
for n in ptbs:
    w, h = sizes.get(n, (120, 24))
    slider_back(w, h, n in vertical).save(os.path.join(cons, n + '-back.png'))
    head.save(os.path.join(cons, n + '-head.png'))

# the window: dark navy, a faint vertical gradient (Thetis stretches it over the window)
bgW, bgH = 1920, 1080
bg = Image.new('RGB', (bgW, bgH))
top, bot = (12, 19, 30), (10, 15, 24)
for y in range(bgH):
    k = y / (bgH - 1)
    c = tuple(int(round(top[i] * (1 - k) + bot[i] * k)) for i in range(3))
    bg.paste(c, (0, y, bgW, y + 1))
bg.save(os.path.join(cons, 'console.png'))
Image.new('RGB', (400, 200), (12, 13, 24)).save(os.path.join(cons, 'picDisplay.png'))

# ---------------------------------------------------------------- the xml: text colours
neutral = {'White', 'ControlLightLight', 'ControlText', 'ControlLight', 'Gainsboro', 'WhiteSmoke', 'LightGray', 'Silver'}
# the VFO, band and meter readouts take Setup's colours (Appearance > VFO / Band Data / Meter)
VFO_BOXES = {'txtVFOAFreq', 'txtVFOAMSD', 'txtVFOALSD', 'txtVFOABand', 'txtVFOBFreq', 'txtVFOBMSD', 'txtVFOBLSD', 'txtVFOBBand',
             'txtMultiText', 'txtRX2Meter'}
lines = ['<?xml version="1.0" encoding="utf-8"?>', '<Form>',
         '  <BackColor>%s</BackColor>' % hexcol(BG), '  <ForeColor>%s</ForeColor>' % hexcol('#c8d8e8'), '  <Controls>']
for n in sorted(buttons):
    if n not in types:
        continue
    st = STYLES['dsp' if n == 'chkPower' else category(n)]
    lines.append('    <%s><ForeColor>%s</ForeColor><FlatAppearance><BorderSize>0</BorderSize></FlatAppearance></%s>' % (n, hexcol(st['text']), n))
for n, t in sorted(types.items()):
    if t in ('LabelTS', 'Label') and fore.get(n, 'White') in neutral:
        lines.append('    <%s><ForeColor>%s</ForeColor></%s>' % (n, hexcol('#c8d8e8'), n))
    elif t in ('ComboBoxTS', 'ComboBox'):
        lines.append('    <%s><BackColor>%s</BackColor><FlatStyle>Flat</FlatStyle><ForeColor>%s</ForeColor></%s>' % (n, hexcol((30, 40, 55, 220)), hexcol('#c8d8e8'), n))
    elif t in ('NumericUpDownTS', 'NumericUpDown'):
        lines.append('    <%s><BackColor>%s</BackColor><ForeColor>%s</ForeColor></%s>' % (n, hexcol((20, 28, 40, 220)), hexcol('#c8d8e8'), n))
    elif t in ('TextBoxTS', 'TextBox') and n not in VFO_BOXES:
        # value boxes (tune step, quick memory, band stack): AetherSDR's value field (BandBar kValueStyle); a 3D border
        # (a frame round the window, which the fork draws as the theme's subtle edge - a single line edit box draws its
        # border inside itself, grey, where it cannot be reached)
        lines.append('    <%s><BackColor>%s</BackColor><BorderStyle>Fixed3D</BorderStyle><ForeColor>%s</ForeColor></%s>' % (n, hexcol((20, 28, 40, 220)), hexcol('#c8d8e8'), n))
for n in ('chkForkNR2', 'chkForkNR3', 'chkForkNR4', 'chkForkNNR'):  # the fork's NR buttons (drawn with chkNR's pictures)
    lines.append('    <%s><ForeColor>%s</ForeColor></%s>' % (n, hexcol(STYLES['dsp']['text']), n))
lines += ['  </Controls>',
          # the fork's extras (ForkTheme.cs; other Thetis builds ignore them): frames without a border (the title
          # stays), AetherSDR's subtle box edges, modern drop-downs as AetherSDR's combo boxes
          '  <ForkTheme>',
          '    <GroupBorder>00000000</GroupBorder>',
          '    <BoxBorder>%s</BoxBorder>' % hexcol('#304050'),
          '    <BoxRadius>3</BoxRadius>',
          '    <Combo>',
          '      <Back>%s</Back>' % hexcol((30, 40, 55, 220)),
          '      <Border>%s</Border>' % hexcol('#304050'),
          '      <BorderHover>%s</BorderHover>' % hexcol('#0090e0'),
          '      <Text>%s</Text>' % hexcol('#c8d8e8'),
          '      <Arrow>%s</Arrow>' % hexcol('#8ea8c0'),
          '      <ListBack>%s</ListBack>' % hexcol('#1a2433'),
          '      <ListSelect>%s</ListSelect>' % hexcol('#0070c0'),
          '      <ListSelectText>FFFFFFFF</ListSelectText>',
          '      <Radius>2</Radius>',
          '    </Combo>',
          '    <Slider>',
          '      <Knob>%s</Knob>' % hexcol('#c8d8e8'),
          '      <KnobHover>FFFFFFFF</KnobHover>',
          '    </Slider>',
          '  </ForkTheme>',
          '</Form>', '']
open(os.path.join(OUT, NAME + '.xml'), 'w', encoding='utf-8', newline='\r\n').write('\n'.join(lines))

print('skin', NAME, 'buttons', len(buttons), count, 'sliders', len(ptbs), 'xml lines', len(lines))
print('pictures', len(os.listdir(cons)))
