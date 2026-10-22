"""Random test wirings for the bench photos (package A, evaluation set).

Prints the shot list for one session: each line is a wiring to set up and photograph.
The list is also the answer key: it says which module and which pin each colour is on in those photos.
The cable's label always says READER1; shots "on READER2" test the zone check (keep Reader 1 in the photo too).
Shots marked "+ other cable" test the bundle check: a wire from a second, labelled cable (label C02) sits on a free pin
of the same module. It has the same colour as one of this cable's wires, because only then is colour no help.
Run from the repository root:  python ml\tools\random_wirings.py
"""
import random

SESSION = 1                      # change to 2 for the second session: gives a different list
N_CTRL = 12                      # controller wirings per session
N_RDR = 6                        # reader wirings per session

WIRES = ["red", "black", "green", "yellow"]           # the four wires on the bench cable
CTRL_PINS = ["1 -", "2 12V", "3 A", "4 B", "5 O1", "6 O2", "7 IO7", "8 IO8"]
RDR_TERMINALS = ["+", "-", "A", "B"]

# the wiring the rule expects at the controller, for this cable
CORRECT = {"red": "2 12V", "black": "1 -", "green": "3 A", "yellow": "4 B"}

# the cable's label says READER1; a wrong-module shot puts the whole cable on READER2 (same pin numbers there)
LABEL_MODULE = "READER1"
OTHER_MODULE = "READER2"


def swap(wiring):
    """Mistake 1: two wires exchange their pins."""
    a, b = random.sample(WIRES, 2)                 # pick two different wires
    wiring[a], wiring[b] = wiring[b], wiring[a]    # exchange their pins


def move(wiring):
    """Mistake 2: one wire goes to a pin that is still free."""
    a = random.choice(WIRES)
    empty_pins = []
    for pin in CTRL_PINS:
        if pin not in wiring.values():
            empty_pins.append(pin)
    wiring[a] = random.choice(empty_pins)


def realistic_wiring():
    """Start from the correct wiring, then make 0, 1 or 2 mistakes."""
    wiring = dict(CORRECT)                                        # a copy: start correct
    n = random.choices([0, 1, 2], weights=[20, 50, 30])[0]        # how many mistakes
    for _ in range(n):
        random.choice([swap, move])(wiring)                       # one mistake of a random kind
    return wiring


# Which shots test the zone check and the bundle check. A fixed number per session (not left to chance), so that
# every session, including the test session, has some; the shots themselves are drawn at random.
N_WRONG_MODULE = 2               # shots with the whole cable on READER2 while the label says READER1
N_TANGLED = 2                    # shots with a wire from another cable on a free pin of this module

module_random = random.Random(1000 + SESSION)    # its own dice, so the wirings above stay as in lists already printed
tangle_random = random.Random(2000 + SESSION)    # its own dice again, for the same reason

WRONG_MODULE_SHOTS = set(module_random.sample(range(1, N_CTRL + 1), N_WRONG_MODULE))
TANGLED_SHOTS = set(tangle_random.sample([i for i in range(1, N_CTRL + 1) if i not in WRONG_MODULE_SHOTS], N_TANGLED))


def module_for_shot(i):
    """Mistake 3: the whole cable sits in the wrong module, for the zone check."""
    return OTHER_MODULE if i in WRONG_MODULE_SHOTS else LABEL_MODULE


def tangle_for_shot(i, wiring):
    """Mistake 4: a wire from ANOTHER cable sits on a free pin of this module, for the bundle check.

    Returns (colour, pin) of the other cable's wire, or None for a normal shot. The colour is one of this cable's
    own colours, so the bundle check cannot tell the wires apart by colour. Never combined with a wrong-module shot,
    so each shot tests one check at a time.
    """
    if i not in TANGLED_SHOTS:
        return None
    free_pins = [pin for pin in CTRL_PINS if pin not in wiring.values()]
    return tangle_random.choice(WIRES), tangle_random.choice(free_pins)


# Pins 5-8 are printed differently on Reader 2 (O3 O4 IO9 IO10); pins 1-4 are the same on both reader blocks.
READER2_NAMES = {"5 O1": "5 O3", "6 O2": "6 O4", "7 IO7": "7 IO9", "8 IO8": "8 IO10"}


def pin_name(pin, module):
    """The pin as printed on the module the wire sits on."""
    return READER2_NAMES.get(pin, pin) if module == OTHER_MODULE else pin


random.seed(SESSION)                                              # same list every time for this session
print(f"SESSION {SESSION}")
print()
print("CONTROLLER")
for i in range(1, N_CTRL + 1):
    w = realistic_wiring()
    module = module_for_shot(i)
    other = tangle_for_shot(i, w)
    line = f"ctrl wiring {i:02d} on {module}: " + ", ".join(f"{c} → {pin_name(w[c], module)}" for c in WIRES)
    if other:
        line += f"   + other cable's {other[0]} → {pin_name(other[1], module)}"
    print(line)
print()
print("READER")
for i in range(1, N_RDR + 1):
    terminals = random.sample(RDR_TERMINALS, 4)
    print(f"rdr  wiring {i:02d}: " + ", ".join(f"{c} → {t}" for c, t in zip(WIRES, terminals)))