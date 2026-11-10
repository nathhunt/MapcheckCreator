# Gantry–couch collision check

Warns (never blocks) when the treatment head comes close to the couch top for a QA isocentre, and suggests a lateral
offset of the phantom on the couch that would clear it. Shown on Array planning (each plan card and the Adjust array
panel), on Review (current position, and the position after a chosen fine-tune move) and in the Create / Move
confirmations.

## Model (`src/SrsMcPsqa.Core/Collision/CouchCollision.cs`)

Room frame with the isocentre at the origin, aligned with the phantom's DICOM axes at couch 0: x = phantom left,
y = posterior (down), z = superior (towards the gantry).

- **Head:** a cylinder of radius *R* around the beam axis, from *D* (head face) to *D* + 300 mm from the isocentre.
  Gantry θ (IEC 61217) puts the source at (sin θ, −cos θ, 0): 0° above, 90° on the phantom's left (HFS).
- **Couch top:** a box *W* wide and *T* thick, top surface *H* below the MapCHECK user origin, centred laterally on the
  phantom (unless offset), from *E* superior of the origin to 2 m towards the feet. It rotates with the couch angle φ
  about the vertical axis through the isocentre, counter-clockwise seen from above for positive φ (IEC 61217).
- **Not modelled:** the phantom, the imaging arms (assumed retracted), couch rails and accessories.
- The head surface is sampled (25 mm face grid, rings every 6°), gantry angles every 1°; clearances are approximate to a
  few mm, which the warning margins absorb.

Clearance per beam = smallest gap between head and couch over its gantry angles. Below **Warning** (default 50 mm):
amber; below **Risk** (default 20 mm) or overlapping: red. When below Warning, the app tries lateral phantom offsets in
5 mm steps up to ±*max* (default 50 mm) and suggests the smallest one that clears the warning level, or the best one
available.

## Settings (Settings → Gantry–couch collision check; department file `Collision*` fields)

| Setting | Default | Notes |
|---|---|---|
| MapCHECK user origin height above couch top (*H*) | **not set** | Required: the check is off until entered. |
| Couch head end, superior of the origin (*E*) | 200 | Negative if the phantom overhangs the head end. |
| Phantom lateral offset on the couch | 0 | + = towards the phantom's left. |
| Largest lateral offset to suggest | 50 | |
| Warning / risk clearance | 50 / 20 | |
| Head face distance from iso (*D*) | 380 | Conservative approximation for TrueBeam: **check the vendor specification**. |
| Head radius (*R*) | 400 | Conservative approximation: **check**. |
| Couch top width / thickness (*W*, *T*) | 530 / 60 | Approximation for Exact IGRT: **check**. |

## Before relying on it

1. Enter *H* and *E* for your phantom setup, and check the head and couch dimensions.
2. **Check the couch rotation direction against your machine:** with couch 90 the model puts the couch body on the
   phantom's left side at couch 0 (+x), so gantry ~90° collides and gantry ~270° is free. If your machine is the other way
   round, tell me: it's a one-line sign change.
3. Compare a few known cases (a plan you know collides, one you know is clear) with the app's numbers.

## References

- IEC 61217:2011, *Radiotherapy equipment – Coordinates, movements and scales* (gantry and patient support angle conventions).
- Humm JL et al., "Collision detection and avoidance during treatment planning", *Int J Radiat Oncol Biol Phys* 1995;33(5):1101–1108.
- Hua C et al., "A practical approach to prevent gantry–couch collision for linac-based radiosurgery", *Med Phys* 2004;31(7):2128–2134.
