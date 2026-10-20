"""Random test wirings for the bench photos (package A, evaluation set).

Prints the shot list for one session: each line is a wiring to set up and photograph.
The list is also the answer key: it says which module and which pin each colour is on in those photos.
The cable's label always says READER1; shots "on READER2" test the zone check (keep Reader 1 in the photo too).
Run from the repository root:  python ml\tools\random_wirings.py
"""
import random

SESSION = 1                      # change to 2 for the second session: gives a different list
N_CTRL = 12                      # controller wirings per session
N_RDR = 6                        # reader wirings per session

WIRES = ["orange", "black", "green", "yellow"]           # the four wires on the bench cable
CTRL_PINS = ["1 -", "2 12V", "3 A", "4 B", "5 O1", "6 O2", "7 IO7", "8 IO8"]
RDR_TERMINALS = ["+", "-", "A", "B"]

# the wiring the rule expects at the controller, for this cable
CORRECT = {"orange": "2 12V", "black": "1 -", "green": "3 A", "yellow": "4 B"}

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


module_random = random.Random(1000 + SESSION)    # its own dice, so the wirings above stay as in lists already printed


def module_for_shot():
    """Mistake 3: the whole cable sits in the wrong module (about 1 shot in 6), for the zone check."""
    return module_random.choices([LABEL_MODULE, OTHER_MODULE], weights=[5, 1])[0]


random.seed(SESSION)                                              # same list every time for this session
print(f"SESSION {SESSION}")
print()
print("CONTROLLER")
for i in range(1, N_CTRL + 1):
    w = realistic_wiring()
    module = module_for_shot()
    print(f"ctrl wiring {i:02d} on {module}: " + ", ".join(f"{c} → {w[c]}" for c in WIRES))
print()
print("READER")
for i in range(1, N_RDR + 1):
    terminals = random.sample(RDR_TERMINALS, 4)
    print(f"rdr  wiring {i:02d}: " + ", ".join(f"{c} → {t}" for c, t in zip(WIRES, terminals)))