"""Flag cable labels, label v2: IA|<cable>|<door>|<controller module>|<device module>|<CTRL|DEV>.

Each strip is [face A | wrap round the cable | face B]. Fold the wrap round the cable and stick A and B back to back;
both faces read upright and each shows its own QR, so the phone can read the label from either side.
One A4 page: cable C01 (Reader 1) and cable C02 (Reader 2), two DEV/CTRL pairs each.

This is the script that made the labels used on the bench (20 Oct 2026), added to the repo unchanged so the label
can be reproduced. Needs: pip install segno reportlab.  Run: python tools/labels/make_flags_v2.py [out.pdf]
"""
import glob
import sys

import segno
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas


def font(name, pat):
    f = glob.glob(pat, recursive=True)
    if f:
        pdfmetrics.registerFont(TTFont(name, f[0]))
        return name
    return 'Helvetica-Bold' if 'Bold' in name else 'Helvetica'


BOLD = font('LBold', '/usr/share/fonts/**/DejaVuSans-Bold.ttf')
REG = font('LReg', '/usr/share/fonts/**/DejaVuSans.ttf')

FACE_W, H = 46 * mm, 24 * mm     # one flag face
WRAP = 20 * mm                   # goes round the cable (about 6 mm diameter)
QUIET = 4                        # quiet zone in modules (ISO/IEC 18004)
MODULE = 0.58 * mm               # same module size as the printed v1 labels
ERROR = 'm'                      # about 15 % may be damaged

# The two cables on the bench. Door is for people; the controller module is where the cable must go.
CABLES = [
    dict(cable='C01', door='D1', ctrl='A1610.READER1', dev='A4020E', port='READER 1'),
    dict(cable='C02', door='D2', ctrl='A1610.READER2', dev='A4020E', port='READER 2'),
]
END = {'DEV': 'READER END', 'CTRL': 'CONTROLLER END'}


def payload(c, end):
    return 'IA|{cable}|{door}|{ctrl}|{dev}|'.format(**c) + end


def far_end(c, end):
    """Where the other end of the cable terminates (cabling practice: name the far end)."""
    door = 'Door ' + c['door'][1:]
    if end == 'DEV':
        return 'A1610 controller', 'port ' + c['port']
    return door + ' reader', 'AXIS A4020-E'


def fit(c, text, face, width, size):
    while size > 3 and pdfmetrics.stringWidth(text, face, size) > width:
        size -= 0.1
    c.setFont(face, size)


def qr(c, text, x, y):
    """Draw the QR (quiet zone included) with its lower-left corner at x, y; returns its side."""
    q = segno.make(text, error=ERROR, boost_error=False, micro=False)
    n = q.symbol_size(border=0)[0]
    side = (n + 2 * QUIET) * MODULE
    c.setFillColorRGB(1, 1, 1)
    c.rect(x, y, side, side, stroke=0, fill=1)
    c.setFillColorRGB(0, 0, 0)
    for r, row in enumerate(q.matrix):
        for k, v in enumerate(row):
            if v & 1:
                c.rect(x + (QUIET + k) * MODULE, y + side - (QUIET + r + 1) * MODULE, MODULE, MODULE, stroke=0, fill=1)
    return side


def face(c, x, y, cab, end):
    text = payload(cab, end)
    top = y + H
    side = qr(c, text, x, y + (H - 21.5 * mm) / 2)
    tx = x + side + 0.5 * mm
    tw = x + FACE_W - 1.2 * mm - tx
    # end band
    c.setFillColorRGB(0, 0, 0)
    c.rect(tx, top - 5.2 * mm, x + FACE_W - 0.8 * mm - tx, 4.4 * mm, stroke=0, fill=1)
    c.setFillColorRGB(1, 1, 1)
    fit(c, END[end], BOLD, tw - 1 * mm, 6.4)
    c.drawString(tx + 0.8 * mm, top - 4.0 * mm, END[end])
    # cable id
    c.setFillColorRGB(0, 0, 0)
    ident = cab['cable'] + ' · ' + cab['door']
    fit(c, ident, BOLD, tw, 9.5)
    c.drawString(tx, top - 9.4 * mm, ident)
    # far end
    far1, far2 = far_end(cab, end)
    c.setFillColorRGB(0.35, 0.35, 0.35)
    c.setFont(BOLD, 4.6)
    c.drawString(tx, top - 12.3 * mm, 'FAR END')
    c.setFillColorRGB(0, 0, 0)
    fit(c, far1, BOLD, tw, 6.2)
    c.drawString(tx, top - 15.0 * mm, far1)
    fit(c, far2, REG, tw, 6.2)
    c.drawString(tx, top - 17.7 * mm, far2)
    # exact QR text, for debugging a misread
    c.setFillColorRGB(0.45, 0.45, 0.45)
    a, b = text.split('|A1610')
    fit(c, a + '|', REG, tw, 4.0)
    c.drawString(tx, top - 20.4 * mm, a + '|')
    fit(c, 'A1610' + b, REG, tw, 4.0)
    c.drawString(tx, top - 22.3 * mm, 'A1610' + b)
    c.setFillColorRGB(0, 0, 0)


def strip(c, x, y, cab, end):
    face(c, x, y, cab, end)
    wx = x + FACE_W
    c.setFillColorRGB(0.87, 0.87, 0.87)
    c.rect(wx, y, WRAP, H, stroke=0, fill=1)
    c.setFillColorRGB(0.4, 0.4, 0.4)
    c.setFont(REG, 4.6)
    c.drawCentredString(wx + WRAP / 2, y + H / 2 + 1.0 * mm, 'wrap round')
    c.drawCentredString(wx + WRAP / 2, y + H / 2 - 1.2 * mm, 'the cable')
    c.setStrokeColorRGB(0.5, 0.5, 0.5)
    c.setDash(0.6 * mm, 0.6 * mm)
    c.line(wx, y, wx, y + H)
    c.line(wx + WRAP, y, wx + WRAP, y + H)
    face(c, wx + WRAP, y, cab, end)
    # cut outline
    c.setStrokeColorRGB(0, 0, 0)
    c.setDash(1.5 * mm, 1 * mm)
    c.setLineWidth(0.3)
    c.rect(x, y, 2 * FACE_W + WRAP, H, stroke=1, fill=0)
    c.setDash()


def sheet(path):
    pw, ph = A4
    c = canvas.Canvas(path, pagesize=A4)
    c.setTitle('Installer Assistant cable flag labels (v2)')
    x = (pw - (2 * FACE_W + WRAP)) / 2
    c.setFont(BOLD, 10)
    c.drawString(x, ph - 14 * mm, 'Installer Assistant · cable flag labels (label v2)')
    c.setFont(REG, 6.8)
    lines = (
        'Print at 100 % (not "fit to page") on plain white paper or A4 sticker paper. Cut along the dashed outline.',
        'Lay the grey part across the cable, fold both faces back round it and stick them back to back. Cover with clear tape.',
        'READER END goes on the cable at the reader; CONTROLLER END at the A1610, where the cable\'s wires fan out,',
        'so the camera sees the label and the terminals in one picture. Use one pair per cable; keep spares out of view.',
        'C01 is the bench cable (Reader 1). C02 is the second cable: Reader 2 tests and the tangled shots.',
    )
    for i, t in enumerate(lines):
        c.drawString(x, ph - 19.5 * mm - i * 3.4 * mm, t)
    top = ph - 40 * mm
    order = [(CABLES[0], 'DEV'), (CABLES[0], 'CTRL'), (CABLES[0], 'DEV'), (CABLES[0], 'CTRL'),
             (CABLES[1], 'DEV'), (CABLES[1], 'CTRL'), (CABLES[1], 'DEV'), (CABLES[1], 'CTRL')]
    for i, (cab, end) in enumerate(order):
        strip(c, x, top - (i + 1) * H - i * 6.5 * mm, cab, end)
    c.showPage()
    c.save()


if __name__ == '__main__':
    sheet(sys.argv[1] if len(sys.argv) > 1 else 'flag_labels_v2_C01_C02.pdf')
