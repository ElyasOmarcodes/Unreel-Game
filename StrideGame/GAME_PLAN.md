# Tropical Strike — game plan

Third-person action shooter for Android, built with the Stride engine (MIT) and CC0 assets.

## Phase 1 — playable action game (this milestone)
- **Screens:** own splash ("TROPICAL STRIKE"), main menu (Play, Settings, Quit) over a flying
  camera, settings (graphics quality, look sensitivity, sound volume, invert Y, FPS counter —
  saved on the phone), pause menu, victory / defeat screens with retry.
- **Weapons:** AK-47, M4A1, Pistol — damage, fire rate, magazine, reserve ammo, spread, recoil,
  reload time; weapon switch, muzzle flash, gunshot/reload/hit sounds, hit markers.
- **Enemies:** 8 hostile soldiers (patrol → chase → shoot, health, death).
- **Player:** health with slow regeneration, damage flash, over-the-shoulder aiming camera
  (character faces where you aim), gun animations (idle / run).
- **Mission:** "Clear the factory" — eliminate all hostiles; energy cells give health + ammo.
- **Touch HUD:** move stick, look by dragging, FIRE (hold + drag to aim), JUMP, RELOAD,
  weapon switch, pause, crosshair, ammo, health bar, hostiles left, timer.

## Phase 2 — more combat
- Shotgun and sniper (scope zoom), grenades, explosive barrels, blood/impact particles,
  enemy variety (heavy, sniper), headshots, footstep/ambient sounds, music.

## Phase 3 — progression
- Several missions / waves, score and stars, unlocking weapons, saved progress,
  difficulty levels, better character models.

## Phase 4 — polish & size
- Weapon/character art pass, LODs, smaller APK (texture compression tuning, shader trimming),
  performance profiles per device.
