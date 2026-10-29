# Report figures (package A)

Made by `python -m ml.eval.report_figures` from the data and the code; nothing in them is typed in. Each figure is
saved as PDF (vector, for LaTeX: `\includegraphics[width=\columnwidth]{...}`) and PNG (300 dpi). Sizes fit an IEEE
two-column page: `locate_cdf` and `augmentation` are one column (3.5 in) wide, the others are the full page width
(`figure*`). Every number below comes from `numbers.json` next to the figures. Session 1 only; session 2 stays
sealed. Re-run the script after new data or code; cached runs live in `ml/runs/report/<os>/` (`--fresh` redoes them).

Draft captions (adapt the wording; the numbers are current):

**pipeline** — From photo to pin patches. (a) A board photo with the 24 pins found by ORB matching and a MAGSAC++
homography (39 matches), refined per terminal block by template matching. (b) The photo
warped into the template's view; every pin gets the same window where its wire leaves. (c) The patches of the Reader 1
block, top to bottom, which the pin reader classifies.

**locate_cdf** — How far the found pins are from the hand-marked ones, in pin spacings (session 1, frames at 1920 px).
Half a spacing (dotted) still picks the right opening. The homography alone puts 13.9 %
of board pins within half a spacing, because the terminal blocks stand above the board (parallax); refining each block
raises this to 98.2 %. The reader end (DISK + LightGlue) reaches 88.6 %
of the slots of the 22 photos it found (2 of 24 were refused as "not found").

**ablation** — Pin-reader changes tried on session 1, each measured with whole wiring rows held out (7 folds):
the base model reads 97.2 % of 1344 board pins right and misses
34 of 252 wires; brightness features plus augmentation (shipped) reads
98.7 % and misses 13, with 0 false wires.
A taller window did not help and was dropped. Right panel: wrong answers given with confidence at least 0.8.

**confusion** — Confusion matrices of the board pins, base (a) and shipped (b), rows = true, cells shaded by row share.
Most of the gain is black and green wires that the base model took for empty pins.

**confidence** — (a) Reliability of the shipped model's confidence before and after temperature scaling
(T = 0.53; ECE 0.035 -> 0.005); bins in the middle hold few pins.
(b) Share of wrong readings among the pins answered, as the confidence threshold is lowered. At 0.8 (dots) the shipped
model answers 97.5 % of pins with 0.8 % wrong; the base model answers
94.6 % with 0.9 % wrong. The rest goes to the installer.

**examples** — (a) Pins the refinement fixed (19; it broke 0), mostly dark or green wires read as
"no wire" before. (b) The 18 pins still wrong, most confident first: tangled wires crossing another pin's
window, a second wire in the same pin (top left, the U1 two-wire pin), and wires leaving at a steep angle.
7 of the 12 shown sit at Reader 1 A or B, where several wires cross.

**augmentation** — One patch and the six training copies made from it (shifted 2 px, turned 6 degrees about the pin),
cut from a patch with a 6 px margin. Copies are added only on the training side of each fold.

**importance** — What the shipped board model uses. (a) Importance of each number of the shrunk patch: colour (a*)
where the wire leaves the opening dominates; the new edge features pick up the wire's borders just right of it.
(b) Shares by feature group: L*a*b* grid 79 %, edges 10 %,
relative L* 6 %, percentiles 4 %, dark share per third < 1 % (unused).

Reader end (not drawn; 88 slots, all wired): 80.7 % right with both models; ECE
0.082 -> 0.031; answered at 0.8: 53.4 % -> 61.4 %.

Made with: {'os': 'linux', 'opencv': '5.0.0', 'scikit-learn': '1.7.2', 'session': 'session1'}. The official set is the one re-run on Yu's laptop.
