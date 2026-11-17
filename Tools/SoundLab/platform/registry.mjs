// Platform reference data: the instruments as the platform sees them (seven since 2026-11-10, when one was retired) (Docs/12-new-instruments/platform.md).
// Units: metres, degrees; the SEAT FRAME is Unity's: head at (0, 1.2, 0), +z ahead, +x right, yaw +90 = to the right.
// A rig pose is (x, z, yaw); a rig-local point (lx, lz) lands at
//   (x + lx cos(yaw) + lz sin(yaw), z - lx sin(yaw) + lz cos(yaw))       (Unity Quaternion.Euler(0, yaw, 0)).
// Footprint boxes are rig-local {cx, cz, hx, hz, y0, y1, air?}: y0/y1 are heights above the floor (the rig's own y is
// already applied). "air" boxes are play space (a field the hands play in): they keep other instruments out but not the player.
//
// The three existing instruments use their homes from ARCHITECTURE.md section 8 (canonical, tested today) and boxes fitted
// to the instrument data of Docs/05-instruments.md (piano.build, drums.pieces, harp.build). The new ones use realistic sizes; their
// instrument agents replace the numbers with their real Rig.Handles bounds (the conformance test checks within 3 cm).
// Plain data, no dependencies.

export const SLOTS = {
  Front: 0, FrontRight: 45, Right: 90, FrontLeft: -45, Left: -90, RightRear: 120, LeftRear: -120,
};

// The carousel (page turn, the menu grid): each neighbour shares one property with the next (platform.md section 4.3).
export const CAROUSEL = ['piano', 'harp', 'bowls', 'handpan', 'marimba', 'handdrums', 'drums'];

// Planning priority: big and hard-to-place first, so small instruments fill what is left.
export const PRIORITY = ['piano', 'drums', 'marimba', 'harp', 'handdrums', 'handpan', 'bowls'];

function drumBoxes() {
  // drums.pieces (Unity, drums-local, head plane at y): shells / cymbals + their stands.
  const P = [
    ['snare', 0.18, 0.14, 0.00, 0.72, 0.30],
    ['tom1', 0.15, 0.20, -0.04, 0.86, 0.625],   // 2026-11-14: spaced from the snare (C# DrumSpec.Pieces)
    ['tom2', 0.165, 0.22, 0.32, 0.85, 0.482],
    ['floor', 0.20, 0.36, 0.38, 0.62, 0.02],
    ['hat', 0.17, 0, -0.42, 0.80, 0.36],
    ['crash', 0.21, 0, -0.39, 1.02, 0.58],
    ['ride', 0.22, 0, 0.48, 1.04, 0.27],
  ];
  const b = [];
  for (const [, r, depth, x, y, z] of P) {
    if (depth > 0) b.push({ cx: x, cz: z, hx: r + 0.01, hz: r + 0.01, y0: y - depth, y1: y + 0.03 });
    else b.push({ cx: x, cz: z, hx: r, hz: r, y0: y - 0.03, y1: y + 0.03 });
    b.push({ cx: x, cz: z, hx: 0.05, hz: 0.05, y0: 0, y1: Math.max(0.05, y - (depth || 0.05)) });   // stand pole (the splayed feet may pass under the bench)
  }
  b.push({ cx: 0.12, cz: 0.90, hx: 0.30, hz: 0.21, y0: 0, y1: 0.59 });      // kick, on its side (measured shell z 0.70-1.107)
  b.push({ cx: 0.12, cz: 0.54, hx: 0.05, hz: 0.11, y0: 0, y1: 0.06 });      // kick pedal
  b.push({ cx: -0.29, cz: 0.19, hx: 0.10, hz: 0.10, y0: 0.57, y1: 0.63 });  // kick pad
  b.push({ cx: -0.29, cz: 0.19, hx: 0.03, hz: 0.03, y0: 0.0, y1: 0.57 });   // its post
  return b;
}

export const INSTRUMENTS = {
  piano: {
    name: 'Piano', canonicalSlot: 'Front', home: { x: 0, z: 0.45, yaw: 0 }, single: { x: 0, z: 0.45, yaw: 0 },
    prefs: ['Front', 'Left', 'Right'], reachMax: 0.62, playCentre: { x: 0, z: 0.05 },
    boxes: [
      { cx: 0, cz: 0.15, hx: 0.66, hz: 0.17, y0: 0.62, y1: 0.95 },          // keys + case + rest
      { cx: -0.58, cz: 0.15, hx: 0.04, hz: 0.15, y0: 0, y1: 0.62 },         // stand sides
      { cx: 0.58, cz: 0.15, hx: 0.04, hz: 0.15, y0: 0, y1: 0.62 },
    ],
    stowFoot: { w: 1.34, d: 0.36 },
  },
  drums: {
    name: 'Drums', canonicalSlot: 'Right', home: { x: 0.2, z: -0.26, yaw: 90 }, single: { x: 0, z: 0.1, yaw: 0 },
    prefs: ['Right', 'Left'], reachMax: 0.70, playCentre: { x: 0, z: 0.3 },
    boxes: drumBoxes(),
    stowFoot: { w: 1.37, d: 1.31 },
  },
  harp: {
    name: 'Harp', canonicalSlot: 'Left', home: { x: -0.78, z: -0.2, yaw: 83 }, single: { x: -0.2966, z: 0.45, yaw: 180 },
    prefs: ['Left', 'Right', 'FrontLeft', 'FrontRight'], reachMax: 0.95, playCentre: { x: -0.33, z: 0 },
    boxes: [
      { cx: -0.33, cz: 0, hx: 0.48, hz: 0.11, y0: 0, y1: 0.08 },            // base + feet
      { cx: -0.33, cz: 0, hx: 0.45, hz: 0.06, y0: 0.08, y1: 1.48 },         // pillar, soundboard, neck (crest 1.476 m), strings
    ],
    stowFoot: { w: 0.98, d: 0.26 },
  },
  marimba: {
    // the built model (phase B, MarimbaDefinition.SeatBoxes): bar tops 0.72 m, home (0, 0.47); the top above 0.62 m, the
    // tubes and legs below it 3.5 cm beyond the knee box; the hands' field as two air boxes (they keep the marimba out of
    // sets with the kit: its hi-hat would stand under the right hand)
    name: 'Marimba', canonicalSlot: 'Front', home: { x: 0, z: 0.47, yaw: 0 }, single: { x: 0, z: 0.47, yaw: 0 },
    prefs: ['Front', 'Left', 'Right'], reachMax: 0.80, playCentre: { x: 0, z: 0.05 },
    boxes: [
      { cx: -0.105, cz: 0.18, hx: 0.525, hz: 0.445, y0: 0.62, y1: 0.76 },  // top x -0.63..0.42: bars, rails, tray, mallets, bass board
      { cx: 0.525, cz: 0.145, hx: 0.105, hz: 0.275, y0: 0.62, y1: 0.76 },  // top, treble end x 0.42..0.63 (z -0.13..0.42)
      { cx: 0, cz: 0.36, hx: 0.21, hz: 0.26, y0: 0, y1: 0.62 },            // under the middle: folded tube runs, stretcher
      { cx: -0.42, cz: 0.30, hx: 0.21, hz: 0.32, y0: 0, y1: 0.62 },        // under the bass half: tubes
      { cx: 0.42, cz: 0.30, hx: 0.21, hz: 0.32, y0: 0, y1: 0.62 },         // under the treble half: straight tubes
      { cx: -0.605, cz: 0.215, hx: 0.035, hz: 0.41, y0: 0, y1: 0.62 },     // bass end board and legs
      { cx: 0.605, cz: 0.15, hx: 0.035, hz: 0.25, y0: 0, y1: 0.62 },       // treble end board and legs
      { cx: 0, cz: 0.25, hx: 0.64, hz: 0.37, y0: 0.76, y1: 1.00, air: true },   // air: the heads' lift over the bars
      { cx: 0, cz: -0.24, hx: 0.50, hz: 0.12, y0: 0.76, y1: 1.05, air: true },  // air: the grips in front of the bars
    ],
    stowFoot: { w: 1.25, d: 0.88 },
  },

  handdrums: {
    // handdrums.md v2 section 4.3 (FINAL); phase B (conformance check 3 on the
    // built model): the hoops to 0.81 m, box B 3.5 cm deeper at the back (the basket stand's back foot at z 0.324)
    name: 'Hand drums', canonicalSlot: 'Front', home: { x: 0, z: 0.57, yaw: 0 }, single: { x: 0, z: 0.57, yaw: 0 },
    prefs: ['FrontRight', 'FrontLeft', 'Right', 'Left', 'Front'], reachMax: 0.62, playCentre: { x: 0, z: -0.05 },
    boxes: [
      { cx: 0.005, cz: 0, hx: 0.36, hz: 0.165, y0: 0.62, y1: 0.81 },       // heads and hoops (may pass over the knees)
      { cx: 0.005, cz: 0.1025, hx: 0.36, hz: 0.2275, y0: 0, y1: 0.62 },    // shells + basket stands (z -0.125..0.33)
    ],
    stowFoot: { w: 0.74, d: 0.50 },
  },

  handpan: {
    // handpan.md rev 2 (6.6, 7.1) with the review's raised placement (critiques/handpan-unaddressed.md item 9): the rim
    // centre at (0, 0.74, 0.38), tilted 28 deg; shell x +-0.265, z +-0.234 about the rig origin, lowest point 0.616 m (at
    // the near edge, over the knees: the box starts at the knee box's top). The stand rises behind the knees (its column
    // and legs set back, the pan cantilevered over the lap): the handpan agent owns the real stand.
    name: 'Handpan', canonicalSlot: 'Front', home: { x: 0, z: 0.38, yaw: 0 }, single: { x: 0, z: 0.38, yaw: 0 },
    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0, z: 0 },
    // phase B: the real cradle tripod replaces the set-back placeholder stand; the shell and its band 7 mm wider
    boxes: [
      { cx: 0, cz: 0, hx: 0.272, hz: 0.240, y0: 0.62, y1: 0.88 },          // the shell and its rubber band, tilted 28 deg
      { cx: 0, cz: 0.1875, hx: 0.315, hz: 0.1275, y0: 0, y1: 0.62 },       // the cradle tripod below the rim: z 0.06..0.315
    ],
    stowFoot: { w: 0.53, d: 0.53 },
  },

  bowls: {
    // bowls.md rev 2 section 3.2 (RIG.seat, final): in the pocket left of the knees
    name: 'Singing bowls', canonicalSlot: 'FrontLeft', home: { x: -0.46, z: 0.25, yaw: 0 }, single: { x: -0.46, z: 0.25, yaw: 0 },
    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0, z: -0.016 },
    boxes: [
      { cx: 0, cz: 0, hx: 0.205, hz: 0.145, y0: 0, y1: 0.61 },             // low wooden stand
      { cx: 0, cz: 0, hx: 0.205, hz: 0.145, y0: 0.61, y1: 0.72 },          // bowls, cushions, puja
    ],
    stowFoot: { w: 0.51, d: 0.18 },
  },

};

// Always at the seat: the side table (deck + switches), never stowed. It stays at its home (today's, Left) unless a set
// needs the Left slot: then it yields to its alternate pose, behind-left (0.66 m from the head, deck still facing the
// player; the looper's LOOP / REC stay a seated twist away).
export const TABLE = {
  home: { x: -0.39, z: -0.21, yaw: -90 },
  alternate: { x: -0.25, z: -0.62, yaw: 180 },
  boxes: [
    { cx: 0.04, cz: 0, hx: 0.22, hz: 0.17, y0: 0, y1: 0.68 },
  ],
};

// The seated player (obstacles for planned instruments, never for "air" boxes).
export const PLAYER = [
  { name: 'torso', cx: 0, cz: -0.05, hx: 0.20, hz: 0.15, y0: 0.45, y1: 1.50 },
  { name: 'knees', cx: 0, cz: 0.25, hx: 0.22, hz: 0.16, y0: 0.40, y1: 0.62 },
  { name: 'seat', cx: 0, cz: -0.10, hx: 0.30, hz: 0.17, y0: 0.44, y1: 0.54 },   // where the player sits (the bench ends may have stand feet beside them)
];

export const GAP = 0.03;          // clearance between two instruments' boxes (m)
export const PUSHES = [0, 0.05, 0.10, 0.15];   // radial push-outs tried per slot (m)
export const MAX_SET = 3;          // the product rule (platform.md 4.2); the planner itself works for any size
// The VR room box (Unity, ARCHITECTURE 5): instruments must stay inside it at the default seat.
// ROOM: the room box in the room frame (stow spots, furniture). Since 2026-11-11 the VR room faces the fire (C# RoomFrame,
// a half turn about the seat, slid 0.5 m back): the seat planner's box in the seat frame is ROOM_SEAT (seat-planner.mjs re-run with it for
// platform.vectors.json: every plan unchanged, only candidate counts).
export const ROOM = { x0: -3.2, x1: 3.2, z0: -4.08, z1: 1.12, inset: 0.02 };
export const ROOM_SEAT = { x0: -3.2, x1: 3.2, z0: -1.62, z1: 3.58, inset: 0.02 };
