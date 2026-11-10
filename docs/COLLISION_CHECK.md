# Gantry–couch collision check

Warns (never blocks) when the treatment head comes close to the couch top for a QA isocentre, and suggests a lateral
offset of the phantom on the couch that would clear it. Shown on Array planning (each plan card and the Adjust array
panel), on Review (current position, and the position after a chosen fine-tune move) and in the Create / Move
confirmations.

## Model (`src/SrsMcPsqa.Core/Collision/CouchCollision.cs`)

Room frame with the isocentre at the origin, aligned with the phantom's DICOM axes at couch 0: x = phantom left,
y = posterior (down), z = superior (towards the gantry).

- **Head:** a cylinder of radius *R* around the beam axis, from *D* (head face) to *D* + 200 mm from the isocentre.
  Gantry θ (IEC 61217) puts the source at (sin θ, −cos θ, 0): 0° above, 90° on the phantom's left (HFS).
- **Couch top:** a box *W* wide and *T* thick, top surface *H* below the MapCHECK user origin, centred laterally on the
  phantom (unless offset), from *E* superior of the origin to 2 m towards the feet. It rotates with the couch angle φ
  about the vertical axis through the isocentre, counter-clockwise seen from above for positive φ (IEC 61217).
- **Not modelled:** the phantom, the imaging arms (assumed retracted), couch rails and accessories.
- Distance: head surface samples (25 mm face grid, rings every 6°) against the couch box, plus couch edge samples (10 mm)
  against the head cylinder computed exactly; the smaller wins. Gantry angles every 1° between control points, in the
  arc's direction (a conformal arc may have only start and stop control points).

Clearance per beam = smallest gap between head and couch over its gantry angles. Below **Warning** (default 50 mm):
amber; below **Risk** (default 20 mm) or overlapping: red. When below Warning, the app tries lateral phantom offsets in
5 mm steps up to ±*max* (default 50 mm) and suggests the smallest one that clears the warning level, or the best one
available.

## Verification (2026-11-10)

- **Conventions match two independent ESAPI collision scripts.** Head face centre in patient coordinates (HFS) for
  gantry θ and couch φ: iso + d·(cos φ·sin θ, −cos θ, −sin φ·sin θ), exactly as in LDClark/PlanCheck
  (`CollisionCalculator.AddCylinderToMesh`); zackmorelli/CollisionCheck uses the same source position
  (1000·sin θ, −1000·cos θ) and couch-rotation sign. Unit test `HeadPositionMatchesOtherEsapiCollisionScripts` places a
  2 mm box at that point for four gantry/couch pairs and checks it touches the head (and is far away with φ mirrored).
- **Accuracy of the distance:** against a brute-force reference (2 mm face grid, 0.5° rings) over 140 random isocentre
  shifts, gantry and couch angles: within +0.6 / −0.1 mm when clear, and the same collide/clear verdict in every
  overlapping case.
- **Hand-calculated cases** (unit tests): lateral and posterior fields, lateral/posterior shifts, couch kicks, conformal
  arcs with two control points, lateral offset suggestions.
- **Not verifiable here:** the machine dimensions (see below) and the couch rotation direction on your machine; compare
  with a known case.

## Settings (Settings → Gantry–couch collision check; department file `Collision*` fields)

| Setting | Default | Notes |
|---|---|---|
| MapCHECK user origin height above couch top (*H*) | **not set** | Required: the check is off until entered. |
| Couch head end, superior of the origin (*E*) | 200 | Negative if the phantom overhangs the head end. |
| Phantom lateral offset on the couch | 0 | + = towards the phantom's left. |
| Largest lateral offset to suggest | 50 | |
| Warning / risk clearance | 50 / 20 | |
| Head face distance from iso (*D*) | 415 | 41.5 cm, as used by two open-source ESAPI collision scripts for Varian C-arm linacs (see below). |
| Head radius (*R*) | 385 | Head 76.5 cm across (CollisionCheck) / 75 cm hub (PlanCheck), rounded up. |
| Couch top width / thickness (*W*, *T*) | 530 / 60 | Width: Exact IGRT 53 cm. Thickness: estimate, **check** (measure, or the Eclipse couch model). |

### Where the head values come from

- [zackmorelli/CollisionCheck](https://github.com/zackmorelli/CollisionCheck) (ESAPI, "designed using the physical
  dimensions of Varian C-Arm linacs"): head face 585 mm from the source at SAD 1000 mm, i.e. 41.5 cm from the isocentre;
  head modelled as a disc of radius 382.5 mm (76.5 cm); alerts below 5 cm.
- [LDClark/PlanCheck](https://github.com/LDClark/PlanCheck) (ESAPI, distances "from a Trilogy"): iso to collimator
  face 41.5 cm; 47 cm metal face plate and 75 cm plastic hub (20 cm deep); warning below 4 cm, collision below 3 cm.
- Neither is a Varian specification; the 5 cm warning margin covers the difference. A published prone-breast clearance
  study on TrueBeam used 39.4 cm from the head to the isocentre when checking clearance in the planning system
  ([Med Dosim 2021](https://www.sciencedirect.com/science/article/abs/pii/S0958394721000248)); if you prefer that more
  cautious figure, enter 394.

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
