# bowls: review items the revision never saw

The design workflow passed only the first 12,000 characters of the independent review to the reviser, so the items below were never addressed. The build agent must handle each one: fix it, or explain in its report why not, with evidence. Low-severity items are optional. Full review: critiques/bowls-critique.json; revision report: critiques/bowls-revision.json.


## Item 10 [medium] spec

**what:** The spec says the contact phasors are renormalised each block, but the reference engine never does it. The phasor magnitude drifts, which skews the coupling to the higher modes during long meditative rubs, and a C# implementation that follows the spec will not match the FNV vectors.

**evidence:** spec §2.6 step 6: "Renormalise both phasors at each block start: e *= (3 − |e|²)/2". bowls-engine.mjs renderActive/renderBlock contains no renormalisation. critic/probes2.mjs J: |e|−1 after continuous rubbing every 30 s is big 0.0083, 0.017 … 0.051 at 180 s and small 0.016 … 0.102 at 180 s. The (9,0) coupling scales as |e|^9, so ×2.4 after 3 min.

**fix:** Add the renormalisation of (ec, es) and (tc, ts) at block start in renderBlock (when s.on), regenerate TV6–TV10 and the FNV hashes, and add a 10-minute rub test asserting ||e|−1| < 1e-5.


## Item 11 [medium] lessons

**what:** The "guaranteed win" lesson can be won without any audible ring, has no forgiving fallback for tap targets, and is described inaccurately.

**evidence:** §6.1 says any tap of any strength on the right bowl counts. Combined with the touch path, a gentle poke registers as a 0.04–0.2 m/s touch-down strike (−56 to −68 LUFS) and is then damped. Only rub targets have the 15 s give-up rule; the tour's piano step has `key_any` forgiving. The 'bowls-first' events are at 0, 1.5, 3, 6, 7.5, 9 and 12 s (gaps of 1.5 and 3 s), not "1.5 s apart".

**fix:** Count a hit only for a tap strike with V ≥ 0.2 m/s on the target bowl. After 8 s with no hit, Sunny re-demos. After 15 s, any touch on the target bowl plays an audible 0.7 m/s strike and counts, mirroring `key_any`. Correct the description.


## Item 12 [medium] lessons

**what:** Several Sunny tips work against the model, the main gesture (rubbing) is hard to discover, and the bowls sit outside the forward field of view.

**evidence:** - "Tap a bowl gently with one fingertip" pushes taps below the 0.25 m/s tap threshold (see the gentle-tap issue).
- "A little faster, like stirring tea" suggests about 2–3 turns/s, but the big bowl buzzes from 1.3 turns/s at 3–4 N (energy above 1.5 kHz at −18.8 dB against −33 dB when singing; critic/probes.mjs B) and the middle from 1.6 turns/s.
- tip_bowls_rub is P3, which sunny-tour §6 rations: not in the first 90 s, at most 3 per 10 min, 60 s apart.
- The bowls sit 48–67° left and 46–63° down, outside a forward view, so the 1.5 s gaze trigger rarely fires.
- The palm-strike feature (bowls.palm) has no tip.

**fix:** Change three lines:
- Arrival (P2, `InstrumentMoveEvent{Arrived}`): "Tap a bowl with one fingertip, then lift to let it ring."
- tip_bowls_rub: make it P2, firing 2 s after the 3rd tap.
- tip_bowls_faster: "A little faster: about one slow circle a second."
Add a P1 "The singing bowls are by your left knee" if the bowls are in the set but unplayed 120 s after the tour.


## Item 13 [medium] interaction

**what:** Rubbing on top of the rim, the natural wine-glass gesture, gives a LIP contact that starts slowly on the big bowl and can fail. No tip or test covers it.

**evidence:** critic/probes2.mjs I, time to singVel > 0.15 at 3.5 N:
- Big bowl, outer wall: 0.96–1.09 s.
- Big bowl, lip: 2.17 s at 0.2 m/s, 3.2 s at 0.3 m/s; at 0.45 m/s still not singing after 5.7 s (0.147 m/s).
- Middle bowl, lip: 0.77–2.49 s.
The first-timer case uses only the outer wall (yOff −8 mm).

**fix:** Run LIP contacts through the OUTER radial-contact model at the lip's outer edge (they share the start-up behaviour), or add a `tip_bowls_outside` trigger for lip rubs not singing after 2 s. Add a lip first-timer PlayMode case on all three bowls.


## Item 14 [medium] spec

**what:** A live rub and a looper playback rub on the same bowl fight over the unit's single contact slot. A played-back Release can also cut a live rub.

**evidence:** critic/loop.mjs (out-loop.txt): interleaved live and playback Contact streams on the middle bowl give 13.6 % contact loss (rattle) and a maximum sample step of 2.5e-2, about 3× a clean rub. The engine has one contact state per bowl, and §3 and §2.10 define no arbitration. A looped rub on its own is fine: it sings on all 5 passes for all bowls.

**fix:** Add an arbitration rule: a bowl with a live primary finger ignores playback Contact and Release (playback strikes still play). When the live finger lifts, playback resumes on its next Contact frame. Add a PlayMode test.


## Item 15 [medium] interaction

**what:** Rubbing must go all the way around the rim, but the far rims of the big and middle bowls are beyond the competition's seated 2 ft (0.61 m) reach.

**evidence:** dev/layout.mjs output, distance from the left shoulder to each far rim: big 0.69 m, middle 0.65 m, small 0.55 m.

**fix:** When re-placing the raised stand, put the big bowl in the nearest spot (or one row of three along the near edge), so every far rim is at most 0.61 m from the shoulder. Add the check to the layout test.


## Item 16 [low] visual

**what:** The rim-breathing highlight is not buildable as specified, and it is a visible exaggeration of motion that is invisible on a real bowl (0.1–0.7 mm).

**evidence:** §5.2 and §5.4 call for a template keyword `_RIM_BREATHE` with globals per bowl on a shared Bronze material. URP Lit has no such keyword, and ARCHITECTURE §14 forbids MaterialPropertyBlocks, so each bowl's identity would need a baked vertex attribute. Neither is specified; the custom shader `MusicRoom/BowlBronze` appears only for the optional 4-draw variant. A modulation of k·A20 ≤ 0.25 is plainly visible, while the user prefers physical realism.

**fix:** Either drop the effect, or specify `MusicRoom/BowlBronze` (Lit-equivalent PBR, bowl index in uv2.x, global `_BowlBreathe[3]`) with k·A20 ≤ 0.08 and default off. A more physical option for later: water in one bowl showing Faraday ripples when it sings (Terwagne & Bush 2011).


## Item 17 [low] interaction

**what:** The tap debounce in the code does not match the spec, and a quick double tap loses its second hit.

**evidence:** spec §4.1 says ≥ 70 ms per (finger, bowl). In interaction.mjs, `fresh = … || t − st.lastTap < TAP_HOLD + 0.02` blocks every strike by that finger on any bowl for 170 ms, and turns the re-entry into a damping contact. critic/hands.mjs case 4: a 150 ms double tap gives 3 contact frames and is −5.5 dB against a single tap.

**fix:** Use a 70 ms debounce per (finger, bowl). Apply the fresh window only to the same bowl, and never as a contact. Add a 150 ms double-tap PlayMode case that expects 2 strikes.


## Item 18 [low] sound

**what:** The steady rubbed tone is almost a pure sine plus a 4-per-turn pulse and hiss, which risks sounding like a synthesiser. No published rubbed spectrum was compared.

**evidence:** critic/rubspectrum.mjs on the delivered renders: 2f, 3f, (3,0), (4,0) and 4f all sit at −46 to −63 dB below the (2,0) peak on all three bowls; the rub noise is −31 dB. The render checks pass: −18.0 LUFS, true peak −8.5 to −14.6 dBTP, energy below 60 Hz −39 to −58 dB, 58.75 MB in total (out-renders.json).

**fix:** Render an A/B pair with a harder puja (Kc 1e6, a wood-like friction law) that brings (3,0) or 2f in at about −25 to −30 dB, and let the user choose by ear. Note in the README that the rub is intentionally nearly pure.


## Item 19 [low] interaction

**what:** The gaps between bowls are fine for a fingertip but tight for a hand.

**evidence:** dev/layout.mjs: gaps big–middle 3.9 cm, middle–small 3.5 cm. A hand is 8–9 cm wide, and any other fingertip entering the neighbouring bowl's band becomes a touch there (a tick, then damping).

**fix:** Use ≥ 6 cm gaps once the stand is re-laid out. Add a PlayMode case rubbing the middle bowl on the side facing the small one with a full 5-tip hand: no calls to the small bowl.


## Item 20 [low] spec

**what:** The determinism tolerances under Burst conflict with the project rules.

**evidence:** bowls.md §9.1 allows 1e-3 × peak (linear) and 2 dB block RMS (nonlinear). ENGINE-API §6 and platform §10.3 require 1e-5 under Burst. The designer measured 1.1e-4 absolute fused deviation.

**fix:** State the exception explicitly, with the reason (long-T60 phase drift plus friction branch flips), and get the platform owner's sign-off in CONTRACT-REQUESTS; or compare Burst against a Burst-strict reference with FMA contraction off.


## Item 21 [low] spec

**what:** interaction.mjs runs its whole test suite when imported, so any reuse rewrites out/interaction-cases.json.

**evidence:** Importing BowlsHands from my critic scripts re-ran the 11 cases and rewrote the lab's out/interaction-cases.json. The output is deterministic (sha1 351cf42a… before and after), so the content is unchanged.

**fix:** Move the cases to interaction-cases.mjs and keep interaction.mjs a pure module.
